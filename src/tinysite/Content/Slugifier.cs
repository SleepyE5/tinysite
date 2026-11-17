using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tinysite.Content;

/// <summary>
/// Lowercase, hyphen-separated URL fragments built from file names and tags.
/// </summary>
/// <remarks>
/// Named <c>Slugifier</c> rather than <c>Slug</c> because <see cref="Document"/> has a
/// <c>Slug</c> property: inside that class the property would win and the helper would be
/// unreachable.
/// </remarks>
public static partial class Slugifier
{
    /// <summary>
    /// Builds the URL fragment for a document. A <c>2026-03-01-</c> date prefix is dropped, so
    /// correcting the date in a file name does not move the URL; a name that leaves nothing
    /// behind falls back to <c>page</c> rather than publishing into an empty path segment.
    /// </summary>
    public static string FromFileName(string nameWithoutExtension)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameWithoutExtension);

        string stem = DatePrefixRegex().Replace(nameWithoutExtension.Trim(), string.Empty);
        return Sluggify(stem) is { Length: > 0 } slugged ? slugged : "page";
    }

    /// <summary>
    /// Reads the <c>yyyy-mm-dd-</c> prefix of a file name as the publication moment.
    /// </summary>
    /// <remarks>
    /// No time of day, so a post published "today" sorts with the other posts of its day rather
    /// than at the minute the file was saved. Midnight UTC is a fixed point, which keeps the
    /// ordering identical on two machines in two zones -- the thing a date-first filename is for.
    /// </remarks>
    public static bool TryReadDatePrefix(string nameWithoutExtension, out DateTimeOffset publishedAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameWithoutExtension);

        Match match = DatePrefixRegex().Match(nameWithoutExtension.Trim());
        publishedAt = default;

        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateOnly day))
        {
            return false;
        }

        publishedAt = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// Builds the URL fragment for a tag, or <see langword="null"/> when the tag has no characters
    /// a URL can be built from, which the caller reports instead of dropping silently.
    /// </summary>
    public static string? FromTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        string slugged = Sluggify(tag.Trim());
        return slugged.Length > 0 ? slugged : null;
    }

    /// <summary>
    /// True when two differently spelled tags would share one URL: <c>C#</c> and <c>c</c>, or
    /// <c>dot net</c> and <c>dot-net</c>.
    /// </summary>
    public static bool Collides(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        string? leftSlug = FromTag(left);
        return leftSlug is not null
            && string.Equals(leftSlug, FromTag(right), StringComparison.Ordinal)
            && !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Letters and digits of any script are kept and lowercased against the invariant culture;
    /// runs of anything else collapse to a single hyphen. Keeping non-ASCII means a Japanese
    /// title still produces a readable URL, and percent-encoding is left to the server.
    /// </summary>
    private static string Sluggify(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool pendingSeparator = false;

        foreach (char c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    _ = builder.Append('-');
                }

                _ = builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = builder.Length > 0;
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-")]
    private static partial Regex DatePrefixRegex();
}
