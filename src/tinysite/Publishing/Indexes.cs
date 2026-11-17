using Tinysite.Content;

namespace Tinysite.Publishing;

/// <summary>
/// The order posts are listed in: newest first, and for two posts on the same date the order
/// they arrived in, which is the ordinal source path order the reader guarantees.
/// </summary>
public static class PostOrdering
{
    /// <summary>
    /// Orders posts by descending date, newest first.
    /// </summary>
    /// <remarks>
    /// <see cref="Enumerable.OrderByDescending{TSource, TKey}"/> is a documented stable sort, so
    /// two posts on the same date keep the order they arrived in. That is only reproducible
    /// because the input is already ordinal-sorted by source path - see <see cref="SourceReader"/>
    /// - which is why nothing here is allowed to enumerate the filesystem directly.
    /// </remarks>
    public static IReadOnlyList<Document> ByDateDescending(IEnumerable<Document> posts)
    {
        ArgumentNullException.ThrowIfNull(posts);

        return [.. posts
            .Where(post => post.Kind == DocumentKind.Post)
            .OrderByDescending(post => post.PublishedAt ?? UndatedSortKey)];
    }

    /// <summary>
    /// A post is only ever undated if a caller hands one in; it sorts oldest rather than throwing
    /// inside a renderer, because a renderer has no way to report a source problem.
    /// </summary>
    private static readonly DateTimeOffset UndatedSortKey = DateTimeOffset.MinValue;
}

/// <summary>One tag and every published post carrying it.</summary>
/// <param name="Tag">Spelling kept for display: the first one in source order.</param>
/// <param name="Slug">URL fragment for the tag page.</param>
/// <param name="UrlPath">Site-absolute path of the tag page.</param>
/// <param name="OutputPath">Output path of the tag page.</param>
/// <param name="Posts">Posts under this tag, newest first.</param>
public sealed record TagPage(string Tag, string Slug, string UrlPath, string OutputPath, IReadOnlyList<Document> Posts);

/// <summary>Aggregates tags across the published posts.</summary>
public static class TagIndex
{
    /// <summary>Builds one page per tag, sorted by tag slug, each listing its posts newest first.</summary>
    /// <exception cref="SiteContentException">Two differently spelled tags would share one URL.</exception>
    public static IReadOnlyList<TagPage> Build(IEnumerable<Document> posts)
    {
        ArgumentNullException.ThrowIfNull(posts);

        var groups = new Dictionary<string, TagGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (Document post in posts.Where(post => post.Kind == DocumentKind.Post))
        {
            foreach (string tag in post.FrontMatter.Tags)
            {
                string key = tag.Trim().ToLowerInvariant();

                if (!groups.TryGetValue(key, out TagGroup? group))
                {
                    group = new TagGroup(tag);
                    groups[key] = group;
                }

                group.Posts.Add(post);
            }
        }

        var pages = new List<TagPage>(groups.Count);
        var bySlug = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string key, TagGroup group) in groups)
        {
            string slug = Slugifier.FromTag(group.DisplayName)
                ?? throw new SiteContentException(
                    $"the tag '{group.DisplayName}' has no characters a URL can be built from.");

            if (bySlug.TryGetValue(slug, out string? other))
            {
                throw new SiteContentException(
                    $"the tags '{other}' and '{group.DisplayName}' would both be published to "
                    + $"/tags/{slug}/. Give one of them a different name.");
            }

            bySlug[slug] = group.DisplayName;
            pages.Add(new TagPage(group.DisplayName, slug, $"/tags/{slug}/", $"tags/{slug}/index.html", PostOrdering.ByDateDescending(group.Posts)));
        }

        // Tag pages are ordered by slug, not by first appearance, so adding a post in the middle
        // of the tree cannot reorder every other tag page.
        pages.Sort((left, right) => string.CompareOrdinal(left.Slug, right.Slug));
        return pages;
    }

    private sealed class TagGroup(string displayName)
    {
        public string DisplayName { get; } = displayName;

        public List<Document> Posts { get; } = [];
    }
}

/// <summary>The list of every published post, newest first.</summary>
/// <param name="UrlPath">Site-absolute path.</param>
/// <param name="OutputPath">Output path.</param>
/// <param name="Posts">Posts, newest first.</param>
public sealed record ArchiveIndex(string UrlPath, string OutputPath, IReadOnlyList<Document> Posts)
{
    /// <summary>Output path of the archive page.</summary>
    public const string ArchiveOutputPath = "archive/index.html";

    /// <summary>URL of the archive page.</summary>
    public const string ArchiveUrlPath = "/archive/";

    /// <summary>Builds the archive from already-filtered documents.</summary>
    public static ArchiveIndex Build(IEnumerable<Document> documents) =>
        new(ArchiveUrlPath, ArchiveOutputPath, PostOrdering.ByDateDescending(documents));

    /// <summary>Groups the posts by year for a page that lists them year by year.</summary>
    public IReadOnlyList<ArchiveYear> ByYear()
    {
        var years = new List<ArchiveYear>();

        foreach (IGrouping<int, Document> group in Posts
            .GroupBy(post => post.PublishedAt!.Value.Year)
            .OrderByDescending(group => group.Key))
        {
            years.Add(new ArchiveYear(group.Key, [.. group]));
        }

        return years;
    }
}

/// <summary>One year of the archive.</summary>
/// <param name="Year">Calendar year.</param>
/// <param name="Posts">Posts in that year, newest first.</param>
public sealed record ArchiveYear(int Year, IReadOnlyList<Document> Posts);
