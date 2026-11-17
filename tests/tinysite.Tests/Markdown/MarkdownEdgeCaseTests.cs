using Tinysite.Markdown;

namespace Tinysite.Tests;

/// <summary>
/// The cases that decide whether a hand-written parser is trustworthy: things that look like
/// markup inside markup, and anything that could reach a browser as live HTML.
/// </summary>
public sealed class MarkdownEdgeCaseTests
{
    private static string Md(string markedMarkdown) => MarkdownConverter.ToHtml(markedMarkdown.Replace("<NL>", "\n"));

    [Fact]
    public void FencedBlockKeepsSharpAndScriptLiteral()
    {
        const string markdown = """
            ```text
            # not a heading
            </script>
            <script>alert(1)</script>
            ```
            """;

        const string expected = """
            <pre><code class="language-text"># not a heading
            &lt;/script&gt;
            &lt;script&gt;alert(1)&lt;/script&gt;</code></pre>
            """;

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown));
    }

    [Fact]
    public void FencedBlockInsideBlockquoteIsStillCode()
    {
        const string markdown = """
            > ```
            > <script>no</script>
            > ```
            """;

        string html = MarkdownConverter.ToHtml(markdown);

        Assert.Contains("<pre><code>&lt;script&gt;no&lt;/script&gt;</code></pre>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkDestinationWithParenthesesMatchesTheRightCloser()
    {
        Assert.Equal(
            "<p><a href=\"/wiki/Foo_(bar)\">wiki</a></p>",
            Md("[wiki](/wiki/Foo_(bar))"));
    }

    [Fact]
    public void LinkTitleWithParenthesesIsNotTruncated()
    {
        Assert.Equal(
            "<p><a href=\"/x\" title=\"a (b) c\">t</a></p>",
            Md("[t](/x \"a (b) c\")"));
    }

    [Fact]
    public void LinkLabelWithParenthesesInside()
    {
        Assert.Equal(
            "<p><a href=\"/x\">a (nested) label</a></p>",
            Md("[a (nested) label](/x)"));
    }

    [Fact]
    public void ImageDestinationWithParenthesesInside()
    {
        Assert.Equal(
            "<p><img src=\"/a(1).png\" alt=\"x\" /></p>",
            Md("![x](/a(1).png)"));
    }

    [Fact]
    public void EmphasisMarkerWithoutCloserStaysLiteral()
    {
        Assert.Equal("<p>*oops, never closed</p>", Md("*oops, never closed"));
        Assert.Equal("<p>**double oops</p>", Md("**double oops"));
        Assert.Equal("<p>mixed **open *but not closed</p>", Md("mixed **open *but not closed"));
    }

    [Fact]
    public void EmphasisMarkerWithOnlyWhitespaceInsideStaysLiteral()
    {
        // "* not emphasis *" is a bullet marker in disguise at line start, so test it inline.
        Assert.Equal("<p>say * not emphasis * loudly</p>", Md("say * not emphasis * loudly"));
        Assert.Equal("<p>space * * spaced</p>", Md("space * * spaced"));
    }

    [Theory]
    [InlineData("\\*not emphasis\\*", "<p>*not emphasis*</p>")]
    [InlineData("\\<script\\>", "<p>&lt;script&gt;</p>")]
    [InlineData("\\# not a heading", "<p># not a heading</p>")]
    [InlineData("\\[not a link\\](/x)", "<p>[not a link](/x)</p>")]
    [InlineData("a \\` b", "<p>a ` b</p>")]
    [InlineData("\\\\backslash", "<p>\\backslash</p>")]
    [InlineData("escaped __underscores__", "<p>escaped <strong>underscores</strong></p>")]
    [InlineData("C:\\path\\is\\literal", "<p>C:\\path\\is\\literal</p>")]
    public void BackslashEscapesPunctuation(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Fact]
    public void BackslashBeforeAnOrdinaryLetterIsKept() =>
        Assert.Equal("<p>a\\b</p>", Md("a\\b"));

    /// <summary>
    /// List items hold a single run of text: the fence lines are item content, not a block,
    /// and the angle brackets inside them must still come out escaped.
    /// </summary>
    [Fact]
    public void MarkupInsideListItemContentIsEscapedNotParsed()
    {
        string html = Md("- item<NL>  ```<NL>  <b>x</b><NL>  ```");

        Assert.DoesNotContain("<pre>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BareSpaceInDestinationIsNotALink()
    {
        Assert.Equal("<p><img src=\"/a%20b.png\" alt=\"x\" /></p>", Md("![x](</a b.png>)"));
        Assert.Equal("<p>![x](a b.png)</p>", Md("![x](a b.png)"));
        Assert.Equal("<p>[t](a b.png)</p>", Md("[t](a b.png)"));
    }

    [Fact]
    public void CrlfInputProducesTheSameHtmlAsLfInput()
    {
        string lf = "# Title\n\nPara **one** and _two_.\n\n- a\n- b\n";
        string crlf = lf.Replace("\n", "\r\n");

        Assert.Equal(MarkdownConverter.ToHtml(lf), MarkdownConverter.ToHtml(crlf));
    }

    [Fact]
    public void BlankLinesAndTrailingWhitespaceDoNotProduceEmptyBlocks() =>
        Assert.Equal("<p>only</p>", Md("<NL><NL>only<NL><NL><NL>"));

    [Fact]
    public void EmptyDocumentProducesEmptyOutput()
    {
        Assert.Equal(string.Empty, MarkdownConverter.ToHtml(string.Empty));
        Assert.Equal(string.Empty, MarkdownConverter.ToHtml("\n\n\n"));
    }

    [Fact]
    public void DocumentThatIsOnlyFrontMatterConvertsToNothing()
    {
        const string text = """
            ---
            title: Empty
            date: 2026-04-01
            ---
            """;

        Tinysite.Metadata.FrontMatterDocument document = Tinysite.Metadata.FrontMatterParser.Parse(text);

        Assert.Equal(string.Empty, document.Body);
        Assert.Equal(string.Empty, MarkdownConverter.ToHtml(document.Body));
    }

    [Theory]
    [InlineData("naïve café résumé", "<p>naïve café résumé</p>")]
    [InlineData("日本語のお題", "<p>日本語のお題</p>")]
    [InlineData("Эм дashes — and ‘quotes’", "<p>Эм дashes — and ‘quotes’</p>")]
    public void UnicodeTextPassesThroughUnchanged(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Fact]
    public void EmojiPassThroughIncludingSurrogatePairsAndJoiners()
    {
        string markdown = "Ship it 🚀 family 👩‍👩‍👧‍👦 flag 🇯🇵 emoji 👍🏽";

        string html = MarkdownConverter.ToHtml(markdown);

        Assert.Equal($"<p>{markdown}</p>", html);
        Assert.DoesNotContain("&#", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SurrogatePairIsNotSplitByInlineScanning()
    {
        string html = Md("**bold 🚀 rocket**");

        Assert.Equal("<p><strong>bold 🚀 rocket</strong></p>", html);
    }

    [Fact]
    public void ZeroDepthBudgetStillEscapesWhenNestingIsExtreme()
    {
        string markdown = string.Concat(new string('*', 400), "deep", new string('*', 400));

        string html = MarkdownConverter.ToHtml(markdown);

        Assert.StartsWith("<p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<em><em><em>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ListMarkerWithOnlyMarkerAndSpacesIsNotAnItem()
    {
        Assert.Equal("<p>-</p>", Md("-"));
        Assert.Equal("<p>1.</p>", Md("1."));
    }

    [Fact]
    public void DeeplyNestedBlockquotesRemainBalanced()
    {
        string html = Md("> > > > four levels");

        Assert.Equal(4, html.Split("<blockquote>").Length - 1);
        Assert.Equal(4, html.Split("</blockquote>").Length - 1);
    }

    [Fact]
    public void NestingBeyondTheBudgetDegradesInsteadOfThrowing()
    {
        string markdown = string.Concat(Enumerable.Repeat("> ", 40)) + "deep";

        string html = MarkdownConverter.ToHtml(markdown);

        Assert.Contains("deep", html, StringComparison.Ordinal);
    }
}
