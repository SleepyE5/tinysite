using System.Text;
using Tinysite.Cli;
using Tinysite.Content;
using Tinysite.Publishing;

namespace Tinysite.Tests.Publishing;

/// <summary>
/// End-to-end behaviour of one build: which files appear, what is in them, and what the tool does
/// when the source is surprising.
/// </summary>
public sealed class SiteBuilderTests
{
    private static BuildResult Publish(TestTree source, TestTree output, bool drafts = false, string baseUrl = "https://site.example") =>
        SiteBuilder.Build(new SiteOptions
        {
            SourceDirectory = source.Root,
            OutputDirectory = output.Root,
            BaseUrl = baseUrl,
            IncludeDrafts = drafts,
        });

    [Fact]
    public void Build_ProducesEveryPageTheScaffoldLayoutLinksTo()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        BuildResult result = Publish(source, output);

        // The default layout points at /archive/ and /tags/. A generator whose own template links
        // to a page it never writes ships a 404.
        // Written in dependency order, so the set is what a reader wants to see; the CLI prints it
        // sorted, and Build_WritesTheSameBytesTwice pins that the order itself is stable.
        Assert.Equal(
            [".tinysite", "about/index.html", "archive/index.html", "feed.xml", "index.html",
                "posts/hello/index.html", "style.css", "tags/first/index.html", "tags/index.html"],
            result.Files.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, result.Documents);
        Assert.Equal(1, result.Posts);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Build_ListsTheDatedPostOnTheHomePageAndItsTagPage()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        _ = Publish(source, output);

        string home = output.Read("index.html");
        Assert.Contains("<a href=\"/posts/hello/\">Hello</a>", home, StringComparison.Ordinal);
        Assert.Contains("<time>2026-01-01</time>", home, StringComparison.Ordinal);
        Assert.Contains("<a href=\"/tags/first/\">first</a>", home, StringComparison.Ordinal);

        string tag = output.Read("tags/first/index.html");
        Assert.Contains("<h1>first</h1>", tag, StringComparison.Ordinal);
        Assert.Contains("posts/hello", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesTheSameBytesTwice()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree first = TestTree.Create("out-a");
        using TestTree second = TestTree.Create("out-b");

        _ = Publish(source, first);
        _ = Publish(source, second);

        Assert.Equal(first.Tree(), second.Tree());
        foreach (string file in first.Tree())
        {
            Assert.Equal(first.ReadBytes(file), second.ReadBytes(file));
        }
    }

    [Fact]
    public void Build_KeepsStaticFilesByteForByte()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        // A BOM and a CRLF are the two things a "helpful" writer removes from a file it did not
        // need to touch at all.
        byte[] raw = [0xEF, 0xBB, 0xBF, (byte)'<', (byte)'b', (byte)'\r', (byte)'\n', 0x00, 0xFF];
        _ = source.WriteBytes("static/blob.bin", raw);

        _ = Publish(source, output);

