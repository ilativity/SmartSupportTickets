namespace SupportTickets.Core.Domain;

/// <summary>Lifecycle state of a ticket. Any state may follow any other: the assignment defines no workflow.</summary>
public enum TicketStatus
{
    New,
    InProgress,
    Resolved,
    Closed,
}

/// <summary>
/// The human-readable names of <see cref="TicketStatus"/> ("In Progress" has a space), used on disk, in the API and
/// in emails. Kept here, in one place, so the JSON adapter and the HTTP layer cannot disagree on the spelling.
/// </summary>
public static class TicketStatusNames
{
    private static readonly (TicketStatus Status, string Name)[] Map =
    [
        (TicketStatus.New, "New"),
        (TicketStatus.InProgress, "In Progress"),
        (TicketStatus.Resolved, "Resolved"),
        (TicketStatus.Closed, "Closed"),
    ];

    /// <summary>All display names in workflow order — what <c>GET /api/tickets/statuses</c> returns.</summary>
    public static IReadOnlyList<string> All { get; } = Map.Select(m => m.Name).ToArray();

    public static string ToName(this TicketStatus status) =>
        Map.First(m => m.Status == status).Name;

    /// <summary>
    /// Parses a display name, ignoring case. Deliberately not <c>Enum.TryParse</c>: that would also accept
    /// "InProgress" and numbers such as "1" or "42", letting clients send values the API does not document.
    /// </summary>
    public static bool TryParse(string? name, out TicketStatus status)
    {
        foreach (var (candidate, candidateName) in Map)
        {
            if (string.Equals(candidateName, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
