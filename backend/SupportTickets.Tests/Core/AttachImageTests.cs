using Microsoft.Extensions.Time.Testing;
using SupportTickets.Core.Application;
using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Core;

public class AttachImageTests
{
    private readonly InMemoryTicketRepository _repository = new();
    private readonly FakeImageStore _images = new();
    private readonly FakeNotificationSender _mail = new();
    private readonly FakeTimeProvider _time = new(TestTickets.T0);

    private TicketUseCases UseCases() =>
        new(_repository, FakeSummaryGenerator.Returns(null), _mail, new NotificationComposer(new Uri("http://localhost:5201")),
            new SequentialIdGenerator(), _images, _time, new CapturingLogger<TicketUseCases>());

    private static ImageUpload Png => new(TestImages.Png, ImageKind.Png);

    [Fact]
    public async Task A_ticket_without_an_image_gets_one_and_no_email_is_sent()
    {
        var ticket = TestTickets.Make();
        await _repository.AddAsync(ticket, default);
        _time.Advance(TimeSpan.FromHours(1));

        var result = await UseCases().AttachImageAsync(ticket.Id, Png, default);

        Assert.Equal(AttachImageOutcome.Attached, result.Outcome);
        Assert.Equal("uploads/fake1.png", result.Ticket!.ImageUrl);
        Assert.Equal(TestTickets.T0.AddHours(1), result.Ticket.UpdatedAt);
        Assert.Equal("uploads/fake1.png", _repository.All.Single().ImageUrl);
        Assert.Empty(_mail.Outbox);
    }

    [Fact]
    public async Task A_working_image_is_never_replaced_and_no_file_is_written()
    {
        var ticket = TestTickets.Make() with { ImageUrl = "uploads/existing.png" };
        _images.Existing.Add("uploads/existing.png");
        await _repository.AddAsync(ticket, default);

        var result = await UseCases().AttachImageAsync(ticket.Id, Png, default);

        Assert.Equal(AttachImageOutcome.AlreadyHasImage, result.Outcome);
        Assert.Empty(_images.Saved);
        Assert.Equal("uploads/existing.png", _repository.All.Single().ImageUrl);
    }

    [Fact]
    public async Task A_broken_image_link_counts_as_no_image()
    {
        // As in the sample dataset: an image name whose file does not exist.
        var ticket = TestTickets.Make() with { ImageUrl = "uploads/laptop_issue.jpg" };
        await _repository.AddAsync(ticket, default);

        var result = await UseCases().AttachImageAsync(ticket.Id, Png, default);

        Assert.Equal(AttachImageOutcome.Attached, result.Outcome);
        Assert.Equal("uploads/fake1.png", result.Ticket!.ImageUrl);
    }

    [Fact]
    public async Task Unknown_ticket_is_not_found_and_no_file_is_written()
    {
        var result = await UseCases().AttachImageAsync(Guid.NewGuid(), Png, default);

        Assert.Equal(AttachImageOutcome.NotFound, result.Outcome);
        Assert.Empty(_images.Saved);
    }

    [Fact]
    public async Task When_another_admin_attached_meanwhile_the_upload_is_refused_and_its_file_deleted()
    {
        var ticket = TestTickets.Make();
        await _repository.AddAsync(ticket, default);
        // Between our quick check and our save, another admin attaches an image.
        _images.AfterSave = async () =>
        {
            _images.Existing.Add("uploads/theirs.png");
            await _repository.UpdateAsync(ticket.Id, t => t with { ImageUrl = "uploads/theirs.png" }, default);
        };

        var result = await UseCases().AttachImageAsync(ticket.Id, Png, default);

        Assert.Equal(AttachImageOutcome.AlreadyHasImage, result.Outcome);
        Assert.Equal("uploads/theirs.png", _repository.All.Single().ImageUrl); // theirs is kept
        Assert.Equal(["uploads/fake1.png"], _images.Deleted);                 // ours is cleaned up
    }

    [Fact]
    public async Task If_saving_the_ticket_fails_the_new_file_is_deleted()
    {
        var ticket = TestTickets.Make();
        await _repository.AddAsync(ticket, default);
        _repository.FailOnUpdate = true;

        await Assert.ThrowsAsync<IOException>(() => UseCases().AttachImageAsync(ticket.Id, Png, default));

        Assert.Equal(["uploads/fake1.png"], _images.Deleted);
    }
}
