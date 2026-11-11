using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SupportTickets.Core.Application;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Core;

public class TicketUseCasesTests
{
    private readonly InMemoryTicketRepository _repository = new();
    private readonly FakeNotificationSender _mail = new();
    private readonly FakeTimeProvider _time = new(TestTickets.T0);
    private readonly NotificationComposer _composer = new(new Uri("http://localhost:5201"));

    private readonly CapturingLogger<TicketUseCases> _log = new();
    private readonly FakeImageStore _images = new();

    private TicketUseCases UseCases(FakeSummaryGenerator? ai = null) =>
        new(_repository, ai ?? FakeSummaryGenerator.Returns("Laptop overheats."), _mail, _composer,
            new SequentialIdGenerator(), _images, _time, _log);

    private static CreateTicketCommand NewTicket() =>
        new(" John Doe ", "john.doe@example.com", "My laptop overheats after ten minutes.");

    // ---- create ----

    [Fact]
    public async Task Create_saves_the_ticket_with_its_summary_and_emails_the_tracking_link_once()
    {
        var ticket = await UseCases().CreateAsync(NewTicket(), default);

        Assert.Equal("John Doe", ticket.Name);
        Assert.Equal("Laptop overheats.", ticket.Summary);
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(TestTickets.T0, ticket.CreatedAt);
        Assert.Same(ticket, Assert.Single(_repository.All));

        var mail = Assert.Single(_mail.Outbox);
        Assert.Equal("john.doe@example.com", mail.To);
        Assert.Contains($"http://localhost:5201/tickets/{ticket.Id}", mail.TextBody);
    }

