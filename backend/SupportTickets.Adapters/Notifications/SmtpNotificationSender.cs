using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Adapters.Notifications;

/// <summary><c>Email:Smtp:*</c>. The password is a secret (user-secrets / environment variable only).</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;

    /// <summary>
    /// <c>StartTls</c> (port 587, Gmail), <c>SslOnConnect</c> (port 465) or <c>None</c> — only for a local test mail
    /// server such as Mailpit; never send real mail unencrypted.
    /// </summary>
    public string Security { get; set; } = "StartTls";

    /// <summary>Empty = no authentication (local test servers). For Gmail: the account, with an App Password.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Smart Support";

    /// <summary>Upper bound for one email, start to finish; the use case does not wait on email with a deadline.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>
/// Real email over SMTP with MailKit (the bonus "real email" in the brief; D8). Sends a multipart/alternative message:
/// the plain-text body for simple clients and the HTML body for the rest.
/// </summary>
public sealed class SmtpNotificationSender(SmtpOptions options) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken ct) =>
        // The use case calls this without a deadline (the email should go out even if the browser left), so the
        // deadline is here, over the whole exchange: a hanging mail server or DNS lookup must not hold a request
        // for ever. WaitAsync enforces it even where a library call does not observe the token.
        SendCoreAsync(notification, ct).WaitAsync(TimeSpan.FromSeconds(options.TimeoutSeconds), ct);

    private async Task SendCoreAsync(Notification notification, CancellationToken ct)
    {
        var message = BuildMessage(notification, options);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        using var client = new SmtpClient { Timeout = options.TimeoutSeconds * 1000 };
        await client.ConnectAsync(options.Host, options.Port, SecurityOption(options.Security), timeout.Token);
        if (!string.IsNullOrEmpty(options.Username))
        {
            await client.AuthenticateAsync(options.Username, options.Password, timeout.Token);
        }

        // MailKit 5 takes a send request (message + optional envelope); the envelope comes from the message here.
        await client.SendAsync(new MailKit.SendRequest(message), timeout.Token);

        try
        {
            await client.DisconnectAsync(quit: true, timeout.Token);
        }
        catch (Exception)
        {
            // The server already accepted the message; a failed goodbye must not report a delivered email as lost.
        }
    }

    /// <summary>Builds the MIME message; separate so its content can be checked without a mail server.</summary>
    public static MimeMessage BuildMessage(Notification notification, SmtpOptions options)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromAddress!)); // validated at startup
        // MailboxAddress parses and encodes the address; the API already accepted only plain addresses.
        message.To.Add(MailboxAddress.Parse(notification.To));
        message.Subject = notification.Subject;
        message.Body = new BodyBuilder { TextBody = notification.TextBody, HtmlBody = notification.HtmlBody }.ToMessageBody();
        return message;
    }

    public static SecureSocketOptions SecurityOption(string security) => security.ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" => SecureSocketOptions.SslOnConnect,
        "none" => SecureSocketOptions.None,
        _ => throw new InvalidOperationException($"Unknown Email:Smtp:Security '{security}'. Use StartTls, SslOnConnect or None."),
    };
}
