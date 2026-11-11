using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Ai;

/// <summary>
/// AI summary via OpenAI's Responses API (<c>POST v1/responses</c>) — the API OpenAI recommends for its current,
/// reasoning-capable models. Plain <see cref="HttpClient"/>, no SDK, same reasons as the Gemini adapter.
/// </summary>
public sealed class OpenAiSummaryGenerator(HttpClient http, OpenAiOptions options, ILogger<OpenAiSummaryGenerator> logger)
    : ISummaryGenerator
{
    public async Task<string?> SummarizeAsync(string description, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            // The instruction and the customer's text travel in separate fields: the text is data, not instructions.
            ["instructions"] = SummaryPrompt.Instruction,
            ["input"] = description,
            ["max_output_tokens"] = SummaryPrompt.MaxOutputTokens,
            ["store"] = false, // do not keep customer text on OpenAI's side for later retrieval
        };
        if (!string.IsNullOrWhiteSpace(options.ReasoningEffort))
        {
            body["reasoning"] = new JsonObject { ["effort"] = options.ReasoningEffort };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // 429 (quota), 401 (bad key), 5xx: no summary. The body is not logged: it may echo the customer's text.
            logger.LogWarning("OpenAI returned {Status}; ticket will have no summary", (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        var status = (string?)json?["status"];
        if (status != "completed")
        {
            // "incomplete" (e.g. max_output_tokens reached), "failed", ...: the text, if any, is cut off.
            logger.LogWarning("OpenAI response status {Status}; ticket will have no summary", status ?? "(none)");
            return null;
        }

        // output[] holds reasoning items and message items; the answer is the text of the message's parts.
        // A "refusal" part (the model declining) carries its words in "refusal", not "text", so it adds nothing here.
        var text = string.Concat(
            (json?["output"] as JsonArray ?? [])
                .Where(item => (string?)item?["type"] == "message")
                .SelectMany(item => item?["content"] as JsonArray ?? [])
                .Select(part => (string?)part?["text"]));

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
