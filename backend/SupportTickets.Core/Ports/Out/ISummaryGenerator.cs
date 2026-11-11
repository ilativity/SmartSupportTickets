namespace SupportTickets.Core.Ports.Out;

/// <summary>Driven port: produces a short summary of a support request. Implemented by the Gemini/OpenAI adapters.</summary>
public interface ISummaryGenerator
{
    /// <summary>
    /// Returns the raw summary, or null when none is available (no provider configured, provider error).
    /// May throw; the use case treats an exception like null. Length and sanity rules are applied by the caller
    /// (<c>SummaryRules</c>), so every provider is held to the same rules.
    /// </summary>
    Task<string?> SummarizeAsync(string description, CancellationToken ct);
}
