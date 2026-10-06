using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Shapers.Assist.Application;

namespace Shapers.Assist.Infrastructure;

/// <summary>
/// Azure OpenAI (chat completions, v1 API) and Azure AI Speech (fast transcription), called over REST.
/// Signs in with the API's managed identity unless a key is configured.
/// </summary>
internal sealed class AzureAiProvider(IHttpClientFactory httpClients, IOptions<AssistOptions> options) : IAiProvider
{
    public const string HttpClientName = "assist";

    private const string CognitiveScope = "https://cognitiveservices.azure.com/.default";
    private static readonly TokenCredential Credential = new DefaultAzureCredential();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private AssistOptions.AzureOptions Azure => options.Value.Azure;

    public string ChatModel => Azure.ChatDeployment;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var endpoint = Azure.OpenAiEndpoint ?? throw new AiProviderException("assist.not_configured", "AI help isn't set up yet (Assist:Azure:OpenAiEndpoint).");
        var body = new JsonObject
        {
            ["model"] = Azure.ChatDeployment,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.User }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = request.SchemaName, ["strict"] = true, ["schema"] = request.Schema.DeepClone() },
            },
            ["max_completion_tokens"] = request.MaxOutputTokens,
        };
        if (!string.IsNullOrWhiteSpace(Azure.ReasoningEffort))
        {
            body["reasoning_effort"] = Azure.ReasoningEffort;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/openai/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        await AuthoriseAsync(message, Azure.ApiKey, "api-key", cancellationToken);
        using var document = await SendAsync(message, cancellationToken);

        var root = document.RootElement;
        var choice = root.GetProperty("choices")[0];
        var finish = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() : null;
        var reply = choice.GetProperty("message");
        if (reply.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
        {
            throw new AiProviderException("assist.refused", "The AI service declined this request.");
        }

        if (finish == "length")
        {
            throw new AiProviderException("assist.too_long", "The answer was cut off because it was too long. Try again, or shorten the text.");
        }

        if (finish == "content_filter")
        {
            throw new AiProviderException("assist.filtered", "The AI service's content filter stopped this answer.");
        }

        var content = reply.GetProperty("content").GetString() ?? string.Empty;
        var usage = root.TryGetProperty("usage", out var u) ? u : default;
        return new ChatResponse(
            content,
            root.TryGetProperty("model", out var model) ? model.GetString() ?? ChatModel : ChatModel,
            usage.ValueKind == JsonValueKind.Object ? usage.GetProperty("prompt_tokens").GetInt32() : 0,
            usage.ValueKind == JsonValueKind.Object ? usage.GetProperty("completion_tokens").GetInt32() : 0);
    }

    public async Task<TranscriptResponse> TranscribeAsync(Stream audio, string fileName, string contentType, string locale, CancellationToken cancellationToken)
    {
        var endpoint = Azure.SpeechEndpoint ?? throw new AiProviderException("assist.not_configured", "Transcription isn't set up yet (Assist:Azure:SpeechEndpoint).");
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(audio);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "audio", fileName);
        var definition = new JsonObject { ["locales"] = new JsonArray(locale), ["profanityFilterMode"] = "Masked" };
        form.Add(new StringContent(definition.ToJsonString(), Encoding.UTF8, "application/json"), "definition");

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/speechtotext/transcriptions:transcribe?api-version=2024-11-15") { Content = form };
        await AuthoriseAsync(message, Azure.SpeechKey, "Ocp-Apim-Subscription-Key", cancellationToken);
        using var document = await SendAsync(message, cancellationToken);

        var root = document.RootElement;
        var text = root.TryGetProperty("combinedPhrases", out var phrases) && phrases.GetArrayLength() > 0
            ? string.Join("\n\n", phrases.EnumerateArray().Select(p => p.GetProperty("text").GetString()).Where(t => !string.IsNullOrWhiteSpace(t)))
            : string.Empty;
        var milliseconds = root.TryGetProperty("durationMilliseconds", out var d) ? d.GetInt64() : 0;
        if (text.Length == 0)
        {
            throw new AiProviderException("assist.no_speech", "No speech was found in the audio.");
        }

        return new TranscriptResponse(text, (int)Math.Ceiling(milliseconds / 1000d), "azure-speech-fast");
    }

    private static async Task AuthoriseAsync(HttpRequestMessage message, string? key, string keyHeader, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            message.Headers.Add(keyHeader, key);
            return;
        }

        var token = await Credential.GetTokenAsync(new TokenRequestContext([CognitiveScope]), cancellationToken);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClients.CreateClient(HttpClientName).SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException("assist.unreachable", "The AI service couldn't be reached. Try again in a few minutes.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("assist.timeout", "The AI service took too long. Try again.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderException(
                    response.StatusCode == HttpStatusCode.TooManyRequests ? "assist.busy" : "assist.service_failed",
                    response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "The AI service is busy. Try again in a minute."
                        : string.Create(CultureInfo.InvariantCulture, $"The AI service returned an error ({(int)response.StatusCode})."),
                    new HttpRequestException(text.Length > 500 ? text[..500] : text));
            }

            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new AiProviderException("assist.bad_output", "The AI service gave an answer we couldn't read. Try again.", ex);
            }
        }
    }
}

/// <summary>Canned answers for development and tests. Remembers the last request so tests can check what would be sent.</summary>
public sealed class FakeAiProvider : IAiProvider
{
    public ChatRequest? LastChat { get; private set; }

    public string ChatModel => "fake";

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        LastChat = request;
        var json = request.SchemaName switch
        {
            "sermon_lesson" => """
                {"title":"Faith that works","summary":"Faith shows itself in what we do.","keyVerses":["James 2:14-17"],"icebreaker":"What's one thing you did for someone this week?","questions":["What stood out to you from Sunday?","Where is your faith hardest to live out?","Who could you help this week?"],"application":"Do one practical kindness for a neighbour.","prayerFocus":"Pray for courage to act on what we believe."}
                """,
            "sermon_notes" => """
                {"summary":"Faith that only talks is not enough: this message calls us to live it out.","notes":"## Faith acts\n- James 2:14-17","topics":["Faith","Service"]}
                """,
            "rewrite" => """{"text":"Join us on Sunday at 09:00."}""",
            _ => "{}",
        };
        return Task.FromResult(new ChatResponse(json, ChatModel, request.User.Length / 4, json.Length / 4));
    }

    public Task<TranscriptResponse> TranscribeAsync(Stream audio, string fileName, string contentType, string locale, CancellationToken cancellationToken) =>
        Task.FromResult(new TranscriptResponse($"Fake transcript of {fileName}. Faith without works is dead.", 60, "fake"));
}

/// <summary>Registered when AI is switched off. The gateway refuses before it gets here.</summary>
internal sealed class DisabledAiProvider : IAiProvider
{
    public string ChatModel => "none";

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken) =>
        throw new AiProviderException("assist.disabled", "AI help is switched off.");

    public Task<TranscriptResponse> TranscribeAsync(Stream audio, string fileName, string contentType, string locale, CancellationToken cancellationToken) =>
        throw new AiProviderException("assist.disabled", "AI help is switched off.");
}