        Assert.Equal(raw, output.ReadBytes("blob.bin"));
    }

    [Fact]
    public void Build_HoldsBackDraftsAndSaysHowMany()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/posts/2026-02-02-secret.md", "---\ntitle: Secret\ndraft: true\n---\nNot yet.\n");
        using TestTree output = TestTree.Create("out");

        BuildResult hidden = Publish(source, output);
        Assert.Equal(1, hidden.DraftsSkipped);
        Assert.False(output.Exists("posts/secret/index.html"));
        Assert.DoesNotContain("Secret", output.Read("feed.xml"), StringComparison.Ordinal);

        using TestTree shown = TestTree.Create("out-drafts");
        BuildResult visible = Publish(source, shown, drafts: true);
        Assert.Equal(0, visible.DraftsSkipped);
        Assert.True(shown.Exists("posts/secret/index.html"));
        Assert.Contains("Secret", shown.Read("feed.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LeavesNothingWrittenWhenADocumentIsBroken()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/broken.md", "---\ntitle: Broken\n:date: nonsense\n---\nbody\n");
        using TestTree output = TestTree.Create("out");

        SiteContentProblemException problem = Assert.Throws<SiteContentProblemException>(
            () => Publish(source, output));

        // Source paths in the message are relative to content/, which is where the author looks.
        Assert.Contains(problem.Messages, message => message.StartsWith("broken.md", StringComparison.Ordinal));

        // The claim being tested: a source that cannot be read produces no output at all, rather
        // than a site that published 40 of 41 pages and looks finished.
        Assert.Empty(output.Tree());
    }

    [Fact]
    public void Build_ReportsALayoutPlaceholderNothingSupplies()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write(SourceReader.LayoutFileName, "<html><body>{{content}} {{nope}}</body></html>");
        using TestTree output = TestTree.Create("out");

        BuildResult result = Publish(source, output);

        // Every page misses it; the layout is the one mistake, so it is reported once, naming a
        // page to start from and counting the rest rather than repeating itself.
        string warning = Assert.Single(result.Warnings, message => message.Contains("nope", StringComparison.Ordinal));
        Assert.Matches(@"\{\{nope\}\} and nothing supplies it \([^)]+ and \d+ other pages\)", warning);
    }

    [Fact]
    public void Build_LetsADocumentInContentClaimTheHomePage()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/index.md", "---\ntitle: Handwritten\n---\nWelcome.\n");
        using TestTree output = TestTree.Create("out");

        BuildResult result = Publish(source, output);

        Assert.Contains("Handwritten", output.Read("index.html"), StringComparison.Ordinal);
        Assert.DoesNotContain("<time>2026-01-01</time>", output.Read("index.html"), StringComparison.Ordinal);
        Assert.Contains(result.Warnings, message => message.Contains("generated post list was skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_EscapesAuthorInputInTheHeadButNotTheConvertedBody()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/xss.md", "---\ntitle: \"<script>alert(1)</script>\"\ndescription: \"\\\" onmouseover=\\\"evil()\"\n---\nReal **markup** here.\n");
        using TestTree output = TestTree.Create("out");

        _ = Publish(source, output);

        string page = output.Read("xss/index.html");
        Assert.DoesNotContain("<script>alert(1)</script>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page, StringComparison.Ordinal);
        Assert.Contains("&quot; onmouseover=", page, StringComparison.Ordinal);

        // ...and the body the converter produced still has its elements.
        Assert.Contains("<strong>markup</strong>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UsesFrontMatterDateWhenBothNameAndFieldGiveOne()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/posts/2026-01-01-clash.md", "---\ntitle: Clash\ndate: 2024-06-15\n---\nbody\n");
        using TestTree output = TestTree.Create("out");

        _ = Publish(source, output);

        // The field is the explicit statement, so it wins; the name still sets the URL.
        Assert.Contains("2024-06-15", output.Read("archive/index.html"), StringComparison.Ordinal);
        Assert.True(output.Exists("posts/clash/index.html"));
    }

    [Fact]
    public void Build_WritesFeedItemsWithAbsoluteUrlsAndGmtDates()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        _ = Publish(source, output, baseUrl: "https://site.example/blog/");

        string feed = output.Read("feed.xml");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", feed, StringComparison.Ordinal);

        // A trailing slash in --base-url must not become //posts/.
        Assert.Contains("<link>https://site.example/blog/posts/hello/</link>", feed, StringComparison.Ordinal);
        Assert.Contains("<pubDate>Thu, 01 Jan 2026 00:00:00 GMT</pubDate>", feed, StringComparison.Ordinal);
        Assert.Contains("<category>first</category>", feed, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PrefixesEveryGeneratedLinkWithTheBasePathOfAProjectSite()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree rootSite = TestTree.Create("out-root");
        using TestTree subSite = TestTree.Create("out-sub");

        _ = Publish(source, rootSite, baseUrl: "https://sleepye5.github.io");
        _ = Publish(source, subSite, baseUrl: "https://sleepye5.github.io/fablerift/");

        // A page under a project path cannot ask the domain root for its own stylesheet.
        Assert.Contains(@"href=""/style.css""", rootSite.Read("index.html"), StringComparison.Ordinal);
        Assert.Contains(@"href=""/fablerift/style.css""", subSite.Read("index.html"), StringComparison.Ordinal);
        Assert.Contains(@"<a href=""/fablerift/posts/hello/"">", subSite.Read("index.html"), StringComparison.Ordinal);
        Assert.Contains(@"<a href=""/fablerift/tags/first/"">", subSite.Read("index.html"), StringComparison.Ordinal);

        // The feed, on the other hand, must be absolute, and stays that way.
        Assert.Contains("<link>https://sleepye5.github.io/fablerift/posts/hello/</link>",
            subSite.Read("feed.xml"), StringComparison.Ordinal);

        // Every page carries the nav, so the prefix has to reach a document's own rendering too.
        Assert.Contains(@"<a href=""/fablerift/tags/"">Tags</a>", subSite.Read("about/index.html"), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NeverPutsTheCurrentTimeInTheOutput()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        _ = Publish(source, output);
        string firstFeed = output.Read("feed.xml");

        Thread.Sleep(1100);
        _ = Publish(source, output);

        // lastBuildDate would make every build differ and every diff a lie about content.
        Assert.Equal(firstFeed, output.Read("feed.xml"));
        Assert.DoesNotContain("lastBuildDate", output.Read("feed.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_RefusesADirectoryItDidNotWrite()
    {
        using TestTree target = TestTree.Create("somebody-elses-site");
        _ = target.Write("index.html", "<p>handwritten</p>");

        SiteContentException refusal = Assert.Throws<SiteContentException>(() => SiteBuilder.Clean(target.Root));

        Assert.Contains(SiteBuilder.MarkerFileName, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("handwritten", target.Read("index.html"), StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_EmptiesADirectoryTheToolWroteButKeepsTheMarker()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");
        _ = Publish(source, output);
        Assert.NotEmpty(output.Tree());

        SiteBuilder.Clean(output.Root);

        Assert.Equal([SiteBuilder.MarkerFileName], output.Tree());
    }

    [Fact]
    public void Clean_IsSilentWhenTheDirectoryDoesNotExistYet()
    {
        using TestTree parent = TestTree.Create("out-parent");
        string missing = Path.Combine(parent.Root, "never-created");

        SiteBuilder.Clean(missing);

        Assert.False(Directory.Exists(missing));
    }
}
