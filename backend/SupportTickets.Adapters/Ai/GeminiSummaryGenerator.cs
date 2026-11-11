using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Ai;

/// <summary>
/// AI summary via Google Gemini's REST API (<c>models/{model}:generateContent</c>). Plain <see cref="HttpClient"/>
/// instead of an SDK: the whole integration is one request and one JSON path, readable end to end.
/// </summary>
public sealed class GeminiSummaryGenerator(HttpClient http, GeminiOptions options, ILogger<GeminiSummaryGenerator> logger)
    : ISummaryGenerator
{
    public async Task<string?> SummarizeAsync(string description, CancellationToken ct)
    {
        var generationConfig = new JsonObject
        {
            ["maxOutputTokens"] = SummaryPrompt.MaxOutputTokens,
            // No temperature: Google advises keeping Gemini 3's default, lower values can degrade its answers.
        };
        if (!string.IsNullOrWhiteSpace(options.ThinkingLevel))
        {
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = options.ThinkingLevel };
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = SummaryPrompt.Instruction }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = description }),
            }),
            ["generationConfig"] = generationConfig,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(options.Model)}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        // The key goes in a header, not in the URL: URLs end up in logs and proxies.
        request.Headers.Add("x-goog-api-key", options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Any non-OK answer (quota 429, bad key 403, outage 5xx) means "no summary" — never an empty string.
            // The response body is not logged: it may echo the request, i.e. the customer's text.
            logger.LogWarning("Gemini returned {Status}; ticket will have no summary", (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        // No candidates at all is a normal answer too (e.g. the prompt itself was blocked).
        var candidate = json?["candidates"] is JsonArray { Count: > 0 } candidates ? candidates[0] : null;
        var finish = (string?)candidate?["finishReason"];
        if (finish is not null and not "STOP")
        {
            // MAX_TOKENS, SAFETY, ... : the text, if any, is incomplete or withheld.
            logger.LogWarning("Gemini stopped with {FinishReason}; ticket will have no summary", finish);
            return null;
        }

        // Join every text part: a reply may arrive in more than one part. Thought parts (if returned) are skipped.
        var parts = candidate?["content"]?["parts"]?.AsArray() ?? [];
        var text = string.Concat(parts
            .Where(p => p?["thought"]?.GetValue<bool>() != true)
            .Select(p => (string?)p?["text"]));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
