using Tinysite.Metadata;

namespace Tinysite.Content;

/// <summary>
/// The layout template: its HTML with placeholders, plus the site-wide values it can carry in a
/// front matter block of its own.
/// </summary>
/// <param name="Template">Template body, front matter removed.</param>
/// <param name="SiteValues">Keys from the layout's own front matter; empty for the built-in layout.</param>
/// <param name="IsDefault">True when the source tree brought no <c>layout.html</c>.</param>
public sealed record Layout(string Template, FrontMatter SiteValues, bool IsDefault);

/// <summary>A whole source tree, read and parsed, ready to be published.</summary>
/// <param name="Documents">Markdown documents, ordinal sorted by source path.</param>
/// <param name="StaticFiles">Files under <c>static/</c>, ordinal sorted by source path.</param>
/// <param name="Layout">The layout in force.</param>
public sealed record SiteContent(
    IReadOnlyList<Document> Documents,
    IReadOnlyList<StaticFile> StaticFiles,
    Layout Layout);

/// <summary>
/// Reads a tinysite source directory into a <see cref="SiteContent"/>.
/// </summary>
/// <remarks>
/// Layout is <c>layout.html</c> at the source root, content is <c>content/**.md</c> and
/// <c>static/</c> is copied byte for byte. Every enumeration is sorted before it is used:
/// filesystem order is not stable between machines, and published output has to be.
/// </remarks>
public static class SourceReader
{
    /// <summary>Layout file name looked for at the source root.</summary>
    public const string LayoutFileName = "layout.html";

    /// <summary>Directory below the source root that holds Markdown.</summary>
    public const string ContentDirectoryName = "content";

    /// <summary>Directory below the source root copied verbatim.</summary>
    public const string StaticDirectoryName = "static";

    /// <summary>
    /// The layout used when a source tree brings none. It is a real template rather than a stub:
    /// <c>tinysite --source . --output _site</c> has to produce a viewable page for someone who
    /// has not written a layout yet.
    /// </summary>
    public const string DefaultLayoutText = """
        <!doctype html>
        <html lang="{{language}}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{title}}</title>
        <meta name="description" content="{{description}}">
        <link rel="alternate" type="application/rss+xml" title="{{site_title}}" href="{{base_url}}/feed.xml">
        </head>
        <body>
        <header>
        <a href="{{base_path}}/">{{site_title}}</a>
        <nav><a href="{{base_path}}/archive/">Archive</a> <a href="{{base_path}}/tags/">Tags</a></nav>
        </header>
        <main>
        {{content}}
        </main>
        <footer>Published with tinysite.</footer>
        </body>
        </html>
        """;

    /// <summary>Reads, parses and validates everything, reporting all source problems at once.</summary>
    /// <exception cref="SiteContentException">A directory is missing, or two outputs would collide.</exception>
    /// <exception cref="SiteContentProblemException">One or more documents failed to parse.</exception>
    public static SiteContent Read(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);

        string sourceRoot = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new SiteContentException($"source directory '{sourceRoot}' does not exist.");
        }

        string contentRoot = Path.Combine(sourceRoot, ContentDirectoryName);
        if (!Directory.Exists(contentRoot))
        {
            throw new SiteContentException(
                $"'{contentRoot}' is not a directory. Markdown files belong in {ContentDirectoryName}/ "
                + "below the source directory, for example content/posts/2026-03-01-hello.md.");
        }

        Layout layout = ReadLayout(sourceRoot);
        List<Document> documents = ParseDocuments(EnumerateMarkdown(contentRoot));
        List<StaticFile> staticFiles = EnumerateStatic(Path.Combine(sourceRoot, StaticDirectoryName));

        DetectCollisions(documents, staticFiles);

        return new SiteContent(documents, staticFiles, layout);
    }

    private static Layout ReadLayout(string sourceRoot)
    {
        string layoutPath = Path.Combine(sourceRoot, LayoutFileName);
        string text = File.Exists(layoutPath) ? File.ReadAllText(layoutPath) : DefaultLayoutText;

        FrontMatterDocument parsed;

        try
        {
            parsed = FrontMatterParser.Parse(text);
        }
        catch (FrontMatterException exception)
        {
            throw new SiteContentException($"{LayoutFileName}: {exception.Message}", exception);
        }

        return new Layout(parsed.Body, parsed.FrontMatter, File.Exists(layoutPath) is false);
    }

    private static List<Document> ParseDocuments(List<MarkdownFile> files)
    {
        var documents = new List<Document>(files.Count);
        var problems = new List<string>();

        foreach (MarkdownFile file in files)
        {
            try
            {
                documents.Add(Document.Load(file));
            }
            catch (SiteContentException exception)
            {
                problems.Add(exception.Message);
            }
        }

        if (problems.Count > 0)
        {
            throw new SiteContentProblemException(problems);
        }

        return documents;
    }

    private static List<MarkdownFile> EnumerateMarkdown(string contentRoot)
    {
        List<string> paths = EnumerateFiles(contentRoot, ".md");

        if (paths.Count == 0)
        {
            throw new SiteContentException(
                $"'{contentRoot}' contains no .md files, so there is nothing to publish.");
        }

        return [.. paths.Select(path => new MarkdownFile(Relative(contentRoot, path), path))];
    }

    private static List<StaticFile> EnumerateStatic(string staticRoot)
    {
        if (!Directory.Exists(staticRoot))
        {
            return [];
        }

        return [.. EnumerateFiles(staticRoot, extension: null)
            .Select(path => new StaticFile(Relative(staticRoot, path), path))];
    }

    /// <summary>
    /// Collects files recursively and sorts them by full path, ordinal. Sorting here once means
    /// nothing downstream can depend on the order the filesystem happens to hand back.
    /// </summary>
    private static List<string> EnumerateFiles(string root, string? extension)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            foreach (string file in Directory.GetFiles(directory))
            {
                if (extension is null
                    || string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(file);
                }
            }

            foreach (string child in Directory.GetDirectories(directory))
            {
                pending.Push(child);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static string Relative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    /// <summary>
    /// Two documents claiming one output path, or a static file standing where a page goes, is a
    /// build error: silently picking one would publish a site whose content depends on read order.
    /// </summary>
    private static void DetectCollisions(IReadOnlyList<Document> documents, IReadOnlyList<StaticFile> staticFiles)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        foreach (Document document in documents)
        {
            if (owners.TryGetValue(document.OutputPath, out string? previous))
            {
                problems.Add(
                    $"'{previous}' and '{document.SourcePath}' would both be written to "
                    + $"{document.UrlPath} as {document.OutputPath}. Rename one of them.");
                continue;
            }

            owners[document.OutputPath] = document.SourcePath;
        }

        foreach (StaticFile file in staticFiles)
        {
            if (owners.TryGetValue(file.RelativePath, out string? previous))
            {
                problems.Add(
                    $"static/{file.RelativePath} would overwrite {previous}. Move the file.");
                continue;
            }

            owners[file.RelativePath] = $"static/{file.RelativePath}";
        }

        if (problems.Count > 0)
        {
            throw new SiteContentProblemException(problems);
        }
    }
}
