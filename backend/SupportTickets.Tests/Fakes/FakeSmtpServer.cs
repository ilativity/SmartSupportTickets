using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SupportTickets.Tests.Fakes;

/// <summary>
/// A minimal SMTP server on a free local port (no TLS, no authentication) that accepts one message and keeps its raw
/// text, so the SMTP adapter can be tested end to end — MailKit really speaks SMTP to it — without any mail account.
/// With <c>silent: true</c> it accepts the connection and never answers, to test the sender's timeout.
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _serving;

    public FakeSmtpServer(bool silent = false)
    {
        _listener.Start();
        _serving = Task.Run(() => ServeOneAsync(silent));
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The DATA section as received (headers and MIME body).</summary>
    public string? ReceivedData { get; private set; }

    public string? MailFrom { get; private set; }

    public List<string> Recipients { get; } = [];

    private async Task ServeOneAsync(bool silent)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        if (silent)
        {
            await Task.Delay(Timeout.Infinite).WaitAsync(TimeSpan.FromSeconds(30)).ContinueWith(_ => { });
            return;
        }

        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 localhost fake ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 8BITMIME");
                    break;
                case "MAIL":
                    MailFrom = line;
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "RCPT":
                    Recipients.Add(line);
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.AppendLine(dataLine);
                    }

                    ReceivedData = data.ToString();
                    await writer.WriteLineAsync("250 OK queued");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 OK");
                    break;
            }
        }
    }

    public void Dispose()
    {
        _listener.Stop();
        _ = _serving.ContinueWith(_ => { }); // observe any exception from the stopped listener
    }
}