    [Fact]
    public async Task Create_when_the_AI_throws_saves_the_ticket_without_a_summary()
    {
        var ticket = await UseCases(FakeSummaryGenerator.Throws()).CreateAsync(NewTicket(), default);

        Assert.Null(ticket.Summary);
        Assert.Single(_repository.All);
        Assert.Single(_mail.Outbox);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("AI summary failed"));
    }

    [Fact]
    public async Task Create_when_the_AI_hangs_gives_up_after_the_budget_and_saves_without_a_summary()
    {
        var creating = UseCases(FakeSummaryGenerator.Hangs()).CreateAsync(NewTicket(), default);

        _time.Advance(TicketUseCases.SummaryBudget - TimeSpan.FromMilliseconds(1));
        Assert.False(creating.IsCompleted); // still inside the budget

        _time.Advance(TimeSpan.FromMilliseconds(1));
        var ticket = await creating.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(ticket.Summary);
        Assert.Single(_repository.All);
        Assert.Contains(_log.Entries, e => e.Message.Contains("timed out"));
    }

    [Fact]
    public async Task Create_enforces_the_budget_even_if_the_AI_adapter_ignores_cancellation()
    {
        var creating = UseCases(FakeSummaryGenerator.IgnoresCancellation()).CreateAsync(NewTicket(), default);

        _time.Advance(TicketUseCases.SummaryBudget);
        var ticket = await creating.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(ticket.Summary);
        Assert.Single(_repository.All);
    }

    [Fact]
    public async Task Create_rejects_an_implausible_AI_answer()
    {
        var rambling = string.Join(' ', Enumerable.Repeat("blah", 60));

        var ticket = await UseCases(FakeSummaryGenerator.Returns(rambling)).CreateAsync(NewTicket(), default);

        Assert.Null(ticket.Summary);
    }

    [Fact]
    public async Task Create_succeeds_when_the_email_cannot_be_sent()
    {
        _mail.Fail = true;

        var ticket = await UseCases().CreateAsync(NewTicket(), default);

        Assert.Same(ticket, Assert.Single(_repository.All));
    }

    [Fact]
    public async Task Create_stops_without_saving_when_the_client_aborts_the_request()
    {
        using var aborted = new CancellationTokenSource();
        var creating = UseCases(FakeSummaryGenerator.Hangs()).CreateAsync(NewTicket(), aborted.Token);

        await aborted.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
        Assert.Empty(_repository.All);
        Assert.Empty(_mail.Outbox);
        // A client closing the tab is not an AI outage; reporting it as one would hide real outages in noise.
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Create_with_an_image_stores_it_and_links_it_to_the_ticket()
    {
        var command = NewTicket() with { Image = new ImageUpload(TestImages.Png, ImageKind.Png) };

        var ticket = await UseCases().CreateAsync(command, default);

        Assert.Equal("uploads/fake1.png", ticket.ImageUrl);
        Assert.Equal(TestImages.Png, Assert.Single(_images.Saved).Content);
    }

    [Fact]
    public async Task Create_fails_without_saving_when_the_image_cannot_be_stored()
    {
        _images.Fail = true;
        var command = NewTicket() with { Image = new ImageUpload(TestImages.Png, ImageKind.Png) };

        await Assert.ThrowsAsync<IOException>(() => UseCases().CreateAsync(command, default));
        Assert.Empty(_repository.All);
        Assert.Empty(_mail.Outbox);
    }

    [Fact]
    public async Task An_image_whose_ticket_could_not_be_saved_is_deleted_again()
    {
        _repository.FailOnAdd = true;
        var command = NewTicket() with { Image = new ImageUpload(TestImages.Png, ImageKind.Png) };

        await Assert.ThrowsAsync<IOException>(() => UseCases().CreateAsync(command, default));

        Assert.Equal(["uploads/fake1.png"], _images.Deleted);
        Assert.Empty(_mail.Outbox);
    }

    // ---- update ----

    [Theory]
    [InlineData(TicketStatus.InProgress, "")]                  // status only
    [InlineData(TicketStatus.New, "Technician booked")]        // resolution only
    [InlineData(TicketStatus.Resolved, "Replaced the fan")]    // both
    public async Task Update_with_a_change_sends_exactly_one_email(TicketStatus status, string resolution)
    {
        var ticket = TestTickets.Make();
        await _repository.AddAsync(ticket, default);
        _time.Advance(TimeSpan.FromHours(1));

        var updated = await UseCases().UpdateAsync(new UpdateTicketCommand(ticket.Id, status, resolution), default);

        Assert.NotNull(updated);
        Assert.Equal(status, updated.Status);
        Assert.Equal(TestTickets.T0.AddHours(1), updated.UpdatedAt);
        Assert.Single(_mail.Outbox);
    }

    [Fact]
    public async Task Update_without_a_change_sends_no_email_and_writes_nothing()
    {
        var ticket = TestTickets.Make(status: TicketStatus.InProgress, resolution: "Booked");
        await _repository.AddAsync(ticket, default);
        var writesBefore = _repository.Writes;

        var updated = await UseCases().UpdateAsync(new UpdateTicketCommand(ticket.Id, TicketStatus.InProgress, "Booked"), default);

        Assert.Same(ticket, updated);
        Assert.Empty(_mail.Outbox);
        Assert.Equal(writesBefore, _repository.Writes);
    }

    [Fact]
    public async Task Update_of_an_unknown_ticket_returns_null_and_sends_nothing()
    {
        var updated = await UseCases().UpdateAsync(new UpdateTicketCommand(Guid.NewGuid(), TicketStatus.Closed, ""), default);

        Assert.Null(updated);
        Assert.Empty(_mail.Outbox);
    }

    [Fact]
    public async Task Update_succeeds_when_the_email_cannot_be_sent()
    {
        var ticket = TestTickets.Make();
        await _repository.AddAsync(ticket, default);
        _mail.Fail = true;

        var updated = await UseCases().UpdateAsync(new UpdateTicketCommand(ticket.Id, TicketStatus.Closed, ""), default);

        Assert.Equal(TicketStatus.Closed, updated?.Status);
        Assert.Equal(TicketStatus.Closed, _repository.All.Single().Status);
    }

    // ---- list ----

    [Fact]
    public async Task List_returns_matching_tickets_newest_first()
    {
        await _repository.AddAsync(TestTickets.Make(name: "old", createdAt: TestTickets.T0), default);
        await _repository.AddAsync(TestTickets.Make(name: "new", createdAt: TestTickets.T0.AddDays(2)), default);
        await _repository.AddAsync(TestTickets.Make(name: "mid", createdAt: TestTickets.T0.AddDays(1)), default);
        await _repository.AddAsync(TestTickets.Make(name: "closed", status: TicketStatus.Closed), default);

        var list = await UseCases().ListAsync(new TicketQuery(Status: TicketStatus.New), default);

        Assert.Equal(["new", "mid", "old"], list.Select(t => t.Name));
    }
}
