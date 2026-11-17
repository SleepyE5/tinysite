namespace Tinysite.Publishing;

/// <summary>Everything one build needs to know.</summary>
public sealed record SiteOptions
{
    /// <summary>Source directory holding <c>content/</c>, <c>layout.html</c> and <c>static/</c>.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Directory the site is written to, created when missing.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// Absolute site root, for example <c>https://example.com</c>. Every link, every tag page URL
    /// and the feed's channel link are built from it, so an RSS reader can resolve items without
    /// knowing anything about the local tree.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>Publish documents marked <c>draft: true</c>. Off by default.</summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>Number of items the feed carries. RSS readers do not need the whole archive.</summary>
    public int MaxFeedItems { get; init; } = 20;

    /// <summary>The base URL without a trailing slash, so links never double up.</summary>
    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');

    /// <summary>
    /// The path part of <see cref="BaseUrl"/> with no trailing slash: empty at a domain root,
    /// <c>/my-site</c> for a GitHub project page.
    /// </summary>
    /// <remarks>
    /// A site published at <c>user.github.io/my-site/</c> cannot hard-code <c>/style.css</c>: that
    /// is the domain root, which is somebody else's document. Every link the generator builds goes
    /// through <see cref="Link"/> so the same tree works at a root and under a path.
    /// </remarks>
    public string BasePath
    {
        get
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? parsed))
            {
                return string.Empty;
            }

            return parsed.AbsolutePath.TrimEnd('/');
        }
    }

    /// <summary>A site-absolute path as a root-relative link, prefixed with the base path.</summary>
    public string Link(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string relative = path.StartsWith('/') ? path : "/" + path;
        string prefix = BasePath;
        return prefix.Length == 0 ? relative : prefix + relative;
    }

    /// <summary>An absolute URL for a site-absolute path.</summary>
    public string AbsoluteUrl(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Length == 0
            ? NormalizedBaseUrl
            : string.Concat(NormalizedBaseUrl, path.StartsWith('/') ? path : "/" + path);
    }
}
