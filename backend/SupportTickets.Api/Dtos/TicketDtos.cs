using System.ComponentModel.DataAnnotations;
using SupportTickets.Api.Validation;
using SupportTickets.Core.Domain;

namespace SupportTickets.Api.Dtos;

/// <summary>
/// A row of the public ticket list. Deliberately has <b>no email</b> (D4): the list is public, so an email here would
/// publish every customer's address.
/// </summary>
public sealed record TicketListItemDto(
    Guid Id,
    string Name,
    string Description,
    string? Summary,
    string Status,
    string Resolution,
    bool HasImage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One ticket in full. <see cref="Email"/> is masked (<c>j***@example.com</c>) unless an admin asks (D4).</summary>
public sealed record TicketDetailsDto(
    Guid Id,
    string Name,
    string Email,
    string Description,
    string? Summary,
    string? ImageUrl,
    string Status,
    string Resolution,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The "New ticket" form (multipart/form-data, so an image can be attached).</summary>
public sealed class CreateTicketRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [TrimmedLength(TicketRules.NameMinLength, TicketRules.NameMaxLength)]
    [SingleLine]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [StrictEmail]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Description is required.")]
    [TrimmedLength(TicketRules.DescriptionMinLength, TicketRules.DescriptionMaxLength)]
    public string? Description { get; set; }
}

/// <summary>An admin's edit (JSON). Both fields are always sent: the form shows both.</summary>
public sealed class UpdateTicketRequest
{
    [Required(ErrorMessage = "Status is required.")]
    public string? Status { get; set; }

    [TrimmedLength(0, TicketRules.ResolutionMaxLength)]
    public string? Resolution { get; set; }
}

/// <summary>Mapping between the domain and the HTTP contract, in one place.</summary>
public static class TicketMapping
{
    public static TicketListItemDto ToListItem(this Ticket t) => new(
        t.Id, t.Name, t.Description, t.Summary, t.Status.ToName(), t.Resolution, t.ImageUrl is not null,
        t.CreatedAt, t.UpdatedAt);

    public static TicketDetailsDto ToDetails(this Ticket t, bool showEmail) => new(
        t.Id, t.Name, showEmail ? t.Email : MaskEmail(t.Email), t.Description, t.Summary,
        // Stored relative ("uploads/x.png"); served from the site root, so the browser gets "/uploads/x.png".
        t.ImageUrl is null ? null : "/" + t.ImageUrl.TrimStart('/'),
        t.Status.ToName(), t.Resolution, t.CreatedAt, t.UpdatedAt);

    /// <summary>
    /// "john.doe@example.com" → "j***@example.com": enough for the customer to recognise their own ticket, not enough
    /// to harvest the address. Anything that does not look like an address is hidden entirely.
    /// </summary>
    public static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        // The first *character* as a reader sees it (Rune), not the first UTF-16 unit: an address starting with an
        // emoji or another non-BMP letter would otherwise be cut into an invalid half.
        var first = System.Text.Rune.GetRuneAt(email, 0).ToString();
        return $"{first}***{email[at..]}";
    }
}
