using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Shapers.Api.Hosting;

/// <summary>
/// A crash in the admin portal or the member app. Only what helps fix the bug: which app and page, and the error.
/// The apps strip personal details before sending (packages/api-client/src/client-errors.ts); the API strips them
/// again with <see cref="ClientErrorScrubber"/> before logging, so nothing about a person reaches the logs.
/// </summary>
public sealed record ClientErrorReport(string App, string? Version, string? Route, string? ErrorType, string? Message, string? Stack);

/// <summary>Removes anything that could identify a person from text an app sent, and keeps it short.</summary>
public static partial class ClientErrorScrubber
{
    public static string? Scrub(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = QueryString().Replace(text, "$1");
        clean = Email().Replace(clean, "[email]");
        clean = Guid().Replace(clean, "[id]");
        clean = Jwt().Replace(clean, "[token]");
        clean = LongToken().Replace(clean, "[token]");
        clean = PhoneOrNumber().Replace(clean, "[number]");
        clean = clean.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength] + "…";
    }

    /// <summary>A page's address with ids and numbers replaced, so it names the page and not the record.</summary>
    public static string? Route(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var path = route.Split('?', '#')[0];
        var segments = path.Split('/').Select(s => Guid().IsMatch(s) || (s.Length > 0 && s.All(char.IsAsciiDigit)) ? ":id" : s);
        return Scrub(string.Join('/', segments), 200);
    }

    /// <summary>Keeps the first lines of a stack trace: enough to find the code, not the whole call chain.</summary>
    public static string? Stack(string? stack)
    {
        var clean = Scrub(stack, 2000);
        return clean is null ? null : string.Join('\n', clean.Split('\n').Take(12).Select(l => l.TrimEnd()));
    }

    /// <summary>Query strings and fragments, stopping at a colon so a stack line's ":line:column" survives.</summary>
    [GeneratedRegex(@"(https?://[^\s?#'""()]+)[?#][^\s'""():]*")]
    private static partial Regex QueryString();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex Guid();

    [GeneratedRegex(@"\beyJ[\w-]+\.[\w-]+\.[\w-]+")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b[A-Za-z0-9_-]{32,}\b")]
    private static partial Regex LongToken();

    /// <summary>Phone numbers and ID numbers: seven or more digits, allowing spaces, dashes and brackets between.</summary>
    [GeneratedRegex(@"\+?\d[\d ()-]{5,}\d")]
    private static partial Regex PhoneOrNumber();
}

internal static partial class ClientErrors
{
    public const string RateLimitPolicy = "client-errors";
    private const long MaxBodyBytes = 16 * 1024;
    private static readonly HashSet<string> Apps = ["admin", "member-app"];

    public static IEndpointRouteBuilder MapClientErrors(this IEndpointRouteBuilder endpoints)
    {
        // Anyone may report, signed in or not: a crash on the sign-in page matters too. The rate limit, the size
        // limit and the scrubbing are what keep it safe; nothing is stored except the log line.
        endpoints.MapPost("/api/client-errors", ReportAsync)
            .Accepts<ClientErrorReport>("application/json")
            .WithTags("Client errors")
            .WithName("ReportClientError")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);
        return endpoints;
    }

    private static async Task<Results<NoContent, ValidationProblem, StatusCodeHttpResult>> ReportAsync(
        HttpContext context, IOptions<JsonOptions> json, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Read at most one byte past the limit, whatever the request claims about its length.
        var buffer = new byte[MaxBodyBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        if (length > MaxBodyBytes)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        ClientErrorReport? report;
        try
        {
            report = JsonSerializer.Deserialize<ClientErrorReport>(buffer.AsSpan(0, length), json.Value.SerializerOptions);
        }
        catch (JsonException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Send the report as JSON."] });
        }

        if (report is null || !Apps.Contains(report.App))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["app"] = ["Unknown app."] });
        }

        LogClientError(
            loggers.CreateLogger("Shapers.Api.ClientErrors"),
            report.App,
            ClientErrorScrubber.Scrub(report.Version, 40) ?? "unknown",
            ClientErrorScrubber.Route(report.Route) ?? "unknown",
            ClientErrorScrubber.Scrub(report.ErrorType, 100) ?? "Error",
            ClientErrorScrubber.Scrub(report.Message, 500) ?? "",
            ClientErrorScrubber.Stack(report.Stack) ?? "");
        return TypedResults.NoContent();
    }

    // The alert in infra/azure/main.bicep matches the start of this message; keep the two in step.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Client error in {App} {AppVersion} at {Route}: {ErrorType}: {ErrorMessage}\n{Stack}")]
    private static partial void LogClientError(ILogger logger, string app, string appVersion, string route, string errorType, string errorMessage, string stack);
}
