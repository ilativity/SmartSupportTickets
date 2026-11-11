using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;

namespace SupportTickets.Core.Ports.Out;

/// <summary>Driven port: where tickets are stored. Implemented by the JSON-file adapter.</summary>
public interface ITicketRepository
{
    /// <summary>Tickets matching <paramref name="query"/>, in no particular order.</summary>
    Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken ct);

    Task<Ticket?> GetAsync(Guid id, CancellationToken ct);

    Task AddAsync(Ticket ticket, CancellationToken ct);

    /// <summary>
    /// Reads the ticket, runs <paramref name="apply"/> on it and stores the result — <b>all as one atomic step</b>
    /// (D15). If reading and writing were separate calls, two simultaneous admin edits could both read the same
    /// "before", one would silently overwrite the other, and the emails would describe the wrong change.
    /// When <paramref name="apply"/> returns the same instance nothing is written.
    /// Returns null if the ticket does not exist.
    /// </summary>
    Task<TicketUpdate?> UpdateAsync(Guid id, Func<Ticket, Ticket> apply, CancellationToken ct);
}

/// <summary>The ticket as it was right before an update and right after it, read under the same lock.</summary>
public sealed record TicketUpdate(Ticket Before, Ticket After);
