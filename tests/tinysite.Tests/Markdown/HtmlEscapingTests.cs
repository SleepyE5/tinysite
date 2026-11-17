using Tinysite.Markdown;

namespace Tinysite.Tests;

/// <summary>
/// HTML escaping is the security boundary of a site generator: the input is an author's
/// Markdown file and the output is HTML served under someone's origin. These tests pin the
/// five characters that matter, then try to get markup past them.
/// </summary>
public sealed class HtmlEscapingTests
{
    [Theory]
    [InlineData("<", "&lt;")]
    [InlineData(">", "&gt;")]
    [InlineData("&", "&amp;")]
    [InlineData("\"", "&quot;")]
    [InlineData("'", "&#39;")]
    [InlineData("<a href=\"x\">", "&lt;a href=&quot;x&quot;&gt;")]
    [InlineData("it's a \"test\" & more", "it&#39;s a &quot;test&quot; &amp; more")]
    [InlineData("emoji 🚀 stays", "emoji 🚀 stays")]
    [InlineData("", "")]
    [InlineData("no special characters", "no special characters")]
    public void EscapeCoversEveryHtmlSignificantCharacter(string raw, string expected) =>
        Assert.Equal(expected, HtmlEscape.Escape(raw));

    [Fact]
    public void EscapeIsAppliedToTextNotJustAttributes()
    {
        // Not escaping quotes in element text is harmless; escaping them is what makes this
        // one function safe to reuse in both contexts, and this asserts the consequence.
        Assert.Equal("<p>a &quot;quoted&quot; &#39;word&#39;</p>", MarkdownConverter.ToHtml("a \"quoted\" 'word'"));
    }

    [Fact]
    public void AmpersandIsEscapedOnceNotDoubly()
    {
        // The dialect has no entity pass-through, so "&amp;" in the source is data, not markup.
        Assert.Equal("<p>&amp;amp;</p>", MarkdownConverter.ToHtml("&amp;"));
    }

    [Theory]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    public void IsDangerousUrlRefusesExecutableAndLocalSchemes(string url) =>
        Assert.True(HtmlEscape.IsDangerousUrl(url));

    [Theory]
    [InlineData("/relative/path")]
    [InlineData("sibling.html")]
    [InlineData("#fragment")]
    [InlineData("/a#b:c")]
    [InlineData("https://example.com/x")]
    [InlineData("http://example.com")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("tel:+15551234567")]
    [InlineData("")]
    public void IsDangerousUrlAllowsOrdinaryNavigation(string url) =>
        Assert.False(HtmlEscape.IsDangerousUrl(url));

    [Fact]
    public void EscapeUrlEncodesCharactersThatWouldBreakOutOfTheAttribute() =>
        Assert.Equal("/a%22b%20%3Cd%3Ee", HtmlEscape.EscapeUrl("/a\"b <d>e"));

    [Fact]
    public void EscapeUrlLeavesAnAlreadyEncodedQueryAlone() =>
        Assert.Equal("?next=%20", HtmlEscape.EscapeUrl("?next=%20"));

    [Fact]
    public void ScriptTagInBodyTextCannotReachOutputRaw()
    {
        string html = MarkdownConverter.ToHtml("<script>alert('xss')</script>");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptTagInsideCodeSpanIsEscapedToo()
    {
        string html = MarkdownConverter.ToHtml("`<script>alert(1)</script>`");

        Assert.Equal("<p><code>&lt;script&gt;alert(1)&lt;/script&gt;</code></p>", html);
    }

    [Fact]
    public void ScriptTagInsideFencedBlockIsEscapedAndNotParsed()
    {
        string html = MarkdownConverter.ToHtml("```\n<script>alert(1)</script>\n<img src=x onerror=alert(1)>\n```");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptFragmentInLinkUrlIsEscaped()
    {
        string html = MarkdownConverter.ToHtml("[t](</script><script>evil)</script>)");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteInLinkDestinationCannotOpenANewAttribute()
    {
        string html = MarkdownConverter.ToHtml("[t](/x\" onclick=\"evil())");

        Assert.DoesNotContain("\" onclick=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href=\"/x\"", html, StringComparison.Ordinal);
        Assert.Contains("onclick", html, StringComparison.Ordinal);
    }

    [Fact]
    public void JavascriptUrlLinkLosesItsHrefButKeepsItsText()
    {
        string html = MarkdownConverter.ToHtml("[click me](javascript:alert(1))");

        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("click me", html, StringComparison.Ordinal);
    }

    [Fact]
    public void DataUrlLinkIsRefused()
    {
        string html = MarkdownConverter.ToHtml("[x](data:text/html,<script>alert(1)</script>)");

        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data:text/html", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JavascriptUrlImageIsDroppedEntirely()
    {
        string html = MarkdownConverter.ToHtml("![x](javascript:alert(1))");

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageSourceWithQuoteCannotEscapeTheAttribute()
    {
        string html = MarkdownConverter.ToHtml("![a](/x\" onerror=\"alert(1))");

        Assert.Equal(1, html.Split("<img").Length - 1);
        Assert.DoesNotContain(" onerror=\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AltTextWithAngleBracketsIsEscaped() =>
        Assert.Equal("<p><img src=\"/x.png\" alt=\"&lt;b&gt;\" /></p>", MarkdownConverter.ToHtml("![<b>](/x.png)"));

    [Theory]
    [InlineData("# <script>alert(1)</script>", "<h1>&lt;script&gt;alert(1)&lt;/script&gt;</h1>")]
    [InlineData("> <script>x</script>", "<blockquote>\n<p>&lt;script&gt;x&lt;/script&gt;</p>\n</blockquote>")]
    [InlineData("- <script>x</script>", "<ul><li>&lt;script&gt;x&lt;/script&gt;</li></ul>")]
    [InlineData("[<b>](/x)", "<p><a href=\"/x\">&lt;b&gt;</a></p>")]
    [InlineData("<script>x</script> after", "<p>&lt;script&gt;x&lt;/script&gt; after</p>")]
    public void EveryBlockContextEscapesMarkup(string markdown, string expected) =>
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown));

    [Fact]
    public void EscapeHandlesNull() => Assert.Equal(string.Empty, HtmlEscape.Escape(null));
}
