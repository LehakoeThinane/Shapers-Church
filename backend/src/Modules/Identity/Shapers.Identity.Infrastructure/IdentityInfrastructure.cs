using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shapers.Identity.Application;
using Shapers.Identity.Contracts;
using Shapers.People.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;

namespace Shapers.Identity.Infrastructure;

public static class IdentityInfrastructure
{
    /// <summary>Picks JWT for requests carrying a bearer token (mobile) and the cookie otherwise (admin portal).</summary>
    public const string SmartScheme = "shapers";

    public const string CsrfHeader = "X-Shapers-CSRF";

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName, typeof(IUserDirectory).Assembly);
        services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.Configure<IdentitySecurityOptions>(configuration.GetSection(IdentitySecurityOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<BootstrapAdminOptions>(configuration.GetSection(BootstrapAdminOptions.SectionName));
        services.AddSingleton(sp => new IdentityKeys(
            sp.GetRequiredService<IOptions<JwtOptions>>(),
            sp.GetRequiredService<IOptions<OtpOptions>>(),
            environment.IsDevelopment(),
            sp.GetRequiredService<ILogger<IdentityKeys>>()));

        services.AddSingleton<IPermissionProvider, IdentityPermissionProvider>();
        services.AddScoped<GrantLoader>();
        services.AddScoped<IAuthorizer, ScopedAuthorizer>();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IOtpHasher, HmacOtpHasher>();
        services.AddScoped<OtpLoginService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AccessService>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IIntegrationEventHandler<PeopleMergedIntegrationEvent>, RelinkUserOnPeopleMerged>();

        // Data minimisation: sign-in codes and dead sessions are only kept as long as they're useful for support.
        services.AddSingleton(new RecurringJobDefinition("identity-credentials-cleanup", "15 3 * * *", async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
            await db.OtpChallenges.Where(c => c.CreatedAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
            await db.RefreshTokens.Where(t => t.ExpiresAt < now.AddDays(-30)).ExecuteDeleteAsync(ct);
        }));

        var smsProvider = configuration["Sms:Provider"] ?? "Log";
        if (smsProvider == "Log" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Sms:Provider 'Log' is for development only. Configure a real SMS provider.");
        }

        services.AddScoped<ISmsSender, LoggingSmsSender>();

        services
            .AddIdentityCore<User>(o =>
            {
                // NIST 800-63B: length over composition rules.
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ShapersClaimsPrincipalFactory>();

        services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = SmartScheme;
                o.DefaultChallengeScheme = SmartScheme;
            })
            .AddPolicyScheme(SmartScheme, "Bearer or cookie", o =>
                o.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : IdentityConstants.ApplicationScheme)
            .AddJwtBearer()
            .AddIdentityCookies();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IdentityKeys, IOptions<JwtOptions>>((o, keys, jwt) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = keys.SigningKey,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ShapersClaims.Subject,
                };
            });

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = environment.IsDevelopment() ? "shapers.admin" : "__Host-shapers.admin";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;

            // An API never redirects to a login page.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

        services.AddAuthorization();
        return services;
    }

    public static async Task InitialiseIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedRolesAsync(cancellationToken);

        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value;
        await seeder.SeedBootstrapAdminAsync(bootstrap, environment.IsDevelopment(), cancellationToken);
    }
}
