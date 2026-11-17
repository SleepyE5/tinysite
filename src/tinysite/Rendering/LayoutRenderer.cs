using System.Text;
using Tinysite.Markdown;

namespace Tinysite.Rendering;

/// <summary>The values a layout template can read.</summary>
public sealed class PlaceholderTable
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rawNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a value. A template reads it HTML-escaped through <c>{{name}}</c>.</summary>
    public PlaceholderTable Add(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        values[name] = value ?? string.Empty;
        _ = rawNames.Remove(name);
        return this;
    }

    /// <summary>
    /// Adds a value the generator itself produced - a page body, a list of tag links - which is
    /// inserted without escaping. Author input never goes through here.
    /// </summary>
    public PlaceholderTable AddRaw(string name, string? html)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        values[name] = html ?? string.Empty;
        _ = rawNames.Add(name);
        return this;
    }

    /// <summary>True when the value for this name was registered as already-safe HTML.</summary>
    public bool IsRaw(string name) => rawNames.Contains(name);

    /// <summary>Reads a value, or <see langword="null"/> when the name was never set.</summary>
    public string? this[string name] => values.TryGetValue(name, out string? value) ? value : null;

    /// <summary>True when a value was set for this name, empty or not.</summary>
    public bool HasValue(string name) => values.ContainsKey(name);

    /// <summary>Names that were set, ordinal sorted, for diagnostics.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            List<string> names = [.. values.Keys];
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }
}

/// <summary>
/// The whole template engine: <c>{{name}}</c> inserts a value HTML-escaped,
/// <c>{{{name}}}</c> inserts it as written, and <c>{{content}}</c> is the page body slot.
/// </summary>
/// <remarks>
/// No Razor, no loops, no conditionals: a static site needs a chrome wrapper around one HTML
/// fragment, and every feature added here is an escaping rule somebody has to remember.
/// Escaping by default is the point - a front matter value such as <c>title: &lt;script&gt;</c>
/// must arrive as text, not as a live tag. Generator-produced HTML (the body, a list of tag
/// links) is read through triple braces.
/// </remarks>
public static class LayoutRenderer
{
    /// <summary>The placeholder a page body is inserted at.</summary>
    public const string ContentKey = "content";

    /// <summary>
    /// Substitutes every placeholder it can recognise, copies the rest verbatim, and reports the
    /// names that were asked for but never set so the caller can warn about a typo instead of
    /// quietly publishing an empty hole.
    /// </summary>
    public static LayoutRenderResult Render(string template, PlaceholderTable placeholders)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(placeholders);

        var output = new StringBuilder(template.Length + 512);
        var unknown = new List<string>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        while (index < template.Length)
        {
            if (TryReadPlaceholder(template, ref index, out string name, out bool raw))
            {
                string? value = placeholders[name];
                if (value is null)
                {
                    if (reported.Add(name))
                    {
                        unknown.Add(name);
                    }

                    value = string.Empty;
                }

                bool insertAsWritten = raw || placeholders.IsRaw(name);
                _ = output.Append(insertAsWritten ? value : HtmlEscape.Escape(value));
                continue;
            }

            _ = output.Append(template[index]);
            index++;
        }

        return new LayoutRenderResult(output.ToString(), unknown);
    }

    /// <summary>
    /// Attempts to read <c>{{name}}</c> or <c>{{{name}}}</c> at <paramref name="index"/>, moving it
    /// past the closing braces on success. Padding inside the braces is allowed. A brace run that
    /// is not a placeholder - CSS blocks, a stray <c>{{</c>, an unknown shape - leaves the index
    /// on the opening brace so the caller copies exactly one character and resyncs.
    /// </summary>
    private static bool TryReadPlaceholder(string text, ref int index, out string name, out bool raw)
    {
        name = string.Empty;
        raw = false;

        if (index + 1 >= text.Length || text[index] != '{' || text[index + 1] != '{')
        {
            return false;
        }

        bool triple = index + 2 < text.Length && text[index + 2] == '{';
        string closer = triple ? "}}}" : "}}";
        int cursor = index + (triple ? 3 : 2);

        cursor = SkipPadding(text, cursor);
        int nameStart = cursor;
        while (cursor < text.Length && IsNameCharacter(text[cursor]))
        {
            cursor++;
        }

        int nameLength = cursor - nameStart;
        cursor = SkipPadding(text, cursor);

        if (nameLength == 0 || cursor + closer.Length > text.Length
            || !text.AsSpan(cursor, closer.Length).Equals(closer, StringComparison.Ordinal))
        {
            return false;
        }

        name = text[nameStart..(nameStart + nameLength)];
        raw = triple;
        index = cursor + closer.Length;
        return true;
    }

    private static int SkipPadding(string text, int from)
    {
        while (from < text.Length && text[from] is ' ' or '\t')
        {
            from++;
        }

        return from;
    }

    private static bool IsNameCharacter(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.';
}

/// <summary>
/// A rendered layout and the placeholder names the template asked for that had no value.
/// </summary>
public sealed record LayoutRenderResult(string Html, IReadOnlyList<string> UnknownPlaceholders);
