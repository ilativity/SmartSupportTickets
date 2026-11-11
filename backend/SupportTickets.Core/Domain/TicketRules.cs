namespace SupportTickets.Core.Domain;

/// <summary>
/// Field limits of a ticket. Constants, so the HTTP layer can use them in validation attributes and the limits
/// live in the domain rather than being retyped in each layer.
/// </summary>
public static class TicketRules
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 100;

    /// <summary>The maximum length of an email address (RFC 5321 path limit).</summary>
    public const int EmailMaxLength = 254;

    public const int DescriptionMinLength = 10;
    public const int DescriptionMaxLength = 2000;
    public const int ResolutionMaxLength = 2000;

    /// <summary>A stored summary is at most this long; see <c>SummaryRules</c>.</summary>
    public const int SummaryMaxLength = 200;
}
