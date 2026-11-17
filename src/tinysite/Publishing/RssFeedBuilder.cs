using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Tinysite.Content;

namespace Tinysite.Publishing;

/// <summary>
/// Builds an RSS 2.0 feed with <see cref="XDocument"/>, which is what keeps it well-formed:
/// titles, descriptions and URLs are escaped by the XML layer rather than by hand.
/// </summary>
/// <remarks>
/// No <c>lastBuildDate</c> or <c>ttl</c> derived from the clock, because a build that contains
/// the current time is not reproducible. Every value in the feed comes from the documents.
/// </remarks>
public static class RssFeedBuilder
{
    /// <summary>File name of the feed at the site root.</summary>
    public const string FeedFileName = "feed.xml";

    /// <summary>Site-absolute URL of the feed.</summary>
    public const string FeedUrlPath = "/feed.xml";

    /// <summary>
    /// The atom namespace, spelled out: <c>XNamespace.Atom</c> is a common misreading of the
    /// BCL, which exposes no such field.
    /// </summary>
    internal static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// Builds the feed for the newest posts.
    /// </summary>
    /// <param name="posts">Posts to publish, already filtered for drafts; only the newest are kept.</param>
    /// <param name="channelTitle">Site title.</param>
    /// <param name="channelDescription">Site description.</param>
    /// <param name="language">Content language of the site.</param>
    /// <param name="options">Base URL and item limit.</param>
    public static string Build(
        IReadOnlyList<Document> posts,
        string channelTitle,
        string channelDescription,
        string language,
        SiteOptions options)
    {
        ArgumentNullException.ThrowIfNull(posts);
        ArgumentNullException.ThrowIfNull(options);

        string link = options.AbsoluteUrl("/");
        string selfUrl = options.AbsoluteUrl(FeedUrlPath);

        var channel = new XElement("channel",
            new XElement("title", NullToEmpty(channelTitle)),
            new XElement("link", link),
            new XElement("description", NullToEmpty(channelDescription)),
            new XElement("language", NullToEmpty(language)),
            new XElement("generator", GeneratorName),
            // An atom:self link is the one extension RSS readers use to find the feed's own URL.
            new XElement(
                Atom + "link",
                new XAttribute("href", selfUrl),
                new XAttribute("rel", "self")));

        foreach (Document post in posts.Take(Math.Max(0, options.MaxFeedItems)))
        {
            string url = options.AbsoluteUrl(post.UrlPath);
            channel.Add(new XElement("item",
                new XElement("title", NullToEmpty(post.Title)),
                new XElement("link", url),
                new XElement("guid", new XAttribute("isPermaLink", "true"), url),
                new XElement("pubDate", Rfc822Date(post.PublishedAt ?? DateTimeOffset.MinValue)),
                new XElement("description", NullToEmpty(post.Description)),
                post.FrontMatter.Tags.Select(tag => new XElement("category", tag))));
        }

        // The declaration is a node of the document, not a call on the writer: XDocument.WriteTo
        // emits it, and asking the XmlWriter for one as well is an invalid second StartDocument.
        var document = new XDocument(new XElement("rss", new XAttribute("version", "2.0"), channel));

        return WriteToText(document);
    }

    /// <summary>The feed's generator string. Names the tool without pretending to be a version.</summary>
    public const string GeneratorName = "tinysite";

    /// <summary>
    /// Formats a date the way RSS 2.0 asks for: RFC 1123 with a four-digit year, always in GMT.
    /// </summary>
    internal static string Rfc822Date(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);

    private static string NullToEmpty(string? value) => value ?? string.Empty;

    /// <summary>The one line XmlWriter is not allowed to write for itself.</summary>
    internal const string Declaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

    /// <summary>
    /// Serializes with two-space indentation, LF newlines and a UTF-8 declaration, which together
    /// make the bytes identical on every machine. <see cref="SaveOptions.DisableFormatting"/> is
    /// what stops <see cref="XDocument"/> from inserting its own line endings first.
    /// </summary>
    private static string WriteToText(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            // Omitted here and written by hand below: the declaration XmlWriter writes reports the
            // encoding of the *writer*, and a StringWriter is UTF-16, which made bytes that are
            // really UTF-8 announce themselves as UTF-16.
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = true,
        };

        var buffer = new StringBuilder(4096);

        using (var textWriter = new StringWriter(buffer, CultureInfo.InvariantCulture))
        {
            textWriter.NewLine = "\n";
            textWriter.Write(Declaration);
            textWriter.Write("\n");
            using XmlWriter xmlWriter = XmlWriter.Create(textWriter, settings);
            // WriteTo rather than Save: XDocument has no Save(XmlWriter, SaveOptions) in this
            // target, and the indentation is the writer's job, not the document's.
            document.WriteTo(xmlWriter);
            xmlWriter.Flush();
        }

        return buffer.ToString();
    }
}
