using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using SupportTickets.Adapters;
using SupportTickets.Adapters.Notifications;
using SupportTickets.Core.Ports.Out;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Adapters;

public class SmtpNotificationSenderTests
{
    private static readonly Notification Mail = new(
        "ann@example.com", "We received your support request", "Hello Ann, plain text body", "<p>Hello <b>Ann</b></p>");

    private static SmtpOptions LocalServer(int port, int timeoutSeconds = 10) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        Security = "None",
        FromAddress = "support@example.com",
        FromName = "Smart Support",
        TimeoutSeconds = timeoutSeconds,
    };

    [Fact]
    public async Task Delivers_one_multipart_message_over_real_SMTP()
    {
        using var server = new FakeSmtpServer();

        await new SmtpNotificationSender(LocalServer(server.Port)).SendAsync(Mail, default);

        Assert.Contains("support@example.com", server.MailFrom);
        Assert.Contains("ann@example.com", Assert.Single(server.Recipients));
        var message = MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(server.ReceivedData!)));
        Assert.Equal("We received your support request", message.Subject);
        Assert.Equal("Hello Ann, plain text body", message.TextBody.Trim());
        Assert.Contains("<b>Ann</b>", message.HtmlBody);
        Assert.Equal("Smart Support", ((MailboxAddress)message.From.Single()).Name);
    }

    [Fact]
    public async Task A_mail_server_that_never_answers_is_abandoned_after_the_timeout()
    {
        using var server = new FakeSmtpServer(silent: true);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Fails (and the use case logs it) instead of hanging; the adapter's own 1-second limit fires, not this test's.
        await Assert.ThrowsAnyAsync<Exception>(() => new SmtpNotificationSender(LocalServer(server.Port, timeoutSeconds: 1)).SendAsync(Mail, default));
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("StartTls", MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData("sslonconnect", MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData("None", MailKit.Security.SecureSocketOptions.None)]
    public void Security_names_map_to_MailKit_options(string name, MailKit.Security.SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpNotificationSender.SecurityOption(name));

    private static IServiceCollection Wire(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Append(("Email:Provider", "Smtp")).ToDictionary(s => s.Item1, s => (string?)s.Item2))
            .Build();
        return new ServiceCollection().AddLogging().AddAdapters(config, Path.GetTempPath());
    }

    [Fact]
    public void Smtp_without_a_sender_address_fails_at_startup() =>
        Assert.Contains("FromAddress", Assert.Throws<InvalidOperationException>(void () => Wire()).Message);

    [Fact]
    public void Smtp_with_a_username_but_no_password_fails_at_startup() =>
        Assert.Contains("Password", Assert.Throws<InvalidOperationException>(void () =>
            Wire(("Email:Smtp:FromAddress", "s@example.com"), ("Email:Smtp:Username", "s@example.com"))).Message);

    [Fact]
    public void Smtp_with_an_unknown_security_mode_fails_at_startup() =>
        Assert.Contains("Security", Assert.Throws<InvalidOperationException>(void () =>
            Wire(("Email:Smtp:FromAddress", "s@example.com"), ("Email:Smtp:Security", "Tls"))).Message);

    [Theory]
    [InlineData("Email:Smtp:FromAddress", "not an address")]
    [InlineData("Email:Smtp:Port", "0")]
    public void Smtp_with_an_invalid_value_fails_at_startup(string key, string value)
    {
        var settings = new Dictionary<string, string> { ["Email:Smtp:FromAddress"] = "s@example.com", [key] = value };
        Assert.Throws<InvalidOperationException>(void () => Wire(settings.Select(kv => (kv.Key, kv.Value)).ToArray()));
    }

    [Fact]
    public void A_password_over_an_unencrypted_connection_is_refused_at_startup() =>
        Assert.Contains("Security=None", Assert.Throws<InvalidOperationException>(void () =>
            Wire(("Email:Smtp:FromAddress", "s@example.com"), ("Email:Smtp:Security", "None"),
                 ("Email:Smtp:Username", "u"), ("Email:Smtp:Password", "p"))).Message);

    [Fact]
    public void A_complete_smtp_configuration_selects_the_smtp_sender() =>
        Assert.IsType<SmtpNotificationSender>(
            Wire(("Email:Smtp:FromAddress", "s@example.com"), ("Email:Smtp:Username", "u"), ("Email:Smtp:Password", "p"))
                .BuildServiceProvider().GetRequiredService<INotificationSender>());
}
