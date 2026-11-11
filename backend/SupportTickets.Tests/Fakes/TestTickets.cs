using SupportTickets.Core.Domain;

namespace SupportTickets.Tests.Fakes;

/// <summary>Builds tickets for tests; only the fields a test cares about need to be given.</summary>
public static class TestTickets
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static Ticket Make(
        Guid? id = null,
        string name = "John Doe",
        string email = "john.doe@example.com",
        string description = "My laptop overheats and shuts down.",
        TicketStatus status = TicketStatus.New,
        string resolution = "",
        DateTimeOffset? createdAt = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Email = email,
            Description = description,
            Summary = null,
            ImageUrl = null,
            Status = status,
            Resolution = resolution,
            CreatedAt = createdAt ?? T0,
            UpdatedAt = createdAt ?? T0,
        };
}
