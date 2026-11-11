using Microsoft.Extensions.Logging;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Notifications;

/// <summary>
/// The mock email sender the assignment allows (D8): writes the email to the application log instead of sending it.
/// The default, so the app runs with zero setup; the text body is logged in full so the tracking link can be clicked.
/// </summary>
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        logger.LogInformation(
            "EMAIL (console mock)\nTo: {To}\nSubject: {Subject}\n\n{Body}",
            notification.To, notification.Subject, notification.TextBody);
        return Task.CompletedTask;
    }
}
