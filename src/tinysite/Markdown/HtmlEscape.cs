using System.Text;

namespace Tinysite.Markdown;

/// <summary>
/// Every character that leaves the generator as HTML passes through here. This is the
/// XSS surface of a site generator: Markdown body text, link targets and template values
/// are all author-controlled, and a single unescaped <c>&lt;</c> turns a post into a script
/// tag on someone else's domain.
/// </summary>
public static class HtmlEscape
{
    /// <summary>
    /// Escapes the five characters that can change what a browser parses:
    /// <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c>.
    /// </summary>
    /// <remarks>
    /// Applied to element text as well as attribute values. Escaping quotes in text costs
    /// nothing but means a caller cannot accidentally reuse this for one context only and
    /// get it wrong the other way round.
    /// </remarks>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (NeedsNoEscaping(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            _ = c switch
            {
                '&' => builder.Append("&amp;"),
                '<' => builder.Append("&lt;"),
                '>' => builder.Append("&gt;"),
                '"' => builder.Append("&quot;"),
                '\'' => builder.Append("&#39;"),
                _ => builder.Append(c),
            };
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes and percent-encodes a URL for use in <c>href</c> or <c>src</c>.
    /// </summary>
    /// <remarks>
    /// Only the ASCII characters that cannot appear literally in a URL are encoded. Text is
    /// left alone, so an IRI or a path with an emoji in it stays readable.
    /// </remarks>
    public static string EscapeUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                // An ampersand in a query string is a real HTML hazard: inside an attribute
                // it starts a character reference, so it becomes &amp; rather than %26.
                case '&':
                    _ = builder.Append("&amp;");
                    break;
                case ' ':
                    _ = builder.Append("%20");
                    break;
                case '<':
                    _ = builder.Append("%3C");
                    break;
                case '>':
                    _ = builder.Append("%3E");
                    break;
                case '"':
                    _ = builder.Append("%22");
                    break;
                case '\'':
                    _ = builder.Append("%27");
                    break;
                case '`':
                    _ = builder.Append("%60");
                    break;
                case '\\':
                    _ = builder.Append("%5C");
                    break;
                case '^':
                    _ = builder.Append("%5E");
                    break;
                case '{':
                    _ = builder.Append("%7B");
                    break;
                case '}':
                    _ = builder.Append("%7D");
                    break;
                case '|':
                    _ = builder.Append("%7C");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        _ = builder.Append('%').Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _ = builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when a URL could be executed by a browser instead of merely navigated to.
    /// Anything that is not relative, fragment-only, or one of the schemes below is refused.
    /// </summary>
    public static bool IsDangerousUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();

        // Control characters and whitespace inside a scheme are a classic filter bypass:
        // "java\tscript:" is normalised by browsers before dispatch.
        if (candidate.Any(char.IsControl))
        {
            return true;
        }

        int colon = candidate.IndexOf(':');
        if (colon < 0 || IsFragmentOrQuery(candidate, colon))
        {
            // No scheme at all: relative path, root-relative path or fragment.
            return false;
        }

        string scheme = candidate[..colon].Trim().ToLowerInvariant();
        return scheme is not ("http" or "https" or "mailto" or "ftp" or "ftps" or "tel" or "rss");
    }

    private static bool IsFragmentOrQuery(string candidate, int colon)
    {
        int fragment = candidate.IndexOf('#');
        int query = candidate.IndexOf('?');

        // The colon appears only after the path started, e.g. "/a#b:c" or "file?x:y=1".
        return (fragment >= 0 && fragment < colon) || (query >= 0 && query < colon);
    }

    private static bool NeedsNoEscaping(string value)
    {
        foreach (char c in value)
        {
            if (c is '&' or '<' or '>' or '"' or '\'')
            {
                return false;
            }
        }

        return true;
    }
}
