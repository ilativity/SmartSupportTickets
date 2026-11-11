namespace SupportTickets.Core.Domain;

/// <summary>
/// A customer support ticket. Immutable: every change produces a new instance. That is what lets the repository
/// hand out the objects it caches without copying them, and lets an update return a true "before" and "after".
/// </summary>
public sealed record Ticket
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Description { get; init; }

    /// <summary>AI-generated one-line summary; null when the AI provider was unavailable or not configured.</summary>
    public string? Summary { get; init; }

    /// <summary>Relative URL of the uploaded image (<c>uploads/{guid}.png</c>), or null.</summary>
    public string? ImageUrl { get; init; }

    public required TicketStatus Status { get; init; }

    /// <summary>Empty string, not null, when there is no resolution yet — the provided dataset uses "".</summary>
    public required string Resolution { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Creates a new ticket in status <see cref="TicketStatus.New"/>.</summary>
    public static Ticket Create(
        Guid id, string name, string email, string description, string? summary, string? imageUrl, DateTimeOffset now) =>
        new()
        {
            Id = id,
            Name = name.Trim(),
            Email = email.Trim(),
            Description = description.Trim(),
            Summary = summary,
            ImageUrl = imageUrl,
            Status = TicketStatus.New,
            Resolution = "",
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// Applies an admin's edit. Returns <b>this same instance</b> when nothing actually changes, so callers (and the
    /// repository, which then skips the file write) can tell "saved without changes" apart from a real change, and
    /// <see cref="UpdatedAt"/> only moves when something did.
    /// </summary>
    public Ticket Apply(TicketStatus status, string? resolution, DateTimeOffset now)
    {
        var newResolution = resolution?.Trim() ?? "";
        if (status == Status && newResolution == Resolution)
        {
            return this;
        }

        return this with { Status = status, Resolution = newResolution, UpdatedAt = now };
    }
}
