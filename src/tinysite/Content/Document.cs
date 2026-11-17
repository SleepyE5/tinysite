using Tinysite.Metadata;

namespace Tinysite.Content;

/// <summary>
/// One Markdown file, parsed: front matter, body, and the URL and output path it maps to.
/// </summary>
public sealed class Document
{
    /// <summary>Path below the content root, with <c>/</c> separators, e.g. <c>posts/hello.md</c>.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Directory part of <see cref="SourcePath"/>, empty at the content root.</summary>
    public required string Directory { get; init; }

    /// <summary>File name without its extension.</summary>
    public required string FileName { get; init; }

    public required FrontMatter FrontMatter { get; init; }

    /// <summary>Markdown body with the front matter block removed.</summary>
    public required string Body { get; init; }

    /// <summary>URL fragment from the file name, date prefix removed.</summary>
    public required string Slug { get; init; }

    /// <summary>Site-absolute URL path, e.g. <c>/posts/hello/</c>.</summary>
    public required string UrlPath { get; init; }

    /// <summary>Output path relative to the site root, e.g. <c>posts/hello/index.html</c>.</summary>
    public required string OutputPath { get; init; }

    public DocumentKind Kind { get; init; }

    /// <summary>The converted HTML of <see cref="Body"/>, computed once at load time.</summary>
    public required string Html { get; init; }

    public bool IsDraft => FrontMatter.Draft;

    public DateTimeOffset? PublishedAt => FrontMatter.PublishedAt;

    /// <summary>Date as text for a list; pages without a date say so instead of printing nothing.</summary>
    public string DateText => FrontMatter.FormattedDate ?? "undated";

    /// <summary>
    /// Title from front matter, else the first ATX heading in the body, else the slug. Without
    /// the fallbacks a post publishes an empty <c>&lt;title&gt;</c>, which is the most visible
    /// thing about it.
    /// </summary>
    public string Title => FrontMatter.Title ?? FirstHeading() ?? Slug;

    /// <summary>Description from front matter, else the first prose paragraph.</summary>
    public string Description
    {
        get
        {
            if (FrontMatter.TryGetValue("description", out string configured) && configured.Length > 0)
            {
                return configured;
            }

            return PlainText.SummaryOf(Body);
        }
    }

    /// <summary>Reads one file from disk, parsing its front matter and converting its body.</summary>
    /// <exception cref="SiteContentException">The front matter block is malformed.</exception>
    public static Document Load(MarkdownFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        FrontMatterDocument parsed;

        try
        {
            parsed = FrontMatterParser.Parse(File.ReadAllText(file.FullPath));
        }
        catch (FrontMatterException exception)
        {
            throw new SiteContentException($"{file.RelativePath}: {exception.Message}", exception);
        }

        return FromMarkdown(file.RelativePath, parsed);
    }

    /// <summary>
    /// Maps a content-relative path and a parsed document to a <see cref="Document"/>. Kept
    /// separate from disk access so path, slug and kind rules are testable without a file.
    /// </summary>
    public static Document FromMarkdown(string relativePath, FrontMatterDocument parsed)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        ArgumentNullException.ThrowIfNull(parsed);

        string normalized = relativePath.Replace('\\', '/');
        int lastSlash = normalized.LastIndexOf('/');
        string directory = lastSlash < 0 ? string.Empty : normalized[..lastSlash];
        string fileName = Path.GetFileNameWithoutExtension(normalized[(lastSlash + 1)..]);

        // A date in the file name is a publication date. Front matter wins when it says one too,
        // so the two can never disagree in silence: the name is the default, not the override.
        FrontMatter frontMatter = parsed.FrontMatter;
        if (frontMatter.PublishedAt is null && Slugifier.TryReadDatePrefix(fileName, out DateTimeOffset fromName))
        {
            frontMatter = frontMatter with { PublishedAt = fromName };
        }

        string slug = Slugifier.FromFileName(fileName);
        string urlPath = BuildUrlPath(directory, slug);
        // "/about/" -> "about/index.html". The separator has to be written: the URL's trailing
        // slash is what it stands in for, and dropping it produced "aboutindex.html".
        string outputPath = urlPath.Length == 1
            ? "index.html"
            : string.Concat(urlPath.TrimStart('/'), "index.html");

        return new Document
        {
            SourcePath = normalized,
            Directory = directory,
            FileName = fileName,
            FrontMatter = frontMatter,
            Body = parsed.Body,
            Slug = slug,
            UrlPath = urlPath,
            OutputPath = outputPath,
            Kind = frontMatter.PublishedAt is null ? DocumentKind.Page : DocumentKind.Post,
            Html = Markdown.MarkdownConverter.ToHtml(parsed.Body),
        };
    }

    /// <summary>
    /// Trailing-slash URL paths, with <c>index.md</c> claiming its directory. A slug of
    /// <c>index</c> at the content root is the site home page.
    /// </summary>
    internal static string BuildUrlPath(string directory, string slug)
    {
        string prefix = directory.Length == 0
            ? "/"
            : '/' + SlugPathSegments(directory) + '/';

        return slug.Equals("index", StringComparison.Ordinal) ? prefix : prefix + slug + '/';
    }

    /// <summary>Slugs each directory segment, so a folder called "My Posts" cannot emit a URL with a space.</summary>
    private static string SlugPathSegments(string directory)
    {
        string[] segments = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', segments.Select(Slugifier.FromFileName));
    }

    private string? FirstHeading()
    {
        foreach (string line in Body.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith('#'))
            {
                continue;
            }

            int index = 0;
            while (index < trimmed.Length && trimmed[index] == '#')
            {
                index++;
            }

            while (index < trimmed.Length && (trimmed[index] == ' ' || trimmed[index] == '\t'))
            {
                index++;
            }

            string heading = PlainText.StripMarkup(trimmed[index..].Trim().TrimEnd('#', ' ', '\t'));
            if (heading.Length > 0)
            {
                return heading;
            }
        }

        return null;
    }
}
