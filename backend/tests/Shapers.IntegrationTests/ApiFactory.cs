using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Communications.Application;
using Shapers.Identity.Application;
using Shapers.Media.Application;
using Shapers.Platform.Email;
using Testcontainers.PostgreSql;

namespace Shapers.IntegrationTests;

/// <summary>Runs the real API against a throwaway PostgreSQL container.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "correct-horse-battery-staple";
    public const string Csrf = "X-Shapers-CSRF";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), "shapers-tests", Guid.NewGuid().ToString("N"));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public CapturingSmsSender Sms { get; } = new();

    public CapturingEmailSender Email { get; } = new();

    public CapturingPushSender Push { get; } = new();

    protected virtual bool RequireMfa => false;

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_mediaPath))
        {
            Directory.Delete(_mediaPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Shapers", _postgres.GetConnectionString());
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("Auth:BootstrapAdmin:Email", AdminEmail);
        builder.UseSetting("Auth:BootstrapAdmin:Password", AdminPassword);
        builder.UseSetting("Auth:Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Auth:Otp:HashKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Auth:Security:RequireMfaForSensitivePermissions", RequireMfa ? "true" : "false");
        builder.UseSetting("Security:HashKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("RateLimits:AuthPerMinute", "10000");
        builder.UseSetting("Media:Storage:LocalPath", _mediaPath);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISmsSender>(Sms);
            services.AddSingleton<IEmailSender>(Email);
            services.AddSingleton<IPushSender>(Push);
            services.AddSingleton<IYouTubeClient, FakeYouTubeClient>();
        });
    }

    /// <summary>A browser-like client: keeps cookies and talks HTTPS so Secure cookies are sent.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    public async Task<HttpClient> SignInStaffAsync(string email, string password)
    {
        var client = Browser();
        var response = await client.PostAsJsonAsync("/api/auth/staff/login", new { email, password });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add(Csrf, "1");
        return client;
    }

    public Task<HttpClient> SignInAdminAsync() => SignInStaffAsync(AdminEmail, AdminPassword);
}

public sealed class MfaApiFactory : ApiFactory
{
    protected override bool RequireMfa => true;
}

public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentDictionary<string, string> _lastMessage = new();

    public Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken)
    {
        _lastMessage[phoneE164] = message;
        return Task.CompletedTask;
    }

    public string LastCodeFor(string phoneE164) => _lastMessage[phoneE164][..6];
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Verification emails start with the code: "123456 is your Shapers Church code".</summary>
    public string LastCodeFor(string email) => _sent.Last(m => m.To == email && m.Subject.EndsWith("is your Shapers Church code", StringComparison.Ordinal)).Subject[..6];
}

/// <summary>Records pushes instead of sending them. Tokens containing "gone" behave like an uninstalled app.</summary>
public sealed class CapturingPushSender : IPushSender
{
    private readonly ConcurrentQueue<PushMessage> _sent = new();

    public IReadOnlyList<PushMessage> Sent => [.. _sent];

    public Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var results = new List<PushResult>();
        foreach (var m in messages)
        {
            if (m.Token.Contains("gone", StringComparison.Ordinal))
            {
                results.Add(new PushResult(false, null, true, "DeviceNotRegistered"));
            }
            else
            {
                _sent.Enqueue(m);
                results.Add(new PushResult(true, $"ticket-{Guid.NewGuid():N}", false, null));
            }
        }

        return Task.FromResult<IReadOnlyList<PushResult>>(results);
    }
}

internal static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json);
}

/// <summary>Stands in for the YouTube Data API: two videos in the shapes the church actually uses.</summary>
public sealed class FakeYouTubeClient : IYouTubeClient
{
    public bool IsConfigured => true;

    public Task<IReadOnlyList<YouTubeVideo>> ListChannelVideosAsync(string channelId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<YouTubeVideo>>(
        [
            new("y0jPz7KFw_o", "Psalm 42: 1-11 Deep calls unto deep", new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero), null),
            new("abcdefghijk", "Faith that works", new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero), null),
        ]);
}
