using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Ai;

/// <summary><c>Ai:Provider = None</c>: no AI configured, tickets are saved without a summary (D7).</summary>
public sealed class NoSummaryGenerator : ISummaryGenerator
{
    public Task<string?> SummarizeAsync(string description, CancellationToken ct) => Task.FromResult<string?>(null);
}
