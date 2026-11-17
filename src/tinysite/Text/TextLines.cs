namespace Tinysite.Text;

/// <summary>
/// Text plumbing shared by the front matter parser, the Markdown converter and the writer.
/// </summary>
public static class TextLines
{
    /// <summary>
    /// The single newline used throughout the generated site. Fixed rather than
    /// <see cref="Environment.NewLine"/> so output is byte-identical on every machine.
    /// </summary>
    public const string NewLine = "\n";

    /// <summary>UTF-8 byte order mark, as a code point so it never has to be typed literally.</summary>
    public const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// Removes a UTF-8 BOM and folds CRLF and lone CR to LF, so the rest of the
    /// pipeline only ever has to deal with LF.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string withoutBom = text.Length > 0 && text[0] == ByteOrderMark ? text[1..] : text;

        if (withoutBom.IndexOf('\r') < 0)
        {
            return withoutBom;
        }

        return withoutBom.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>Splits normalized text into lines. An empty string yields one empty line.</summary>
    public static IReadOnlyList<string> Split(string text) => Normalize(text).Split('\n');

    /// <summary>Number of leading space or tab characters, tabs counting as one column.</summary>
    public static int IndentWidth(string line)
    {
        int count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
        {
            count++;
        }

        return count;
    }
}
