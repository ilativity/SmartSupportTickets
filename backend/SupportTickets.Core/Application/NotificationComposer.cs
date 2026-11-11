using System.Net;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Core.Application;

/// <summary>
/// Writes the customer emails. Kept apart from the senders so the texts are the same whichever adapter delivers them
/// (console or SMTP), and testable without sending anything.
/// </summary>
public sealed class NotificationComposer(Uri frontendBaseUrl)
{
    // A trailing slash makes "tickets/…" resolve *under* the base path: without it, a frontend hosted at
    // https://host/support would produce links to https://host/tickets/… .
    private readonly Uri _baseUrl = frontendBaseUrl.AbsoluteUri.EndsWith('/')
        ? frontendBaseUrl
        : new Uri(frontendBaseUrl.AbsoluteUri + "/");

    /// <summary>The page where the customer can follow the ticket: <c>{frontend}/tickets/{id}</c>.</summary>
    public string TrackingLink(Guid ticketId) =>
        new Uri(_baseUrl, $"tickets/{ticketId}").AbsoluteUri;

    public Notification TicketCreated(Ticket ticket)
    {
        // Subjects never contain customer input: a value with a line break in a mail header is a classic
        // header-injection vector, and a fixed subject makes the question moot.
        const string subject = "We received your support request";
        var link = TrackingLink(ticket.Id);

        var text =
            $"""
             Hello {ticket.Name},

             Thank you for contacting us. Your ticket has been created with status "{ticket.Status.ToName()}".

             Your request:
             {ticket.Description}

             You can follow it here: {link}
             """;

        var html =
            $"""
             <p>Hello {Html(ticket.Name)},</p>
             <p>Thank you for contacting us. Your ticket has been created with status <b>{Html(ticket.Status.ToName())}</b>.</p>
             <p>Your request:</p>
             <blockquote>{Html(ticket.Description)}</blockquote>
             <p><a href="{Html(link)}">Follow your ticket</a></p>
             """;

        return new Notification(ticket.Email, subject, text, html);
    }

    /// <summary>
    /// One email for one save, listing everything that changed (D10). Returns null when nothing changed —
    /// there is nothing to tell the customer.
    /// </summary>
    public Notification? TicketUpdated(Ticket before, Ticket after)
    {
        var change = TicketChange.Between(before, after);
        if (!change.Any)
        {
            return null;
        }

        const string subject = "Your support request was updated";
        var link = TrackingLink(after.Id);

        var textLines = new List<string>();
        var htmlItems = new List<string>();
        if (change.StatusChanged)
        {
            textLines.Add($"Status: {before.Status.ToName()} -> {after.Status.ToName()}");
            htmlItems.Add($"<li>Status: {Html(before.Status.ToName())} → <b>{Html(after.Status.ToName())}</b></li>");
        }

        if (change.ResolutionChanged)
        {
            var resolution = after.Resolution.Length > 0 ? after.Resolution : "(cleared)";
            textLines.Add($"Resolution: {resolution}");
            htmlItems.Add($"<li>Resolution: {Html(resolution)}</li>");
        }

        var text =
            $"""
             Hello {after.Name},

             Your ticket was updated:
             {string.Join(Environment.NewLine, textLines)}

             Details: {link}
             """;

        var html =
            $"""
             <p>Hello {Html(after.Name)},</p>
             <p>Your ticket was updated:</p>
             <ul>{string.Concat(htmlItems)}</ul>
             <p><a href="{Html(link)}">View your ticket</a></p>
             """;

        return new Notification(after.Email, subject, text, html);
    }

    // Customer-written text goes into HTML, so it is encoded: "<script>" in a description must arrive as text.
    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
