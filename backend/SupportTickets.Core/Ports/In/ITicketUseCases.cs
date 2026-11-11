using SupportTickets.Core.Domain;

namespace SupportTickets.Core.Ports.In;

/// <summary>
/// Driving port: everything the outside world (here, the HTTP API) can ask the application to do with tickets.
/// </summary>
public interface ITicketUseCases
{
    /// <summary>Tickets matching the query, newest first.</summary>
    Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken ct);

    /// <summary>The ticket, or null if there is none with this id.</summary>
    Task<Ticket?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Creates a ticket: asks the AI for a summary, saves the ticket, emails the customer. AI and email failures
    /// are logged and do not fail the call — the customer's ticket is never lost to a third-party outage.
    /// </summary>
    Task<Ticket> CreateAsync(CreateTicketCommand command, CancellationToken ct);

    /// <summary>
    /// Changes status and/or resolution and emails the customer once if anything changed.
    /// Returns the updated ticket, or null if there is none with this id.
    /// </summary>
    Task<Ticket?> UpdateAsync(UpdateTicketCommand command, CancellationToken ct);

    /// <summary>
    /// Adds an image to a ticket that has none (or only a broken link). Never replaces a working image. No email:
    /// the customer is told about status and resolution changes only.
    /// </summary>
    Task<AttachImageResult> AttachImageAsync(Guid id, ImageUpload image, CancellationToken ct);
}

public enum AttachImageOutcome
{
    Attached,
    NotFound,
    AlreadyHasImage,
}

/// <summary>What happened; <see cref="Ticket"/> is set only when the image was attached.</summary>
public sealed record AttachImageResult(AttachImageOutcome Outcome, Ticket? Ticket = null);

/// <summary>Input of <see cref="ITicketUseCases.CreateAsync"/>; already validated by the caller.</summary>
public sealed record CreateTicketCommand(string Name, string Email, string Description, ImageUpload? Image = null);

/// <summary>An uploaded image whose type was already decided from its bytes (<see cref="ImageFormat.Detect"/>).</summary>
public sealed record ImageUpload(ReadOnlyMemory<byte> Content, ImageKind Kind);

/// <summary>Input of <see cref="ITicketUseCases.UpdateAsync"/>; already validated by the caller.</summary>
public sealed record UpdateTicketCommand(Guid Id, TicketStatus Status, string? Resolution);

/// <summary>List filter. Both parts are optional; an empty search means "no text filter".</summary>
public sealed record TicketQuery(TicketStatus? Status = null, string? Search = null)
{
    /// <summary>
    /// The filter itself, in the domain so every repository adapter applies the same rule:
    /// status equals, and the search text appears in the name or the description, ignoring case.
    /// </summary>
    public bool Matches(Ticket ticket)
    {
        if (Status is { } status && ticket.Status != status)
        {
            return false;
        }

        var search = Search?.Trim();
        return string.IsNullOrEmpty(search)
            || ticket.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || ticket.Description.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
