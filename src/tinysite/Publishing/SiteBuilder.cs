using Tinysite.Content;
using Tinysite.Markdown;
using Tinysite.Rendering;

namespace Tinysite.Publishing;

/// <summary>What one build produced, in the order it was written.</summary>
/// <param name="Files">Output paths, relative to the output directory, <c>/</c>-separated.</param>
/// <param name="Documents">Markdown documents published.</param>
/// <param name="Posts">Of those, the dated ones that appear in the feed and the archive.</param>
/// <param name="DraftsSkipped">Documents held back because they are drafts and drafts were not asked for.</param>
/// <param name="Warnings">Things worth reading but not fatal, such as a layout placeholder nothing fills.</param>
public sealed record BuildResult(
    IReadOnlyList<string> Files,
    int Documents,
    int Posts,
    int DraftsSkipped,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Number of files written, which is what a build log reports.</summary>
    public int FileCount => Files.Count;
}

/// <summary>
/// Publishes a source tree into a static site.
/// </summary>
/// <remarks>
/// The order of work is deliberate: read and validate everything first, then write. A source
/// tree with a malformed document therefore produces no output at all rather than a half-built
/// site, because the alternative is a publish that looks like it succeeded.
///
/// Two kinds of page come out of a build. Documents become pages at their own paths. The
/// aggregate pages - home, archive, one per tag, the feed - are generated, and a source tree is
/// allowed to bring its own <c>content/index.md</c>: if it does, the generator stands down for
/// that path and says so in the warnings rather than overwriting what was written by hand.
/// </remarks>
public static class SiteBuilder
{
    /// <summary>Marker written into the output directory; it is what authorises <c>--clean</c> to delete.</summary>
    public const string MarkerFileName = ".tinysite";

    /// <summary>Output path of the generated home page.</summary>
    public const string HomeOutputPath = "index.html";

    /// <summary>Output path of the page that lists the tags.</summary>
    public const string TagListOutputPath = "tags/index.html";

    /// <summary>Placeholder holding the page body. Registered raw, because the converter built it.</summary>
    public const string ContentKey = LayoutRenderer.ContentKey;

    /// <summary>
    /// Builds the site described by <paramref name="options"/>.
    /// </summary>
    /// <exception cref="SiteContentException">The source tree is not readable as a site.</exception>
    /// <exception cref="SiteContentProblemException">One or more documents failed to parse.</exception>
    public static BuildResult Build(SiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        SiteContent content = SourceReader.Read(options.SourceDirectory);

        int draftsSkipped = content.Documents.Count(document => document.IsDraft && !options.IncludeDrafts);
        List<Document> published = [.. content.Documents.Where(document => options.IncludeDrafts || !document.IsDraft)];
        List<Document> posts = [.. published.Where(document => document.Kind == DocumentKind.Post)];

        Dictionary<string, string> siteValues = ReadSiteValues(content.Layout);
        List<string> warnings = [];

        // One entry per placeholder name, however many pages miss it: a typo in a layout is one
        // mistake, and six hundred lines about it is a wall of text that hides the typo.
        var unknowns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        List<string> written = [];

        Directory.CreateDirectory(options.OutputDirectory);

        foreach (Document document in published)
        {
            string html = RenderPage(
                content.Layout.Template,
                placeholders: PagePlaceholders(document, siteValues, options, posts),
                where: document.SourcePath,
                unknowns: unknowns);
            Write(document.OutputPath, html, options.OutputDirectory, written);
        }

        WriteGeneratedPages(content, posts, siteValues, options, warnings, unknowns, written);

        foreach (StaticFile file in content.StaticFiles)
        {
            // Byte for byte: a stylesheet, an image or a favicon has no business being rewritten,
            // and "it is only whitespace" is how a font gets corrupted.
            string target = Path.Combine(options.OutputDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file.FullPath, target, overwrite: true);
            written.Add(file.RelativePath);
        }

        Write(MarkerFileName, MarkerText(published.Count, options), options.OutputDirectory, written);

        foreach ((string name, List<string> pages) in unknowns.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string where = pages.Count == 1
                ? pages[0]
                : $"{pages[0]} and {pages.Count - 1} other page{(pages.Count == 2 ? string.Empty : "s")}";
            warnings.Add($"layout asks for {{{{{name}}}}} and nothing supplies it ({where}).");
        }

        return new BuildResult(written, published.Count, posts.Count, draftsSkipped, warnings);
    }

