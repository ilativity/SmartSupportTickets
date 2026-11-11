using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace SupportTickets.Api.Validation;

/// <summary>
/// Length limits measured on the value <b>after trimming</b>, because that is what gets stored (Core trims). With the
/// stock <c>[StringLength]</c>, <c>"  a "</c> would pass a 2-character minimum and be saved as one letter.
/// </summary>
public sealed class TrimmedLengthAttribute(int minimum, int maximum) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not string text)
        {
            return ValidationResult.Success; // null is [Required]'s job
        }

        var length = text.Trim().Length;
        return length >= minimum && length <= maximum
            ? ValidationResult.Success
            : new ValidationResult(
                $"{context.DisplayName} must be between {minimum} and {maximum} characters.", [context.MemberName!]);
    }
}

/// <summary>
/// An email address we can actually send to. Stricter than <c>[EmailAddress]</c>, which only checks for an "@"
/// that is not first or last (so it accepts "a@b" and "a@b@c").
/// </summary>
public sealed partial class StrictEmailAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not string text)
        {
            return ValidationResult.Success;
        }

        var trimmed = text.Trim();
        var valid = trimmed.Length <= SupportTickets.Core.Domain.TicketRules.EmailMaxLength
            // Conservative on purpose: plain ASCII addresses only. RFC 5322 also allows quoted local parts
            // ("a b"@x.com) and other forms that many real mail servers reject.
            && SimpleAddress().IsMatch(trimmed)
            // MailAddress also accepts "Name <a@b.c>"; requiring Address == input allows the bare address only.
            && MailAddress.TryCreate(trimmed, out var address)
            && address.Address == trimmed
            // A domain without a dot ("a@localhost") is not deliverable on the internet.
            && address.Host.Contains('.')
            && !address.Host.EndsWith('.');

        return valid
            ? ValidationResult.Success
            : new ValidationResult("Enter a valid email address, e.g. name@example.com.", [context.MemberName!]);
    }

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+$")]
    private static partial Regex SimpleAddress();
}

/// <summary>
/// An uploaded image: not empty, at most <see cref="ImageFormat.MaxBytes"/>, and a JPEG, PNG or WebP <b>by its
/// content</b>. The file name and the Content-Type the browser sent are ignored: both are chosen by the client.
/// </summary>
public sealed class ImageFileAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not IFormFile file)
        {
            return ValidationResult.Success; // optional
        }

        if (file.Length == 0 || file.Length > SupportTickets.Core.Domain.ImageFormat.MaxBytes)
        {
            return new ValidationResult("The image must be at most 5 MB.", [context.MemberName!]);
        }

        Span<byte> header = stackalloc byte[SupportTickets.Core.Domain.ImageFormat.HeaderLength];
        using var stream = file.OpenReadStream();
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        return SupportTickets.Core.Domain.ImageFormat.Detect(header[..read]) is null
            ? new ValidationResult("The image must be a JPEG, PNG or WebP file.", [context.MemberName!])
            : ValidationResult.Success;
    }
}

/// <summary>
/// One line of visible text: no line breaks or other control characters. A name is shown in emails and pages,
/// where an embedded line break could forge extra lines of text.
/// </summary>
public sealed class SingleLineAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        value is string text && text.Any(char.IsControl)
            ? new ValidationResult($"{context.DisplayName} must be a single line of text.", [context.MemberName!])
            : ValidationResult.Success;
}
