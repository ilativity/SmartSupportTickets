namespace SupportTickets.Core.Domain;

/// <summary>
/// What an update changed. Computed by the domain from the before/after pair, so "what counts as a change"
/// (and therefore whether an email is sent) is decided in one place, not in the HTTP endpoint.
/// </summary>
public sealed record TicketChange(bool StatusChanged, bool ResolutionChanged)
{
    public bool Any => StatusChanged || ResolutionChanged;

    public static TicketChange Between(Ticket before, Ticket after) =>
        new(before.Status != after.Status, before.Resolution != after.Resolution);
}
