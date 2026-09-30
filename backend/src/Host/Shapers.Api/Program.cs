using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Shapers.Api.Hosting;
using Shapers.Church.Api;
using Shapers.Identity.Api;
using Shapers.Events.Api;
using Shapers.Media.Api;
using Shapers.Prayer.Api;
using Shapers.Communications.Api;
using Shapers.Privacy.Api;
using Shapers.Content.Api;
using Shapers.People.Api;
using Shapers.Platform;
using Shapers.Platform.Email;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

// Build-time OpenAPI generation loads this program without a database or secrets; skip anything that connects.
var generatingOpenApi = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = generatingOpenApi ? Environments.Development : null,
});

if (generatingOpenApi)
{
    builder.Configuration["Jobs:Enabled"] = "false";
    builder.Configuration["Outbox:Enabled"] = "false";
    builder.Configuration["Database:MigrateOnStartup"] = "false";
    builder.Configuration["ConnectionStrings:Shapers"] ??= "Host=localhost";
}

// Initialisation order matters (see DatabaseInitialiser).
IModule[] modules = [new ChurchModule(), new PeopleModule(), new IdentityModule(), new MediaModule(), new EventsModule(), new PrayerModule(), new CommunicationsModule(), new PrivacyModule(), new ContentModule()];

builder.Services.AddPlatform(builder.Configuration);
foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration, builder.Environment);
}

builder.Services.AddEmail(builder.Configuration, builder.Environment);
builder.Services.AddShapersJobs(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ShapersExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var perMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);
    o.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Behind Cloudflare / Azure the client IP arrives in X-Forwarded-For. Trusted proxies are configured per environment.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("shapers-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation())
    .UseOtlpExporterWhenConfigured(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseCsrfProtection();
app.UseAuthorization();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference("/docs");
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });

app.MapPlatformEndpoints();
foreach (var module in modules)
{
    module.MapEndpoints(app);
}

if (!generatingOpenApi)
{
    await app.InitialiseAsync(modules);
    app.UseShapersJobs();
}

await app.RunAsync();

public partial class Program;

internal static class OpenTelemetryExtensions
{
    public static IOpenTelemetryBuilder UseOtlpExporterWhenConfigured(this IOpenTelemetryBuilder builder, IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]) ? builder : builder.UseOtlpExporter();
}
