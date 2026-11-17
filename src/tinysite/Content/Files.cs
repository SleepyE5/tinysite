namespace Tinysite.Content;

/// <summary>A Markdown file found under the content root, before it has been parsed.</summary>
/// <param name="RelativePath">Path below the content root, always with <c>/</c> separators.</param>
/// <param name="FullPath">Absolute path on disk.</param>
public sealed record MarkdownFile(string RelativePath, string FullPath);

/// <summary>A file found under <c>static/</c>, copied to the output without being parsed.</summary>
/// <param name="RelativePath">Path below <c>static/</c>, always with <c>/</c> separators.</param>
/// <param name="FullPath">Absolute path on disk.</param>
public sealed record StaticFile(string RelativePath, string FullPath);

/// <summary>Whether a document is a dated entry or a plain page.</summary>
public enum DocumentKind
{
    /// <summary>No date in front matter: rendered, but absent from the archive, feed and tag pages.</summary>
    Page,

    /// <summary>Has a date: rendered and listed in the archive, the feed and its tag pages.</summary>
    Post,
}

/// <summary>
/// Anything wrong with a source tree that the person who wrote it can fix: a missing directory,
/// a malformed front matter block, two documents fighting over one URL.
/// </summary>
public class SiteContentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Several source problems reported in one go, so a first build against a new tree tells the
/// author about every file instead of one at a time.
/// </summary>
public sealed class SiteContentProblemException(IReadOnlyList<string> messages)
    : Exception(string.Join(Environment.NewLine, messages))
{
    /// <summary>One message per problem, in source order.</summary>
    public IReadOnlyList<string> Messages { get; } = messages;
}
