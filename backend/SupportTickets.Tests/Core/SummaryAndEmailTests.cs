using SupportTickets.Core.Application;
using SupportTickets.Core.Domain;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Core;

public class SummaryRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData("\"\"")]
    public void Empty_answers_become_null(string? raw) => Assert.Null(SummaryRules.Normalize(raw));

    [Fact]
    public void Line_breaks_and_wrapping_quotes_are_removed() =>
        Assert.Equal("Laptop overheats and shuts down.", SummaryRules.Normalize("\"Laptop overheats\nand  shuts down.\"\n"));

    [Fact]
    public void An_answer_at_the_word_limit_is_kept() =>
        Assert.NotNull(SummaryRules.Normalize(Words(SummaryRules.MaxWords)));

    // A model that ignored the instruction (answered the customer, followed text inside the ticket) writes far
    // more than one sentence; that must not be shown as a summary.
    [Fact]
    public void An_answer_over_the_word_limit_is_rejected() =>
        Assert.Null(SummaryRules.Normalize(Words(SummaryRules.MaxWords + 1)));

    [Fact]
    public void A_long_answer_within_the_word_limit_is_cut_to_the_stored_length()
    {
        var raw = string.Join(' ', Enumerable.Repeat("supercalifragilistic", 20)); // 20 words, 419 chars

        var summary = SummaryRules.Normalize(raw);

        Assert.NotNull(summary);
        Assert.Equal(TicketRules.SummaryMaxLength, summary.Length);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void Apostrophes_are_kept() =>
        Assert.Equal("'90s printer jams", SummaryRules.Normalize("'90s printer jams"));

    [Fact]
    public void Cutting_never_splits_an_emoji()
    {
        // 198 letters, then an emoji occupying chars 198-199: a naive cut at 199 keeps only its first half.
        var raw = new string('a', 198) + "\U0001F600" + " tail";

        var summary = SummaryRules.Normalize(raw)!;

        Assert.False(char.IsHighSurrogate(summary[^2]), "lone high surrogate before the ellipsis");
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));
}

public class NotificationComposerTests
{
    private readonly NotificationComposer _composer = new(new Uri("https://support.example.com/app"));

    [Fact]
    public void Tracking_link_keeps_the_base_path()
    {
        var id = Guid.Parse("b2a1f7c0-2a93-4d8f-a23e-9b1a24dcf5a1");
        Assert.Equal($"https://support.example.com/app/tickets/{id}", _composer.TrackingLink(id));
    }

    [Fact]
    public void Created_email_goes_to_the_customer_with_the_tracking_link()
    {
        var ticket = TestTickets.Make();

        var mail = _composer.TicketCreated(ticket);

        Assert.Equal(ticket.Email, mail.To);
        Assert.Contains(_composer.TrackingLink(ticket.Id), mail.TextBody);
        Assert.Contains(_composer.TrackingLink(ticket.Id), mail.HtmlBody);
    }

    [Fact]
    public void Customer_text_is_html_encoded_and_never_in_the_subject()
    {
        var ticket = TestTickets.Make(name: "Eve\r\nBcc: x@evil.test", description: "<script>alert(1)</script> broken");

        var mail = _composer.TicketCreated(ticket);

        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.DoesNotContain("Eve", mail.Subject);
    }

    [Fact]
    public void Update_without_a_change_produces_no_email()
    {
        var ticket = TestTickets.Make();
        Assert.Null(_composer.TicketUpdated(ticket, ticket));
    }

    [Fact]
    public void Update_of_status_and_resolution_produces_one_email_listing_both()
    {
        var before = TestTickets.Make();
        var after = before with { Status = TicketStatus.InProgress, Resolution = "Technician booked" };

        var mail = _composer.TicketUpdated(before, after);

        Assert.NotNull(mail);
        Assert.Contains("New -> In Progress", mail.TextBody);
        Assert.Contains("Resolution: Technician booked", mail.TextBody);
    }

    [Fact]
    public void Status_only_update_does_not_mention_the_resolution()
    {
        var before = TestTickets.Make();

        var mail = _composer.TicketUpdated(before, before with { Status = TicketStatus.Closed });

        Assert.NotNull(mail);
        Assert.DoesNotContain("Resolution", mail.TextBody);
    }
}