    private static void WriteGeneratedPages(
        SiteContent content,
        List<Document> posts,
        Dictionary<string, string> siteValues,
        SiteOptions options,
        List<string> warnings,
        IDictionary<string, List<string>> unknowns,
        List<string> written)
    {
        string template = content.Layout.Template;
        HashSet<string> takenByHand = [.. content.Documents.Select(document => document.OutputPath)];

        // Home: the newest posts. A hand-written content/index.md wins, which is why the
        // generated page is skipped rather than merged.
        if (!takenByHand.Contains(HomeOutputPath))
        {
            string body = PostListHtml(posts, options, heading: siteValues.GetValueOrDefault("title", "Posts"));
            string html = RenderPage(template, PagePlaceholders(
                title: siteValues.GetValueOrDefault("title", "Home"),
                description: siteValues.GetValueOrDefault("description", body.Length == 0 ? "Nothing published yet." : "The newest of " + posts.Count + " posts."),
                urlPath: "/",
                bodyHtml: body,
                siteValues: siteValues,
                options: options), where: HomeOutputPath, unknowns);
            Write(HomeOutputPath, html, options.OutputDirectory, written);
        }
        else
        {
            warnings.Add($"{HomeOutputPath} comes from a document in content/; the generated post list was skipped.");
        }

        ArchiveIndex archive = ArchiveIndex.Build(posts);
        if (!takenByHand.Contains(archive.OutputPath))
        {
            string html = RenderPage(template, PagePlaceholders(
                title: "Archive",
                description: $"{posts.Count} posts, newest first.",
                urlPath: archive.UrlPath,
                bodyHtml: ArchiveHtml(archive, options),
                siteValues: siteValues,
                options: options), where: archive.OutputPath, unknowns);
            Write(archive.OutputPath, html, options.OutputDirectory, written);
        }

        IReadOnlyList<TagPage> tags = TagIndex.Build(posts);

        // The default layout links to /tags/, and a generator that ships a link to a page it does
        // not write is a generator whose first build has a 404 in it.
        if (!takenByHand.Contains(TagListOutputPath))
        {
            string html = RenderPage(template, PagePlaceholders(
                title: "Tags",
                description: $"{tags.Count} tag{Suffix(tags.Count)} across {posts.Count} post{Suffix(posts.Count)}.",
                urlPath: "/tags/",
                bodyHtml: TagListHtml(tags, options),
                siteValues: siteValues,
                options: options), where: TagListOutputPath, unknowns);
            Write(TagListOutputPath, html, options.OutputDirectory, written);
        }

        foreach (TagPage tag in tags)
        {
            if (takenByHand.Contains(tag.OutputPath))
            {
                warnings.Add($"{tag.OutputPath} comes from a document in content/; the tag page for '{tag.Tag}' was skipped.");
                continue;
            }

            string html = RenderPage(template, PagePlaceholders(
                title: $"Posts tagged {tag.Tag}",
                description: $"{tag.Posts.Count} posts tagged {tag.Tag}.",
                urlPath: tag.UrlPath,
                bodyHtml: PostListHtml(tag.Posts, options, heading: tag.Tag),
                siteValues: siteValues,
                options: options), where: tag.OutputPath, unknowns);
            Write(tag.OutputPath, html, options.OutputDirectory, written);
        }

        if (!takenByHand.Contains(RssFeedBuilder.FeedFileName))
        {
            string feed = RssFeedBuilder.Build(
                PostOrdering.ByDateDescending(posts),
                siteValues.GetValueOrDefault("title", "Site"),
                siteValues.GetValueOrDefault("description", string.Empty),
                siteValues.GetValueOrDefault("language", "en"),
                options);
            Write(RssFeedBuilder.FeedFileName, feed, options.OutputDirectory, written);
        }
    }

