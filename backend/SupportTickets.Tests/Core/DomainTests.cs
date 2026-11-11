using SupportTickets.Core.Domain;
using SupportTickets.Core.Ports.In;
using SupportTickets.Tests.Fakes;

namespace SupportTickets.Tests.Core;

public class TicketStatusNamesTests
{
    [Fact]
    public void All_lists_display_names_in_workflow_order() =>
        Assert.Equal(["New", "In Progress", "Resolved", "Closed"], TicketStatusNames.All);

    [Theory]
    [InlineData("In Progress", TicketStatus.InProgress)]
    [InlineData("in progress", TicketStatus.InProgress)]
    [InlineData(" Closed ", TicketStatus.Closed)]
    public void TryParse_accepts_display_names_ignoring_case(string input, TicketStatus expected)
    {
        Assert.True(TicketStatusNames.TryParse(input, out var status));
        Assert.Equal(expected, status);
    }

    // Enum.TryParse would accept the C# member name and any number; the API must not.
    [Theory]
    [InlineData("InProgress")]
    [InlineData("1")]
    [InlineData("42")]
    [InlineData("Done")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_rejects_anything_else(string? input) =>
        Assert.False(TicketStatusNames.TryParse(input, out _));
}

public class TicketTests
{
    [Fact]
    public void Apply_without_a_real_change_returns_the_same_instance_and_keeps_UpdatedAt()
    {
        var ticket = TestTickets.Make(status: TicketStatus.InProgress, resolution: "Technician booked");

        // Whitespace around the resolution is not a change.
        var after = ticket.Apply(TicketStatus.InProgress, "  Technician booked ", TestTickets.T0.AddHours(1));

        Assert.Same(ticket, after);
    }

    [Fact]
    public void Apply_with_a_change_returns_a_new_ticket_and_moves_UpdatedAt()
    {
        var ticket = TestTickets.Make();
        var now = TestTickets.T0.AddHours(1);

        var after = ticket.Apply(TicketStatus.Resolved, " Replaced the fan ", now);

        Assert.NotSame(ticket, after);
        Assert.Equal(TicketStatus.Resolved, after.Status);
        Assert.Equal("Replaced the fan", after.Resolution);
        Assert.Equal(now, after.UpdatedAt);
        Assert.Equal(ticket.CreatedAt, after.CreatedAt);
        Assert.Equal(TicketStatus.New, ticket.Status); // the original is untouched
    }

    [Fact]
    public void Apply_treats_null_resolution_as_empty() =>
        Assert.Equal("", TestTickets.Make(resolution: "x").Apply(TicketStatus.New, null, TestTickets.T0).Resolution);

    [Fact]
    public void TicketChange_reports_each_field_separately()
    {
        var before = TestTickets.Make();

        Assert.Equal(new TicketChange(true, false), TicketChange.Between(before, before with { Status = TicketStatus.Closed }));
        Assert.Equal(new TicketChange(false, true), TicketChange.Between(before, before with { Resolution = "done" }));
        Assert.False(TicketChange.Between(before, before).Any);
    }
}

public class TicketQueryTests
{
    private readonly Ticket _laptop = TestTickets.Make(name: "John", description: "Laptop overheats");
    private readonly Ticket _washer = TestTickets.Make(name: "Jane", description: "Washer leaks", status: TicketStatus.InProgress);

    [Fact]
    public void Empty_query_matches_everything()
    {
        Assert.True(new TicketQuery().Matches(_laptop));
        Assert.True(new TicketQuery(Search: "   ").Matches(_washer));
    }

    [Fact]
    public void Status_filter_excludes_other_statuses()
    {
        var query = new TicketQuery(Status: TicketStatus.InProgress);
        Assert.False(query.Matches(_laptop));
        Assert.True(query.Matches(_washer));
    }

    [Theory]
    [InlineData("LAPTOP")]   // description, other case
    [InlineData("joh")]      // name, partial
    public void Search_looks_in_name_and_description_ignoring_case(string search) =>
        Assert.True(new TicketQuery(Search: search).Matches(_laptop));

    [Fact]
    public void Search_rejects_non_matching_text() =>
        Assert.False(new TicketQuery(Search: "printer").Matches(_laptop));

    [Fact]
    public void Status_and_search_must_both_match() =>
        Assert.False(new TicketQuery(TicketStatus.InProgress, "laptop").Matches(_laptop));
}
