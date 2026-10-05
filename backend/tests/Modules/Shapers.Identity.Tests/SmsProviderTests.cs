using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shapers.Identity.Application;
using Shapers.Identity.Infrastructure;

namespace Shapers.Identity.Tests;

/// <summary>Sign-in codes must never end up in production logs, whatever the SMS setting says.</summary>
public sealed class SmsProviderTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Log_provider_refuses_to_start_outside_development(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register("Log", environment));

        Assert.Contains("development only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_provider_refuses_to_start_instead_of_falling_back_to_logging()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register("Twilo", "Production"));

        Assert.Contains("not supported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_provider_starts_in_production_and_uses_the_disabled_sender()
    {
        var services = Register("Disabled", "Production");

        var registration = Assert.Single(services, d => d.ServiceType == typeof(ISmsSender));
        Assert.Equal(typeof(DisabledSmsSender), registration.ImplementationType);
    }

    [Fact]
    public async Task Disabled_sender_tells_the_member_instead_of_sending_the_code()
    {
        var sender = new DisabledSmsSender();

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => sender.SendAsync("+27820000000", "123456 is your code", CancellationToken.None));

        Assert.Equal("identity.sms_unavailable", error.Code);
        Assert.DoesNotContain("123456", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_provider_is_allowed_in_development()
    {
        var services = Register("Log", Environments.Development);

        var registration = Assert.Single(services, d => d.ServiceType == typeof(ISmsSender));
        Assert.Equal(typeof(LoggingSmsSender), registration.ImplementationType);
    }

    private static ServiceCollection Register(string provider, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sms:Provider"] = provider,
                ["ConnectionStrings:Shapers"] = "Host=localhost",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddIdentityInfrastructure(configuration, new TestEnvironment(environment));
        return services;
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Shapers.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
