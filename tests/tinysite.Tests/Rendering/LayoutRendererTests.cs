using Tinysite.Rendering;

namespace Tinysite.Tests;

public sealed class LayoutRendererTests
{
    private const string Layout = """
        <!doctype html>
        <html lang="en">
        <head><title>{{title}}</title><meta name="x" content="{{author}}"></head>
        <body>{{{content}}}</body>
        </html>
        """;

    private static PlaceholderTable Sample() =>
        new PlaceholderTable()
            .Add("title", "A title")
            .Add("author", "Ada")
            .Add("content", "<p>Body <em>markup</em></p>");

    [Fact]
    public void Render_SubstitutesPlaceholdersAndTheContentSlot()
    {
        string html = LayoutRenderer.Render(Layout, Sample()).Html;

        Assert.Contains("<title>A title</title>", html, StringComparison.Ordinal);
        Assert.Contains("content=\"Ada\"", html, StringComparison.Ordinal);
        Assert.Contains("<body><p>Body <em>markup</em></p></body>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EscapesOrdinaryPlaceholders()
    {
        PlaceholderTable table = new PlaceholderTable().Add("title", "Q3 & <b>now</b> \"quoted\" 'single'");

        string html = LayoutRenderer.Render("<title>{{title}}</title>", table).Html;

        Assert.Equal("<title>Q3 &amp; &lt;b&gt;now&lt;/b&gt; &quot;quoted&quot; &#39;single&#39;</title>", html);
    }

    [Fact]
    public void Render_KeepsScriptInFrontMatterValueFromBecomingLiveMarkup()
    {
        // The XSS case that matters for a site generator: a hostile or careless front matter
        // value must arrive as text.
        PlaceholderTable table = Sample().Add("title", "</title><script>alert(document.domain)</script>");

        string html = LayoutRenderer.Render(Layout, table).Html;

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(document.domain)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<body><p>Body <em>markup</em></p></body>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ContentIsNeverEscapedBecauseItIsGeneratedHtml()
    {
        PlaceholderTable table = new PlaceholderTable().Add("content", "<p>Real &lt;em&gt; markup</p>");

        string html = LayoutRenderer.Render("{{content}}", table).Html;

        // The converter already escaped what needed escaping; the slot must not escape twice.
        Assert.Equal("<p>Real &lt;em&gt; markup</p>", html);
    }

    [Theory]
    [InlineData("{{ triple }}")]
    [InlineData("{{triple}}")]
    [InlineData("{{{ triple }}}")]
    [InlineData("{{{triple}}}")]
    public void Render_AcceptsPaddingInsideBraces(string template) =>
        Assert.Equal("raw", LayoutRenderer.Render(template, new PlaceholderTable().Add("triple", "raw")).Html);

    [Theory]
    [InlineData("{{a-b}}", "dash")]
    [InlineData("{{a_b}}", "underscore")]
    [InlineData("{{a.b}}", "dotted")]
    [InlineData("{{v2}}", "digit")]
    public void Render_AllowsTheDocumentedNameCharacters(string template, string expected)
    {
        string name = template.Trim('{', '}');
        Assert.Equal(expected, LayoutRenderer.Render(template, new PlaceholderTable().Add(name, expected)).Html);
    }

    [Fact]
    public void Render_IsCaseInsensitiveForNames() =>
        Assert.Equal("Ada", LayoutRenderer.Render("{{TITLE}}", new PlaceholderTable().Add("title", "Ada")).Html);

    [Fact]
    public void Render_ReportsUnknownNames_AndEmitsNothingForThem()
    {
        LayoutRenderResult result = LayoutRenderer.Render("<p>{{title}} by {{editor}}</p>", Sample());

        Assert.Equal("<p>A title by </p>", result.Html);
        Assert.Equal(["editor"], result.UnknownPlaceholders);
    }

    [Fact]
    public void Render_ReportsUnknownNamesOnlyOnceEach()
    {
        LayoutRenderResult result = LayoutRenderer.Render("{{a}}{{b}}{{a}}", new PlaceholderTable().Add("b", "x"));

        Assert.Equal("x", result.Html);
        Assert.Equal(["a"], result.UnknownPlaceholders);
    }

    [Theory]
    [InlineData("{{}}")]
    [InlineData("{{ }}")]
    [InlineData("{{ name")]
    [InlineData("{{name}")]
    [InlineData("{name}")]
    [InlineData("{{na me}}")]
    [InlineData("{{ }}{{ }}")]
    public void Render_CopiesAnythingThatIsNotAWellFormedPlaceholder(string template)
    {
        var table = new PlaceholderTable().Add("name", "VALUE");

        string html = LayoutRenderer.Render(template, table).Html;

        Assert.DoesNotContain("VALUE", html, StringComparison.Ordinal);
        Assert.Equal(template, html);
    }

    [Fact]
    public void Render_LeavesCssBraceBlocksAlone()
    {
        const string template = "<style>.card { color: red }{{content}}</style>";

        string html = LayoutRenderer.Render(template, new PlaceholderTable().Add("content", "<p>x</p>")).Html;

        Assert.Equal("<style>.card { color: red }<p>x</p></style>", html);
    }

    [Fact]
    public void Render_DoubleBraceInCssSurvivesWithoutBeingATemplateTag()
    {
        const string css = "@media (min-width: 40rem) { .a { color: red } }";

        Assert.Equal(css, LayoutRenderer.Render(css, Sample()).Html);
    }

    [Fact]
    public void Render_KeepsLiteralBracesThatOnlyLookLikeAPlaceholder()
    {
        // "{{{" followed by a non-name and "}}}" in one document: neither is a placeholder.
        string html = LayoutRenderer.Render("{{{ }}}", new PlaceholderTable().Add("x", "y")).Html;

        Assert.Equal("{{{ }}}", html);
    }

    [Fact]
    public void Render_TreatsMissingValueAsEmptyEvenWhenContent()
    {
        Assert.Equal("[]", LayoutRenderer.Render("[{{content}}]", new PlaceholderTable()).Html);
    }

    [Fact]
    public void Render_AcceptsNullValuesInTable()
    {
        var table = new PlaceholderTable().Add("x", null);

        Assert.Equal("", LayoutRenderer.Render("{{x}}", table).Html);
    }

    [Fact]
    public void Render_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutRenderer.Render(null!, Sample()).Html);
        Assert.Throws<ArgumentNullException>(() => LayoutRenderer.Render("x", null!).Html);
    }

    [Fact]
    public void PlaceholderTable_ListsNamesInOrdinalOrder()
    {
        IReadOnlyList<string> names = new PlaceholderTable().Add("b", "1").Add("A", "2").Add("c", "3").Names;

        Assert.Equal(["A", "b", "c"], names);
    }

    [Fact]
    public void PlaceholderTable_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new PlaceholderTable().Add(string.Empty, "x"));
    }

    [Fact]
    public void Render_HandlesRepeatedPlaceholderAndLargeTemplate()
    {
        string template = string.Concat(Enumerable.Repeat("{{title}}|", 2000));

        LayoutRenderResult result = LayoutRenderer.Render(template, new PlaceholderTable().Add("title", "T"));

        Assert.Equal(2000, result.Html.Split('T').Length - 1);
        Assert.Empty(result.UnknownPlaceholders);
    }
}