    /// <summary>Placeholders for a document's own page.</summary>
    private static PlaceholderTable PagePlaceholders(
        Document document,
        Dictionary<string, string> siteValues,
        SiteOptions options,
        IReadOnlyList<Document> posts)
    {
        string body = document.Html;
        string tags = document.FrontMatter.Tags.Count == 0
            ? string.Empty
            : $"<p class=\"tags\">{string.Join(" ", document.FrontMatter.Tags.Select(tag => RenderTagLink(tag, options)))}</p>";

        return PagePlaceholders(document.Title, document.Description, document.UrlPath, body + tags, siteValues, options)
            .Add("date", document.DateText)
            .Add("slug", document.Slug)
            .Add("source_path", document.SourcePath)
            .Add("canonical", options.AbsoluteUrl(document.UrlPath))
            .Add("site_title", siteValues.GetValueOrDefault("title", string.Empty))
            .Add("word_count", WordCount(document.Body).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Placeholders shared by every page, generated or authored.</summary>
    private static PlaceholderTable PagePlaceholders(
        string title,
        string description,
        string urlPath,
        string bodyHtml,
        Dictionary<string, string> siteValues,
        SiteOptions options)
        => new PlaceholderTable()
            .Add("title", title)
            .Add("description", description)
            .Add("url", urlPath)
            .Add("canonical", options.AbsoluteUrl(urlPath))
            .Add("base_path", options.BasePath)
            .Add("base_url", options.NormalizedBaseUrl)
            .Add("language", siteValues.GetValueOrDefault("language", "en"))
            .Add("site_title", siteValues.GetValueOrDefault("title", string.Empty))
            .Add("feed_url", options.AbsoluteUrl(RssFeedBuilder.FeedUrlPath))
            // A layout that only ever uses {{full_title}} gets "Hello · My site" on a post and
            // "My site" on the home page, without the author writing the condition.
            .Add("full_title", FullTitle(title, siteValues.GetValueOrDefault("title", string.Empty)))
            // The one raw slot in a page: everything in it was produced by the converter or by
            // the code below, which escapes what came from an author before emitting it.
            .AddRaw(ContentKey, bodyHtml);

    /// <summary>Page title with the site appended, unless the page already is the site.</summary>
    internal static string FullTitle(string title, string siteTitle)
    {
        if (siteTitle.Length == 0)
        {
            return title;
        }

        return string.Equals(title, siteTitle, StringComparison.Ordinal) ? siteTitle : $"{title} · {siteTitle}";
    }

    private static string RenderTagLink(string tag, SiteOptions options)
    {
        string slug = Slugifier.FromTag(tag) ?? Slugifier.FromFileName(tag);
        return $"<a href=\"{options.Link($"/tags/{slug}/")}\">{HtmlEscape.Escape(tag)}</a>";
    }

    /// <summary>The post list the home page and each tag page are made of.</summary>
    internal static string PostListHtml(IReadOnlyList<Document> posts, SiteOptions options, string heading)
    {
        if (posts.Count == 0)
        {
            return $"<h1>{HtmlEscape.Escape(heading)}</h1>\n<p>Nothing here yet.</p>\n";
        }

        var builder = new System.Text.StringBuilder();
        _ = builder.Append("<h1>").Append(HtmlEscape.Escape(heading)).AppendLine("</h1>");
        _ = builder.AppendLine("<ul class=\"posts\">");
        foreach (Document post in posts)
        {
            _ = builder.Append("  <li><a href=\"").Append(options.Link(post.UrlPath)).Append("\">")
                .Append(HtmlEscape.Escape(post.Title)).Append("</a> <time>")
                .Append(post.DateText).Append("</time>");
            if (post.FrontMatter.Tags.Count > 0)
            {
                _ = builder.Append(" <span class=\"tags\">").Append(string.Join(" ", post.FrontMatter.Tags.Select(tag => RenderTagLink(tag, options)))).Append("</span>");
            }

            _ = builder.AppendLine("</li>");
        }

        _ = builder.Append("</ul>");
        return builder.ToString();
    }

    /// <summary>The tag list, with each tag's count, so an empty taxonomy is visible.</summary>
    internal static string TagListHtml(IReadOnlyList<TagPage> tags, SiteOptions options)
    {
        var builder = new System.Text.StringBuilder();
        _ = builder.AppendLine("<h1>Tags</h1>");
        if (tags.Count == 0)
        {
            _ = builder.AppendLine("<p>No tags yet. A tag appears when a post names one in its front matter.</p>");
            return builder.ToString();
        }

        _ = builder.AppendLine("<ul class=\"tags\">");
        foreach (TagPage tag in tags)
        {
            _ = builder.Append("  <li><a href=\"").Append(options.Link(tag.UrlPath)).Append("\">")
                .Append(HtmlEscape.Escape(tag.Tag)).Append("</a> (")
                .Append(tag.Posts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(")</li>");
        }

        _ = builder.Append("</ul>");
        return builder.ToString();
    }

    private static string Suffix(int count) => count == 1 ? string.Empty : "s";

    /// <summary>The archive, one <c>&lt;section&gt;</c> per year.</summary>
    internal static string ArchiveHtml(ArchiveIndex archive, SiteOptions options)
    {
        var builder = new System.Text.StringBuilder();
        _ = builder.AppendLine("<h1>Archive</h1>");
        foreach (ArchiveYear year in archive.ByYear())
        {
            _ = builder.Append("<section><h2>").Append(year.Year.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine("</h2>");
            _ = builder.Append(PostListHtml(year.Posts, options, heading: string.Empty)
                .Replace("<h1></h1>\n", string.Empty));
            _ = builder.AppendLine("</section>");
        }

        if (archive.Posts.Count == 0)
        {
            _ = builder.Append(PostListHtml([], options, heading: "Archive"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The site-wide values a layout declares in its own front matter block: title, description,
    /// language. Anything else in that block is ignored rather than guessed at.
    /// </summary>
    internal static Dictionary<string, string> ReadSiteValues(Layout layout)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in new[] { "title", "description", "language" })
        {
            if (layout.SiteValues.TryGetValue(key, out string value))
            {
                values[key] = value;
            }
        }

        _ = values.TryAdd("language", "en");
        return values;
    }

    private static int WordCount(string body) => body
        .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
        .Length;

    private static string RenderPage(
        string template,
        PlaceholderTable placeholders,
        string where,
        IDictionary<string, List<string>> unknowns)
    {
        LayoutRenderResult result = LayoutRenderer.Render(template, placeholders);
        foreach (string unknown in result.UnknownPlaceholders)
        {
            // Not fatal: a layout can mention something this kind of page does not carry, and
            // failing the build over it would punish every other page. But it is the reason a page
            // comes out with a hole in it, so the build says it once, naming a page to start from.
            if (!unknowns.TryGetValue(unknown, out List<string>? pages))
            {
                pages = [];
                unknowns[unknown] = pages;
            }

            if (!pages.Contains(where, StringComparer.Ordinal))
            {
                pages.Add(where);
            }
        }

        return result.Html;
    }

    private static void Write(string relativePath, string text, string outputDirectory, List<string> written)
    {
        string target = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        written.Add(relativePath);
    }

    private static string MarkerText(int documents, SiteOptions options) =>
        $"tinysite build\nsource: {options.SourceDirectory}\ndocuments: {documents}\n";

    /// <summary>
    /// Empties an output directory, but only when it carries the marker this tool writes.
    /// </summary>
    /// <remarks>
    /// A build that deletes a directory is the one thing in this program that can destroy work,
    /// so it is refused unless the marker proves the directory is generated output. Pointing
    /// <c>--output</c> at a hand-written site then costs a message, not the site.
    /// </remarks>
    /// <exception cref="SiteContentException">The directory holds files this tool did not write.</exception>
    public static void Clean(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        string marker = Path.Combine(outputDirectory, MarkerFileName);
        if (!File.Exists(marker))
        {
            throw new SiteContentException(
                $"refusing to clean '{outputDirectory}': it has no {MarkerFileName} marker, so this tool cannot prove it wrote what is in there.");
        }

        foreach (string file in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !path.EndsWith(MarkerFileName, StringComparison.Ordinal))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            File.Delete(file);
        }
    }

    /// <summary>True when a directory looks like a tinysite source tree, for a friendlier CLI error.</summary>
    public static bool LooksLikeSource(string directory) =>
        Directory.Exists(Path.Combine(directory, SourceReader.ContentDirectoryName))
        || File.Exists(Path.Combine(directory, SourceReader.LayoutFileName));
}
