using Tinysite.Markdown;

namespace Tinysite.Tests;

/// <summary>
/// Table-driven coverage of the hand-written converter. The Markdown under test is written
/// with a &lt;NL&gt; marker because attribute arguments cannot span lines.
/// </summary>
public sealed class MarkdownConverterTests
{
    private static string Md(string markedMarkdown) => MarkdownConverter.ToHtml(markedMarkdown.Replace("<NL>", "\n"));

    [Theory]
    [InlineData("# One", "<h1>One</h1>")]
    [InlineData("## Two", "<h2>Two</h2>")]
    [InlineData("###### Six", "<h6>Six</h6>")]
    [InlineData("####### Seven hashes is not a heading", "<p>####### Seven hashes is not a heading</p>")]
    [InlineData("# heading#######", "<h1>heading#######</h1>")]
    [InlineData("# Heading with trailing hashes ##", "<h1>Heading with trailing hashes</h1>")]
    [InlineData("#No space, no heading", "<p>#No space, no heading</p>")]
    [InlineData("# Inline *em* here", "<h1>Inline <em>em</em> here</h1>")]
    public void ConvertsHeadings(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("Just a line.", "<p>Just a line.</p>")]
    [InlineData("Two<NL>lines", "<p>Two\nlines</p>")]
    [InlineData("One<NL><NL>Two", "<p>One</p>\n<p>Two</p>")]
    [InlineData("Lead<NL># Head<NL>Trail", "<p>Lead</p>\n<h1>Head</h1>\n<p>Trail</p>")]
    public void ConvertsParagraphs(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("*em*", "<p><em>em</em></p>")]
    [InlineData("_em_", "<p><em>em</em></p>")]
    [InlineData("**strong**", "<p><strong>strong</strong></p>")]
    [InlineData("__strong__", "<p><strong>strong</strong></p>")]
    [InlineData("***both***", "<p><strong><em>both</em></strong></p>")]
    [InlineData("**bold *and* italic**", "<p><strong>bold <em>and</em> italic</strong></p>")]
    [InlineData("snake_case_name stays", "<p>snake_case_name stays</p>")]
    [InlineData("un_matching star", "<p>un_matching star</p>")]
    [InlineData("a * b * c", "<p>a * b * c</p>")]
    [InlineData("2*3*4 keeps stars", "<p>2<em>3</em>4 keeps stars</p>")]
    public void ConvertsEmphasisAndStrong(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("`code`", "<p><code>code</code></p>")]
    [InlineData("`a < b & c`", "<p><code>a &lt; b &amp; c</code></p>")]
    [InlineData("`**not bold**`", "<p><code>**not bold**</code></p>")]
    [InlineData("`` `tick` ``", "<p><code>`tick`</code></p>")]
    [InlineData("`` spaced ``", "<p><code>spaced</code></p>")]
    [InlineData("`unclosed code", "<p>`unclosed code</p>")]
    [InlineData("see `a` and `b`", "<p>see <code>a</code> and <code>b</code></p>")]
    public void ConvertsInlineCode(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("```<NL>x = 1<NL>```", "<pre><code>x = 1</code></pre>")]
    [InlineData("```csharp<NL>int x = 1;<NL>```", "<pre><code class=\"language-csharp\">int x = 1;</code></pre>")]
    [InlineData("~~~<NL>fenced by tildes<NL>~~~", "<pre><code>fenced by tildes</code></pre>")]
    [InlineData("```<NL>a<NL>b<NL>```", "<pre><code>a\nb</code></pre>")]
    [InlineData("```<NL>```", "<pre><code></code></pre>")]
    [InlineData("```<NL>never closed<NL>still code", "<pre><code>never closed\nstill code</code></pre>")]
    public void ConvertsFencedCodeBlocks(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("[text](https://example.com)", "<p><a href=\"https://example.com\">text</a></p>")]
    [InlineData("[text](/relative)", "<p><a href=\"/relative\">text</a></p>")]
    [InlineData("[text](#section)", "<p><a href=\"#section\">text</a></p>")]
    [InlineData("[text](https://example.com \"The title\")", "<p><a href=\"https://example.com\" title=\"The title\">text</a></p>")]
    [InlineData("[angle](</docs/page> \"title\")", "<p><a href=\"/docs/page\" title=\"title\">angle</a></p>")]
    [InlineData("[spaces](/a%20b)", "<p><a href=\"/a%20b\">spaces</a></p>")]
    [InlineData("[search](https://example.com/?a=1&b=2)", "<p><a href=\"https://example.com/?a=1&amp;b=2\">search</a></p>")]
    [InlineData("[**bold** link](/x)", "<p><a href=\"/x\"><strong>bold</strong> link</a></p>")]
    [InlineData("[no target]", "<p>[no target]</p>")]
    [InlineData("[unclosed](/x", "<p>[unclosed](/x</p>")]
    [InlineData("[text]( )", "<p><a href=\"\">text</a></p>")]
    public void ConvertsLinks(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("![alt](/img.png)", "<p><img src=\"/img.png\" alt=\"alt\" /></p>")]
    [InlineData("![alt](/img.png \"title\")", "<p><img src=\"/img.png\" alt=\"alt\" title=\"title\" /></p>")]
    [InlineData("![](empty alt)", "<p>![](empty alt)</p>")]
    [InlineData("!not an image", "<p>!not an image</p>")]
    [InlineData("![a<b](/x.png)", "<p><img src=\"/x.png\" alt=\"a&lt;b\" /></p>")]
    public void ConvertsImages(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("- one<NL>- two", "<ul><li>one</li><li>two</li></ul>")]
    [InlineData("* one<NL>* two", "<ul><li>one</li><li>two</li></ul>")]
    [InlineData("+ one<NL>+ two", "<ul><li>one</li><li>two</li></ul>")]
    [InlineData("1. one<NL>2. two", "<ol><li>one</li><li>two</li></ol>")]
    [InlineData("1) paren ordered<NL>2) still one", "<ol><li>paren ordered</li><li>still one</li></ol>")]
    [InlineData("- a *em* item<NL>- **b**", "<ul><li>a <em>em</em> item</li><li><strong>b</strong></li></ul>")]
    [InlineData("- top<NL>  - nested<NL>  - also nested<NL>- back to top",
        "<ul><li>top\n<ul><li>nested</li><li>also nested</li></ul></li><li>back to top</li></ul>")]
    [InlineData("1. one<NL>   1. inner<NL>2. two", "<ol><li>one\n<ol><li>inner</li></ol></li><li>two</li></ol>")]
    [InlineData("- wrapped item<NL>  continues here", "<ul><li>wrapped item continues here</li></ul>")]
    public void ConvertsLists(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("> quoted", "<blockquote>\n<p>quoted</p>\n</blockquote>")]
    [InlineData("> ## Headed inside", "<blockquote>\n<h2>Headed inside</h2>\n</blockquote>")]
    [InlineData("> > nested quote", "<blockquote>\n<blockquote>\n<p>nested quote</p>\n</blockquote>\n</blockquote>")]
    [InlineData("> one<NL>> two", "<blockquote>\n<p>one\ntwo</p>\n</blockquote>")]
    [InlineData("> - a<NL>> - b", "<blockquote>\n<ul><li>a</li><li>b</li></ul>\n</blockquote>")]
    [InlineData("> `code` with <b>html</b>", "<blockquote>\n<p><code>code</code> with &lt;b&gt;html&lt;/b&gt;</p>\n</blockquote>")]
    public void ConvertsBlockquotes(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Theory]
    [InlineData("---", "<hr />")]
    [InlineData("***", "<hr />")]
    [InlineData("___", "<hr />")]
    [InlineData("* * *", "<hr />")]
    [InlineData("-  -  -", "<hr />")]
    [InlineData("-- not a rule", "<p>-- not a rule</p>")]
    [InlineData("----", "<hr />")]
    public void ConvertsHorizontalRules(string markdown, string expected) => Assert.Equal(expected, Md(markdown));

    [Fact]
    public void ConvertsADocumentMixingEveryConstruct()
    {
        const string markdown = """
            # Release notes

            Intro paragraph with **bold** and a [link](/docs).

            - first
            - second
              - nested

            1. step one
            2. step two

            > Quoted advice.

            ```csharp
            Console.WriteLine("hi");
            ```

            ---

            ![Diagram](/img/diagram.png)
            """;

        const string expected = """
            <h1>Release notes</h1>
            <p>Intro paragraph with <strong>bold</strong> and a <a href="/docs">link</a>.</p>
            <ul><li>first</li><li>second
            <ul><li>nested</li></ul></li></ul>
            <ol><li>step one</li><li>step two</li></ol>
            <blockquote>
            <p>Quoted advice.</p>
            </blockquote>
            <pre><code class="language-csharp">Console.WriteLine(&quot;hi&quot;);</code></pre>
            <hr />
            <p><img src="/img/diagram.png" alt="Diagram" /></p>
            """;

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown));
    }
}
