using System.Text;
using System.Text.RegularExpressions;

namespace Tinysite.Content;

/// <summary>
/// Plain text out of Markdown: what a summary line and a page <c>&lt;title&gt;</c> need, without
/// dragging the inline parser along for the ride.
/// </summary>
/// <remarks>
/// This is deliberately not a second copy of the converter. It removes markup that would read
/// badly as text and passes anything it does not recognise straight through. The result is only
/// ever used as a placeholder value, and placeholders are HTML-escaped on the way out, so a
/// hostile title cannot become live markup here.
/// </remarks>
public static partial class PlainText
{
    /// <summary>Maximum length of a generated summary before it is cut at a word boundary.</summary>
    public const int SummaryLimit = 160;

    /// <summary>The first prose paragraph of a body, stripped of markup and capped in length.</summary>
    public static string SummaryOf(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string paragraph = FirstParagraph(body);
        if (paragraph.Length == 0)
        {
            return string.Empty;
        }

        return Truncate(StripMarkup(paragraph), SummaryLimit);
    }

    /// <summary>Removes inline Markdown from one line of text.</summary>
    public static string StripMarkup(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string result = ImageRegex().Replace(text, string.Empty);
        result = LinkRegex().Replace(result, "$1");

        // Code spans keep their content; emphasis markers go away.
        result = CodeSpanRegex().Replace(result, "$1");
        result = IntrawordSafeEmphasisRegex().Replace(result, "$1");

        return Collapse(result);
    }

    /// <summary>
    /// Cuts at a word boundary where one is available within the limit, so a summary never ends
    /// mid-word. The ellipsis is not counted against the limit.
    /// </summary>
    public static string Truncate(string text, int limit)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        if (limit <= 0 || text.Length <= limit)
        {
            return text;
        }

        string head = text[..limit];
        int lastSpace = head.LastIndexOf(' ');
        string cut = lastSpace > limit / 2 ? head[..lastSpace] : head.TrimEnd();

        return string.Concat(cut.TrimEnd(',', '.', ';', ':', '-'), "...");
    }

    private static string FirstParagraph(string body)
    {
        var builder = new StringBuilder();

        foreach (string rawLine in body.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Length == 0)
            {
                if (builder.Length > 0)
                {
                    break;
                }

                continue;
            }

            if (IsBlockMarkup(line))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                _ = builder.Append(' ');
            }

            _ = builder.Append(line);
        }

        return builder.ToString();
    }

    private static bool IsBlockMarkup(string line) =>
        line.StartsWith('#')
        || line.StartsWith("```", StringComparison.Ordinal)
        || line.StartsWith("~~~", StringComparison.Ordinal)
        || line.StartsWith('>')
        || line.StartsWith("---", StringComparison.Ordinal)
        || line.StartsWith("***", StringComparison.Ordinal)
        || line.StartsWith("___", StringComparison.Ordinal)
        || ListMarkerRegex().IsMatch(line);

    private static string Collapse(string text)
    {
        if (text.IndexOfAny(['\t', '\r']) < 0 && !ContainsDoubleSpace(text))
        {
            return text.Trim();
        }

        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static bool ContainsDoubleSpace(string text) => text.Contains("  ", StringComparison.Ordinal);

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]*)\]\(([^)\s]*)[^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex CodeSpanRegex();

    // Emphasis markers that are not inside a word, so snake_case_name and 2*3*4 survive.
    [GeneratedRegex(@"(^|\s)[*_]{1,3}(?=\S)|(?<=\S)[*_]{1,3}(?=\s|$)")]
    private static partial Regex IntrawordSafeEmphasisRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^([ ]*[-*+][ \t]+|[ ]*\d{1,9}[.)][ \t]+)")]
    private static partial Regex ListMarkerRegex();
}
