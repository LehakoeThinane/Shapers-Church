using System.Net;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shapers.Platform.Email;

public sealed record EmailMessage(string To, string Subject, string PlainText, string Html);

/// <summary>
/// Sends transactional email (verification codes, confirmations, tickets). Marketing and bulk messages
/// belong to the Communications module, which will respect consent and opt-outs.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Log" (development: writes to the console) or "Azure" (Azure Communication Services, Africa data location).</summary>
    public string Provider { get; set; } = "Log";

    public string? ConnectionString { get; set; }

    /// <summary>Verified sender, e.g. DoNotReply@mail.shaperschurch.com.</summary>
    public string? From { get; set; }
}

internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogEmail(logger, message.To, message.Subject, message.PlainText);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DEV EMAIL to {To}: {Subject}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}

internal sealed class AzureEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailClient _client = new(options.Value.ConnectionString ?? throw new InvalidOperationException("Email:ConnectionString is required."));
    private readonly string _from = options.Value.From ?? throw new InvalidOperationException("Email:From is required.");

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var content = new EmailContent(message.Subject) { PlainText = message.PlainText, Html = message.Html };
        await _client.SendAsync(WaitUntil.Started, new Azure.Communication.Email.EmailMessage(_from, message.To, content), cancellationToken);
    }
}

public static class EmailRegistration
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        var provider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Log";
        if (provider.Equals("Azure", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, AzureEmailSender>();
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            throw new InvalidOperationException("Email:Provider 'Log' is for development only. Configure Azure Communication Services.");
        }

        return services;
    }
}

/// <summary>A plain, readable email layout that works in every client, including on small screens.</summary>
public static class EmailLayout
{
    public static string Html(string heading, IEnumerable<string> paragraphs, string? buttonText = null, string? buttonUrl = null)
    {
        var body = string.Concat(paragraphs.Select(p => $"<p style=\"margin:0 0 14px\">{WebUtility.HtmlEncode(p)}</p>"));
        var button = buttonText is null || buttonUrl is null
            ? string.Empty
            : $"<p style=\"margin:20px 0\"><a href=\"{WebUtility.HtmlEncode(buttonUrl)}\" style=\"display:inline-block;padding:12px 20px;border-radius:999px;background:#3F3230;color:#EBE6E4;text-decoration:none;font-weight:600\">{WebUtility.HtmlEncode(buttonText)}</a></p>";
        return "<!doctype html><html><body style=\"margin:0;padding:24px;background:#EBE6E4;font-family:-apple-system,Segoe UI,Roboto,sans-serif;color:#3A2E2B\">"
            + "<div style=\"max-width:520px;margin:0 auto;background:#ffffff;border-radius:16px;padding:24px\">"
            + $"<h1 style=\"font-size:20px;margin:0 0 16px\">{WebUtility.HtmlEncode(heading)}</h1>{body}{button}"
            + "<p style=\"margin:24px 0 0;font-size:12px;color:#6b5f5c\">Shapers Church · 8 Mellis Road, Rivonia · info@shaperschurch.com</p>"
            + "</div></body></html>";
    }
}
