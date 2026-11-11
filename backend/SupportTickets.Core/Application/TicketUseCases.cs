using Microsoft.Extensions.Logging;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Core.Application;

/// <summary>
/// The application's use cases. Implements the driving port using only driven ports, so it can be tested with
/// in-memory fakes and does not know about HTTP, JSON files, Gemini or SMTP.
/// </summary>
public sealed class TicketUseCases(
    ITicketRepository repository,
    ISummaryGenerator summaryGenerator,
    INotificationSender notificationSender,
    NotificationComposer composer,
    IIdGenerator idGenerator,
    IImageStore imageStore,
    TimeProvider timeProvider,
    ILogger<TicketUseCases> logger) : ITicketUseCases
{
    /// <summary>
    /// How long ticket creation waits for the AI summary (D17). The brief asks for the summary while saving, so the
    /// call is synchronous; the budget bounds how long a slow provider can keep the customer waiting.
    /// </summary>
    public static readonly TimeSpan SummaryBudget = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken ct)
    {
        var tickets = await repository.ListAsync(query, ct);
        return tickets.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public Task<Ticket?> GetAsync(Guid id, CancellationToken ct) => repository.GetAsync(id, ct);

    public async Task<Ticket> CreateAsync(CreateTicketCommand command, CancellationToken ct)
    {
        // The image is part of the customer's request: if it cannot be stored, the request fails (unlike AI/email).
        var imageUrl = command.Image is { } image
            ? await imageStore.SaveAsync(image.Content, image.Kind, ct)
            : null;

        var summary = await TrySummarizeAsync(command.Description, ct);

        var ticket = Ticket.Create(
            idGenerator.NewId(), command.Name, command.Email, command.Description, summary, imageUrl,
            timeProvider.GetUtcNow());

        // Saving is the one step allowed to fail the request: without it there is no ticket.
        try
        {
            await repository.AddAsync(ticket, ct);
        }
        catch when (imageUrl is not null)
        {
            // No ticket points at the image any more; do not leave it on disk.
            await imageStore.DeleteAsync(imageUrl, CancellationToken.None);
            throw;
        }

        await TrySendAsync(() => composer.TicketCreated(ticket), ticket.Id);
        return ticket;
    }

    public async Task<Ticket?> UpdateAsync(UpdateTicketCommand command, CancellationToken ct)
    {
        // The edit runs inside the repository's lock (D15); the clock is read there too, so updatedAt belongs
        // to the moment the change was really applied.
        var update = await repository.UpdateAsync(
            command.Id,
            ticket => ticket.Apply(command.Status, command.Resolution, timeProvider.GetUtcNow()),
            ct);

        if (update is null)
        {
            return null;
        }

        // Composed from the before/after pair the repository returned, never from a separate read — that read could
        // already include someone else's later edit.
        await TrySendAsync(() => composer.TicketUpdated(update.Before, update.After), update.After.Id);

        return update.After;
    }

    public async Task<AttachImageResult> AttachImageAsync(Guid id, ImageUpload image, CancellationToken ct)
    {
        // A quick check first, so an upload to a ticket that already has an image is refused without writing a file.
        var current = await repository.GetAsync(id, ct);
        if (current is null)
        {
            return new AttachImageResult(AttachImageOutcome.NotFound);
        }

        if (HasWorkingImage(current))
        {
            return new AttachImageResult(AttachImageOutcome.AlreadyHasImage);
        }

        var url = await imageStore.SaveAsync(image.Content, image.Kind, ct);
        TicketUpdate? update;
        try
        {
            // The decisive check runs inside the repository's lock (D15): if another admin attached an image in the
            // meantime, the ticket comes back unchanged and this upload is refused instead of overwriting theirs.
            update = await repository.UpdateAsync(
                id,
                ticket => HasWorkingImage(ticket) ? ticket : ticket.AttachImage(url, timeProvider.GetUtcNow()),
                ct);
        }
        catch
        {
            await imageStore.DeleteAsync(url, CancellationToken.None);
            throw;
        }

        if (update is null || ReferenceEquals(update.Before, update.After))
        {
            await imageStore.DeleteAsync(url, CancellationToken.None); // nothing points at the new file
            return new AttachImageResult(update is null ? AttachImageOutcome.NotFound : AttachImageOutcome.AlreadyHasImage);
        }

        return new AttachImageResult(AttachImageOutcome.Attached, update.After);
    }

    private bool HasWorkingImage(Ticket ticket) => ticket.ImageUrl is { } url && imageStore.Exists(url);

    /// <summary>Asks the AI provider for a summary; any failure or timeout becomes "no summary" (D9).</summary>
    private async Task<string?> TrySummarizeAsync(string description, CancellationToken requestAborted)
    {
        using var budget = new CancellationTokenSource(SummaryBudget, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, budget.Token);
        try
        {
            // WaitAsync enforces the budget even against an adapter that ignores its cancellation token:
            // the token alone is only a polite request to stop.
            var raw = await summaryGenerator.SummarizeAsync(description, linked.Token)
                .WaitAsync(SummaryBudget, timeProvider, requestAborted);
            return SummaryRules.Normalize(raw);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            // The client went away: stop the whole request instead of creating a ticket nobody is waiting for.
            throw;
        }
        catch (Exception ex)
        {
            if (ex is TimeoutException || budget.IsCancellationRequested)
            {
                logger.LogWarning("AI summary timed out after {Budget}; ticket saved without a summary", SummaryBudget);
            }
            else
            {
                logger.LogWarning(ex, "AI summary failed; ticket saved without a summary");
            }

            return null;
        }
    }

    /// <summary>
    /// Composes and sends an email; any failure is logged and swallowed — the ticket is already saved (D9).
    /// Composing is inside the try as well: a bug in an email template must not turn a saved ticket into a 500.
    /// A null notification means there is nothing to tell the customer.
    /// </summary>
    private async Task TrySendAsync(Func<Notification?> compose, Guid ticketId)
    {
        try
        {
            if (compose() is not { } notification)
            {
                return;
            }

            // Not tied to the request's token: once the ticket is saved, the customer should get the email even if
            // the browser tab was closed a moment later.
            await notificationSender.SendAsync(notification, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification for ticket {TicketId} could not be sent", ticketId);
        }
    }
}
