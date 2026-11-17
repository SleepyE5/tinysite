using System.Globalization;
using System.Text;
using Tinysite.Text;

namespace Tinysite.Metadata;

/// <summary>A front matter block plus the Markdown that follows it.</summary>
/// <param name="FrontMatter">The parsed block, or <see cref="FrontMatter.Empty"/>.</param>
/// <param name="Body">The document text after the block, with newlines normalized to LF.</param>
public sealed record FrontMatterDocument(FrontMatter FrontMatter, string Body);

/// <summary>
/// Reads the YAML-ish block between a leading <c>---</c> line and its closing delimiter.
/// </summary>
/// <remarks>
/// This is deliberately not YAML. It supports the subset a static site needs: scalar
/// values, quoted values, inline and block sequences, one level of nesting and whole-line
/// comments. Anything else fails loudly instead of guessing, because a silently dropped
/// <c>draft: true</c> publishes something the author meant to keep private.
/// </remarks>
public static class FrontMatterParser
{
    private const string OpeningDelimiter = "---";

    /// <summary>
    /// Parses a whole document. Text without a leading <c>---</c> line is returned as
    /// front matter-less body text; an unterminated block is an error.
    /// </summary>
    public static FrontMatterDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string normalized = TextLines.Normalize(text);
        string[] lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].TrimEnd() != OpeningDelimiter)
        {
            return new FrontMatterDocument(FrontMatter.Empty, normalized);
        }

        int closing = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed is "---" or "...")
            {
                closing = i;
                break;
            }
        }

        if (closing < 0)
        {
            throw new FrontMatterException(1,
                "front matter block opened with '---' but never closed by a '---' or '...' line.");
        }

        var frontMatter = ReadBlock(lines[1..closing]);

        // The body is the text after the closing delimiter. Blank lines hugging the
        // delimiter carry no meaning, so they are dropped from both ends.
        string body = string.Join('\n', lines[(closing + 1)..]).Trim('\n');
        return new FrontMatterDocument(frontMatter, body);
    }

    private static FrontMatter ReadBlock(string[] block)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var maps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        string? title = null;
        DateTimeOffset? publishedAt = null;
        bool draft = false;
        IReadOnlyList<string>? tags = null;

        for (int i = 0; i < block.Length; i++)
        {
            string line = block[i];
            int lineNo = i + 1;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (TextLines.IndentWidth(line) != 0)
            {
                throw new FrontMatterException(lineNo,
                    "unexpected indentation; indented lines belong to the key above them.");
            }

            (string key, string value) = SplitKeyValue(line.Trim(), lineNo);

            if (scalars.ContainsKey(key) || lists.ContainsKey(key) || maps.ContainsKey(key))
            {
                throw new FrontMatterException(lineNo, $"duplicate key '{key}'.");
            }

            if (value.Length == 0)
            {
                switch (ReadNested(block, ref i, key))
                {
                    case SequenceResult sequence:
                        AssignSequence(key, sequence.Items, lineNo);
                        break;

                    case MapResult map:
                        maps[key] = map.Entries;
                        break;
                }

                continue;
            }

            // Shape decides everything: "[a, b]" is a sequence, a bare "a, b" is one for
            // tags only, and anything else is a scalar. Unquoting has to happen after that
            // test, or a value like 'a', 'b' loses its outer quotes and splits wrong.
            string known = key.ToLowerInvariant();
            if (TryReadInlineSequence(value, lineNo, key, out List<string> items))
            {
                AssignSequence(key, items, lineNo);
            }
            else if (known == "tags")
            {
                AssignSequence(key, SplitPlainList(value, lineNo, key), lineNo);
            }
            else
            {
                string scalar = Unquote(value, lineNo, key);
                switch (known)
                {
                    case "title":
                        title = scalar;
                        break;
                    case "date":
                        publishedAt = ParseDate(scalar, lineNo);
                        break;
                    case "draft":
                        draft = ParseBool(scalar, lineNo, key);
                        break;
                    default:
                        break;
                }

                scalars[key] = scalar;
            }
        }

        return new FrontMatter
        {
            Title = title,
            PublishedAt = publishedAt,
            Tags = tags ?? [],
            Draft = draft,
            Scalars = new Dictionary<string, string>(scalars, StringComparer.OrdinalIgnoreCase),
            Lists = new Dictionary<string, IReadOnlyList<string>>(lists, StringComparer.OrdinalIgnoreCase),
            Maps = new Dictionary<string, IReadOnlyDictionary<string, string>>(maps, StringComparer.OrdinalIgnoreCase),
        };

        void AssignSequence(string sequenceKey, IEnumerable<string> rawItems, int sequenceLine)
        {
            List<string> items = UnquoteAll(rawItems, sequenceLine, sequenceKey);
            lists[sequenceKey] = items;

            // Only tags are de-duplicated; a hand-written "related" list keeps its order
            // and its repeats.
            if (IsTagKey(sequenceKey))
            {
                tags = Distinct(items);
            }

            if (string.Equals(sequenceKey, "draft", StringComparison.OrdinalIgnoreCase))
            {
                throw new FrontMatterException(sequenceLine,
                    "'draft' needs a true or false value, not a list.");
            }
        }
    }

    /// <summary>Reads the indented lines under a key that had no inline value.</summary>
    private static NestedResult ReadNested(string[] block, ref int index, string key)
    {
        var items = new List<string>();
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool? isSequence = null;
        int expectedIndent = -1;

        int i = index + 1;
        for (; i < block.Length; i++)
        {
            string line = block[i];
            int lineNo = i + 1;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = TextLines.IndentWidth(line);
            if (indent == 0)
            {
                break;
            }

            if (expectedIndent < 0)
            {
                expectedIndent = indent;
            }
            else if (indent != expectedIndent)
            {
                throw new FrontMatterException(lineNo,
                    $"inconsistent indentation under '{key}'; use {expectedIndent} column(s).");
            }

            string content = line.Trim();

            if (content.StartsWith("- ", StringComparison.Ordinal) || content == "-")
            {
                isSequence ??= true;
                if (isSequence == false)
                {
                    throw new FrontMatterException(lineNo,
                        $"'{key}' mixes a sequence item with 'key: value' lines.");
                }

                items.Add(Unquote(content.Length > 1 ? content[2..].Trim() : string.Empty, lineNo, key));
            }
            else
            {
                isSequence ??= false;
                if (isSequence == true)
                {
                    throw new FrontMatterException(lineNo,
                        $"'{key}' mixes a sequence item with 'key: value' lines.");
                }

                (string nestedKey, string nestedValue) = SplitKeyValue(content, lineNo);
                if (entries.ContainsKey(nestedKey))
                {
                    throw new FrontMatterException(lineNo, $"duplicate key '{nestedKey}' under '{key}'.");
                }

                if (nestedValue.Length == 0)
                {
                    throw new FrontMatterException(lineNo,
                        $"'{key}.{nestedKey}' has no value; only one nesting level is supported.");
                }

                entries[nestedKey] = Unquote(nestedValue, lineNo, $"{key}.{nestedKey}");
            }
        }

        index = i - 1;

        if (isSequence == true)
        {
            return new SequenceResult(items);
        }

        if (isSequence == false)
        {
            return new MapResult(entries);
        }

        // A key with neither an inline nor an indented value still has to appear, otherwise
        // "tags:" silently means "no tags" for a file the author meant to fix later.
        throw new FrontMatterException(index + 1, $"'{key}' has no value.");
    }

    private static (string Key, string Value) SplitKeyValue(string text, int lineNo)
    {
        int colon = IndexOfValueColon(text);
        if (colon <= 0)
        {
            throw new FrontMatterException(lineNo,
                $"expected 'key: value' but found '{text}'.");
        }

        string key = text[..colon].Trim();
        if (key.Contains(' '))
        {
            throw new FrontMatterException(lineNo, $"key '{key}' contains a space.");
        }

        return (key, text[(colon + 1)..].Trim());
    }

    /// <summary>Finds the colon that separates a key from its value, skipping quoted text.</summary>
    private static int IndexOfValueColon(string text)
    {
        char quote = '"';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                quote = c;
                int close = text.IndexOf(quote, i + 1);
                if (close < 0)
                {
                    // Unterminated quote: treat the rest as value text and let the value
                    // parser report it.
                    continue;
                }

                i = close;
                continue;
            }

            if (c == ':')
            {
                return i;
            }
        }

        return -1;
    }

    private static string Unquote(string raw, int lineNo, string key)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        char first = raw[0];
        if (first is not ('"' or '\'') || raw.Length < 2 || raw[^1] != first)
        {
            return raw;
        }

        string inner = raw[1..^1];
        if (first == '\'')
        {
            return inner.Replace("''", "'", StringComparison.Ordinal);
        }

        return inner.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Recognises the flow style <c>[a, b, c]</c>. The parts keep their quotes; the caller
    /// unquotes them, which is what lets <c>tags: 'dotnet', 'markdown'</c> survive.
    /// </summary>
    private static bool TryReadInlineSequence(string raw, int lineNo, string key, out List<string> items)
    {
        items = [];
        if (raw.Length < 2 || raw[0] != '[' || raw[^1] != ']')
        {
            return false;
        }

        items = SplitTopLevel(raw[1..^1], ',', lineNo, key);
        return true;
    }

    /// <summary>Reads the plain <c>a, b, c</c> shape most people type for tags.</summary>
    private static List<string> SplitPlainList(string raw, int lineNo, string key) =>
        SplitTopLevel(raw, ',', lineNo, key);

    /// <summary>
    /// Splits on a separator that is not inside single or double quotes. Double-quoted
    /// segments may escape the quote with a backslash.
    /// </summary>
    private static List<string> SplitTopLevel(string text, char separator, int lineNo, string key)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Backslash escape inside double quotes: copy both characters verbatim.
            if (quote == '"' && c == '\\' && i + 1 < text.Length)
            {
                _ = current.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                if (quote == '\0')
                {
                    quote = c;
                }
                else if (quote == c)
                {
                    quote = '\0';
                }

                _ = current.Append(c);
                continue;
            }

            if (c == separator && quote == '\0')
            {
                parts.Add(current.ToString().Trim());
                _ = current.Clear();
                continue;
            }

            _ = current.Append(c);
        }

        if (quote != '\0')
        {
            throw new FrontMatterException(lineNo, $"unbalanced quote in the value of '{key}'.");
        }

        parts.Add(current.ToString().Trim());
        _ = parts.RemoveAll(part => part.Length == 0);
        return parts;
    }

    private static List<string> UnquoteAll(IEnumerable<string> raw, int lineNo, string key)
    {
        var items = new List<string>();
        foreach (string candidate in raw)
        {
            string item = Unquote(candidate, lineNo, key).Trim();
            if (item.Length > 0)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static List<string> Distinct(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string item in items)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static bool ParseBool(string raw, int lineNo, string key) => raw.ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        "false" or "no" or "off" or "0" => false,
        _ => throw new FrontMatterException(lineNo,
            $"'{key}' must be true or false but was '{raw}'."),
    };

    private static DateTimeOffset ParseDate(string raw, int lineNo)
    {
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, styles, out DateTimeOffset parsed))
        {
            return parsed;
        }

        throw new FrontMatterException(lineNo,
            $"'date' must be an ISO date such as 2026-03-01 or 2026-03-01T14:30:00Z but was '{raw}'.");
    }

    private static bool IsTagKey(string key) =>
        string.Equals(key, "tags", StringComparison.OrdinalIgnoreCase);

    private abstract record NestedResult;

    private sealed record SequenceResult(List<string> Items) : NestedResult;

    private sealed record MapResult(Dictionary<string, string> Entries) : NestedResult;
}
