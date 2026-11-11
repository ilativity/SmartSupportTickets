using System.Text.RegularExpressions;
using SupportTickets.Core.Domain;

namespace SupportTickets.Core.Application;

/// <summary>
/// What the application accepts from an AI provider as a summary. Applied here, not in each adapter, so Gemini and
/// OpenAI are held to exactly the same rules and one set of tests covers both.
/// </summary>
public static partial class SummaryRules
{
    /// <summary>
    /// A summary is asked for in at most 15 words. An answer far longer than that means the model ignored the
    /// instruction — for example it answered the customer or followed instructions hidden in the ticket text — so it
    /// is thrown away rather than shown as a "summary".
    /// </summary>
    public const int MaxWords = 40;

    /// <summary>Returns a clean single-line summary, or null if the answer is empty or not a plausible summary.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Models sometimes add line breaks or wrap the sentence in double quotes; the UI shows a single line.
        // Single quotes are left alone: they may be real apostrophes ("'90s printer", "customers'").
        var text = Whitespace().Replace(raw, " ").Trim().Trim('"', '“', '”').Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > MaxWords)
        {
            return null;
        }

        if (text.Length <= TicketRules.SummaryMaxLength)
        {
            return text;
        }

        // Do not cut between the two halves of a surrogate pair (e.g. an emoji): a lone half is invalid text.
        var cut = TicketRules.SummaryMaxLength - 1;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
