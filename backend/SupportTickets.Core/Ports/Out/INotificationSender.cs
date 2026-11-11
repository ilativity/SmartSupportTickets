namespace SupportTickets.Core.Ports.Out;

/// <summary>Driven port: delivers an email. Implemented by the console (mock) and SMTP adapters.</summary>
public interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken ct);
}

/// <summary>An email, already composed. Both bodies are provided so an adapter can send a proper multipart mail.</summary>
public sealed record Notification(string To, string Subject, string TextBody, string HtmlBody);
