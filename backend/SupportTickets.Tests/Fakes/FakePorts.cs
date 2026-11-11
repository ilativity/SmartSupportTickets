using SupportTickets.Core.Ports.Out;

namespace SupportTickets.Tests.Fakes;

/// <summary>An AI provider whose behaviour each test chooses: answer, throw, or never answer.</summary>
public sealed class FakeSummaryGenerator(Func<string, CancellationToken, Task<string?>> behaviour) : ISummaryGenerator
{
    public int Calls { get; private set; }

    public static FakeSummaryGenerator Returns(string? summary) => new((_, _) => Task.FromResult(summary));

    public static FakeSummaryGenerator Throws() =>
        new((_, _) => throw new HttpRequestException("provider down"));

    /// <summary>Waits until cancelled — models a provider that hangs.</summary>
    public static FakeSummaryGenerator Hangs() =>
        new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "never";
        });

    /// <summary>Never answers and ignores cancellation — a badly written adapter.</summary>
    public static FakeSummaryGenerator IgnoresCancellation() =>
        new((_, _) => new TaskCompletionSource<string?>().Task);

    public Task<string?> SummarizeAsync(string description, CancellationToken ct)
    {
        Calls++;
        return behaviour(description, ct);
    }
}

/// <summary>Collects emails instead of sending them; can be told to fail.</summary>
public sealed class FakeNotificationSender : INotificationSender
{
    public List<Notification> Outbox { get; } = [];

    public bool Fail { get; set; }

    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP down");
        }

        Outbox.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>Ids 00000000-0000-0000-0000-000000000001, …02, … in order.</summary>
public sealed class SequentialIdGenerator : IIdGenerator
{
    private int _next;

    public Guid NewId() => new($"00000000-0000-0000-0000-{++_next:D12}");
}

/// <summary>Keeps every log entry so a test can check what was (or was not) reported.</summary>
public sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
