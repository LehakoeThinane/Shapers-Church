using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Identity.Application;
using Shapers.Identity.Contracts;
using Shapers.Identity.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Text;
using Shapers.Platform.Web;

namespace Shapers.Identity.Api;

public sealed record StaffSignInRequest(string Email, string Password, string? TwoFactorCode, string? RecoveryCode);

public enum StaffLoginStatus
{
    SignedIn,
    TwoFactorRequired,
}

public sealed record StaffLoginResponse(StaffLoginStatus Status);

public sealed record SetPasswordRequest(Guid UserId, string Token, string Password);

public sealed record LogoutRequest(string RefreshToken);

public sealed record TwoFactorStatus(bool Enabled, int RecoveryCodesLeft);

public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

public sealed record EnableTwoFactorRequest(string Code);

public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

public sealed class IdentityModule : IModule
{
    private static readonly Error BadCredentials = Error.Unauthorized("identity.bad_credentials", "Email or password is incorrect.");

    public string Name => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddIdentityInfrastructure(configuration, environment);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseIdentityAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapMemberAuth(endpoints.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth));
        MapStaffAuth(endpoints.MapGroup("/api/auth").WithTags("Auth"));
        MapMe(endpoints.MapGroup("/api/me").WithTags("My account").RequireAuthorization());
        MapAdmin(endpoints.MapGroup("/api/admin").WithTags("Access").RequireAuthorization());
    }

    private static void MapMemberAuth(RouteGroupBuilder auth)
    {
        auth.MapPost("/otp/request", async (RequestCodeRequest request, OtpLoginService service, HttpContext http, CancellationToken ct) =>
                (await service.RequestCodeAsync(request, http.Connection.RemoteIpAddress?.ToString(), ct)).ToHttp())
            .WithName("RequestSignInCode");

        auth.MapPost("/otp/verify", async (VerifyCodeRequest request, OtpLoginService service, CancellationToken ct) =>
                (await service.VerifyCodeAsync(request, ct)).ToHttp())
            .WithName("VerifySignInCode");

        auth.MapPost("/register", async (RegisterRequest request, OtpLoginService service, CancellationToken ct) =>
                (await service.RegisterAsync(request, ct)).ToHttp())
            .WithName("Register");

        auth.MapPost("/refresh", async (RefreshRequest request, SessionService service, CancellationToken ct) =>
                (await service.RefreshAsync(request, ct)).ToHttp())
            .WithName("RefreshSession");

        auth.MapPost("/logout", async (LogoutRequest request, SessionService service, CancellationToken ct) =>
            {
                await service.RevokeAsync(request.RefreshToken, ct);
                return TypedResults.NoContent();
            })
            .WithName("Logout");
    }

    private static void MapStaffAuth(RouteGroupBuilder auth)
    {
        auth.MapPost("/staff/login", StaffLoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("StaffLogin");

        auth.MapPost("/staff/logout", async (SignInManager<User> signIn) =>
            {
                await signIn.SignOutAsync();
                return TypedResults.NoContent();
            })
            .WithName("StaffLogout");

        auth.MapPost("/staff/set-password", async Task<Results<NoContent, ProblemHttpResult>> (SetPasswordRequest request, UserManager<User> users) =>
            {
                var user = await users.FindByIdAsync(request.UserId.ToString());
                var result = user is null
                    ? IdentityResult.Failed(new IdentityError { Description = "This link is invalid or has expired." })
                    : await users.ResetPasswordAsync(user, request.Token, request.Password);
                return result.Succeeded
                    ? TypedResults.NoContent()
                    : new Error("identity.password_rejected", string.Join(" ", result.Errors.Select(e => e.Description))).ToProblem();
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("SetStaffPassword");

        var twoFactor = auth.MapGroup("/2fa").RequireAuthorization();

        twoFactor.MapGet("/", async Task<Results<Ok<TwoFactorStatus>, UnauthorizedHttpResult>> (ClaimsPrincipal principal, UserManager<User> users) =>
                await users.GetUserAsync(principal) is { } user
                    ? TypedResults.Ok(new TwoFactorStatus(user.TwoFactorEnabled, await users.CountRecoveryCodesAsync(user)))
                    : TypedResults.Unauthorized())
            .WithName("GetTwoFactorStatus");

        twoFactor.MapPost("/setup", async Task<Results<Ok<AuthenticatorSetup>, UnauthorizedHttpResult>> (ClaimsPrincipal principal, UserManager<User> users) =>
            {
                if (await users.GetUserAsync(principal) is not { } user)
                {
                    return TypedResults.Unauthorized();
                }

                await users.ResetAuthenticatorKeyAsync(user);
                var key = (await users.GetAuthenticatorKeyAsync(user))!;
                var label = Uri.EscapeDataString(user.Email ?? user.UserName!);
                var uri = $"otpauth://totp/Shapers%20Church:{label}?secret={key}&issuer=Shapers%20Church&digits=6";
                return TypedResults.Ok(new AuthenticatorSetup(FormatKey(key), uri));
            })
            .WithName("SetupAuthenticator");

        twoFactor.MapPost("/enable", async Task<Results<Ok<RecoveryCodes>, UnauthorizedHttpResult, ProblemHttpResult>> (EnableTwoFactorRequest request, ClaimsPrincipal principal, UserManager<User> users, SignInManager<User> signIn) =>
            {
                if (await users.GetUserAsync(principal) is not { } user)
                {
                    return TypedResults.Unauthorized();
                }

                var code = request.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
                if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
                {
                    return new Error("identity.code_invalid", "That code isn't right. Check the time on your phone and try again.").ToProblem();
                }

                await users.SetTwoFactorEnabledAsync(user, true);
                var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

                // The code just proved the second factor, so upgrade this session instead of forcing a new sign-in.
                await signIn.SignInWithClaimsAsync(user, isPersistent: false, [new Claim(ShapersClaims.AuthenticationMethod, ShapersClaims.Mfa)]);
                return TypedResults.Ok(new RecoveryCodes(codes?.ToList() ?? []));
            })
            .WithName("EnableTwoFactor");
    }

    private static async Task<Results<Ok<StaffLoginResponse>, ProblemHttpResult>> StaffLoginAsync(StaffSignInRequest request, UserManager<User> users, SignInManager<User> signIn)
    {
        if (!ContactNormaliser.TryNormaliseEmail(request.Email, out var email) || string.IsNullOrEmpty(request.Password))
        {
            return BadCredentials.ToProblem();
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            return BadCredentials.ToProblem();
        }

        var password = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (password.IsLockedOut)
        {
            return new Error("identity.locked_out", "Too many failed attempts. Try again in 15 minutes.", ErrorKind.RateLimited).ToProblem();
        }

        if (!password.Succeeded)
        {
            return BadCredentials.ToProblem();
        }

        var methods = new List<Claim> { new(ShapersClaims.AuthenticationMethod, "pwd") };
        if (user.TwoFactorEnabled)
        {
            var secondFactorOk = false;
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                var code = request.TwoFactorCode.Replace(" ", string.Empty, StringComparison.Ordinal);
                secondFactorOk = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code);
            }
            else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
            {
                secondFactorOk = (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode.Trim())).Succeeded;
            }
            else
            {
                return TypedResults.Ok(new StaffLoginResponse(StaffLoginStatus.TwoFactorRequired));
            }

            if (!secondFactorOk)
            {
                await users.AccessFailedAsync(user);
                return new Error("identity.code_invalid", "That code isn't right.", ErrorKind.Unauthorized).ToProblem();
            }

            methods.Add(new Claim(ShapersClaims.AuthenticationMethod, ShapersClaims.Mfa));
        }

        await users.ResetAccessFailedCountAsync(user);
        await signIn.SignInWithClaimsAsync(user, isPersistent: false, methods);
        return TypedResults.Ok(new StaffLoginResponse(StaffLoginStatus.SignedIn));
    }

    private static void MapMe(RouteGroupBuilder me)
    {
        me.MapGet("/access", async (AccessService service, CancellationToken ct) => (await service.GetMyAccessAsync(ct)).ToHttp())
            .WithName("GetMyAccess");

        me.MapPut("/preferences", async (PreferencesRequest request, AccessService service, CancellationToken ct) =>
                (await service.SetPreferencesAsync(request, ct)).ToHttp())
            .WithName("SetMyPreferences");
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("/permissions", (AccessService service) => service.ListPermissions())
            .WithName("ListPermissions")
            .RequirePermission(IdentityPermissions.UsersView);

        admin.MapGet("/roles", (AccessService service, CancellationToken ct) => service.ListRolesAsync(ct))
            .WithName("ListRoles")
            .RequirePermission(IdentityPermissions.UsersView);

        admin.MapPost("/roles", async (SaveRoleRequest request, AccessService service, CancellationToken ct) =>
                (await service.CreateRoleAsync(request, ct)).ToCreated(r => $"/api/admin/roles/{r.Id}"))
            .WithName("CreateRole")
            .RequirePermission(IdentityPermissions.RolesManage);

        admin.MapPut("/roles/{id:guid}", async (Guid id, SaveRoleRequest request, AccessService service, CancellationToken ct) =>
                (await service.UpdateRoleAsync(id, request, ct)).ToHttp())
            .WithName("UpdateRole")
            .RequirePermission(IdentityPermissions.RolesManage);

        admin.MapGet("/roles/{id:guid}", async (Guid id, AccessService service, CancellationToken ct) =>
                (await service.GetRoleAsync(id, ct)).ToHttp())
            .WithName("GetRole")
            .RequirePermission(IdentityPermissions.UsersView);

        admin.MapGet("/roles/{id:guid}/people", async (Guid id, AccessService service, CancellationToken ct) =>
                (await service.ListRoleHoldersAsync(id, ct)).ToHttp())
            .WithName("ListRoleHolders")
            .RequirePermission(IdentityPermissions.UsersView);

        admin.MapPost("/roles/{id:guid}/reset", async (Guid id, AccessService service, CancellationToken ct) =>
                (await service.ResetRoleAsync(id, ct)).ToHttp())
            .WithName("ResetRole")
            .RequirePermission(IdentityPermissions.RolesManage);

        admin.MapGet("/people/{personId:guid}/access", async (Guid personId, AccessService service, CancellationToken ct) =>
                (await service.GetPersonAccessAsync(personId, ct)).ToHttp())
            .WithName("GetPersonAccess")
            .RequirePermission(IdentityPermissions.UsersView);

        admin.MapPost("/people/{personId:guid}/staff-login", async (Guid personId, StaffLoginRequestDto request, AccessService service, CancellationToken ct) =>
                (await service.PrepareStaffLoginAsync(personId, new StaffLoginRequest(request.Email), ct)).ToHttp())
            .WithName("PrepareStaffLogin")
            .RequirePermission(IdentityPermissions.GrantsManage);

        admin.MapPost("/grants", async (CreateGrantRequest request, AccessService service, CancellationToken ct) =>
                (await service.CreateGrantAsync(request, ct)).ToHttp())
            .WithName("CreateGrant")
            .RequirePermission(IdentityPermissions.GrantsManage);

        admin.MapDelete("/grants/{id:guid}", async (Guid id, AccessService service, CancellationToken ct) =>
                (await service.RevokeGrantAsync(id, ct)).ToHttp())
            .WithName("RevokeGrant")
            .RequirePermission(IdentityPermissions.GrantsManage);
    }

    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return builder.ToString().TrimEnd().ToLowerInvariant();
    }
}

public sealed record StaffLoginRequestDto(string Email);
