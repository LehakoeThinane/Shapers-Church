using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Shapers.Giving.Application;

namespace Shapers.Giving.Infrastructure;

/// <summary>The church's Yoco account. Both keys live in Key Vault.</summary>
public sealed class YocoOptions
{
    public const string SectionName = "Giving:Yoco";

    /// <summary>The secret key from the Yoco portal (sk_live_… or sk_test_…).</summary>
    public string? SecretKey { get; set; }

    /// <summary>The webhook signing secret Yoco returns when the webhook is registered (whsec_…).</summary>
    public string? WebhookSecret { get; set; }

    public string BaseUrl { get; set; } = "https://payments.yoco.com/";
}

/// <summary>
/// Yoco Checkout: the giver pays on Yoco's own page, and Yoco tells us the result through a signed webhook.
/// Webhooks are signed the Standard Webhooks way: HMAC-SHA256 of "id.timestamp.body" with the secret.
/// </summary>
public sealed class YocoPaymentProvider(HttpClient http, IOptions<YocoOptions> options, TimeProvider clock) : IPaymentProvider
{
    /// <summary>How old a webhook may be, so a captured one can't be replayed later.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.SecretKey) && !string.IsNullOrWhiteSpace(options.Value.WebhookSecret);

    public async Task<CheckoutStarted> StartCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkouts")
        {
            Content = JsonContent.Create(new
            {
                amount = request.AmountCents,
                currency = request.Currency,
                successUrl = request.SuccessUrl,
                cancelUrl = request.CancelUrl,
                failureUrl = request.FailureUrl,
                metadata = new { giftId = request.GiftId.ToString() },
            }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);
        // The same gift never creates two checkouts, even if the request is retried.
        message.Headers.Add("Idempotency-Key", request.GiftId.ToString());

        using var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var checkout = await response.Content.ReadFromJsonAsync<Checkout>(cancellationToken)
            ?? throw new InvalidOperationException("Yoco returned an empty checkout.");
        return new CheckoutStarted(checkout.Id, checkout.RedirectUrl);
    }

    public PaymentNotice? ReadNotice(IReadOnlyDictionary<string, string> headers, string body)
    {
        if (!headers.TryGetValue("webhook-id", out var id) || !headers.TryGetValue("webhook-timestamp", out var timestamp)
            || !headers.TryGetValue("webhook-signature", out var signatures) || !IsSigned(id, timestamp, body, signatures))
        {
            return null;
        }

        Event? evt;
        try
        {
            evt = JsonSerializer.Deserialize<Event>(body);
        }
        catch (JsonException)
        {
            return null;
        }

        if (evt?.Payload?.Metadata?.CheckoutId is not { Length: > 0 } checkoutId)
        {
            return null;
        }

        return evt.Type switch
        {
            "payment.succeeded" => new PaymentNotice(checkoutId, evt.Payload.Id, evt.Payload.Amount, Succeeded: true),
            "payment.failed" => new PaymentNotice(checkoutId, evt.Payload.Id, evt.Payload.Amount, Succeeded: false),
            _ => null,
        };
    }

    /// <summary>True when one of the signatures matches and the timestamp is recent.</summary>
    public bool IsSigned(string id, string timestamp, string body, string signatures)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || (clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Tolerance)
        {
            return false;
        }

        var secret = options.Value.WebhookSecret ?? "";
        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        foreach (var part in signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = part.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0 || part[..comma] != "v1")
            {
                continue;
            }

            byte[] given;
            try
            {
                given = Convert.FromBase64String(part[(comma + 1)..]);
            }
            catch (FormatException)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(given, expected))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Checkout([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("redirectUrl")] string RedirectUrl);

    private sealed record Metadata([property: JsonPropertyName("checkoutId")] string? CheckoutId);

    private sealed record Payload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("metadata")] Metadata? Metadata);

    private sealed record Event([property: JsonPropertyName("type")] string? Type, [property: JsonPropertyName("payload")] Payload? Payload);
}
