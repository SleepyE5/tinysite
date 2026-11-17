namespace Tinysite.Metadata;

/// <summary>
/// Raised when a front matter block is malformed. The message names the line inside the
/// block, never the document; the caller prefixes the file path.
/// </summary>
public sealed class FrontMatterException : Exception
{
    public FrontMatterException(int line, string message)
        : base($"line {line}: {message}")
    {
        Line = line;
    }

    /// <summary>One-based line number within the front matter block.</summary>
    public int Line { get; }
}
