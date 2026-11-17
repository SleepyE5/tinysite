using Tinysite.Metadata;

namespace Tinysite.Tests;

public sealed class FrontMatterParserTests
{
    /// <summary>
    /// Front matter is line-oriented, so most cases here are small documents. Attribute
    /// arguments cannot span lines, so they are written with a &lt;NL&gt; marker and expanded here.
    /// </summary>
    private static FrontMatter ParseLines(string markedText) =>
        FrontMatterParser.Parse(markedText.Replace("<NL>", "\n")).FrontMatter;

    [Fact]
    public void Parse_ReturnsWholeTextAsBody_WhenThereIsNoFrontMatterBlock()
    {
        const string text = """
            # Just a heading

            Some prose.
            """;

        FrontMatterDocument document = FrontMatterParser.Parse(text);

        Assert.Same(FrontMatter.Empty, document.FrontMatter);
        Assert.Equal("# Just a heading\n\nSome prose.", document.Body);
    }

    [Fact]
    public void Parse_ReturnsEmptyBody_WhenTheDocumentIsOnlyFrontMatter()
    {
        const string text = """
            ---
            title: Nothing else
            ---
            """;

        FrontMatterDocument document = FrontMatterParser.Parse(text);

        Assert.Equal(string.Empty, document.Body);
        Assert.Equal("Nothing else", document.FrontMatter.Title);
    }

    [Fact]
    public void Parse_ReadsEveryKnownKey()
    {
        const string text = """
            ---
            title: Hello, world
            date: 2026-03-01
            tags: [dotnet, markdown]
            draft: true
            ---
            Body text.
            """;

        FrontMatterDocument document = FrontMatterParser.Parse(text);

        Assert.Equal("Hello, world", document.FrontMatter.Title);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), document.FrontMatter.PublishedAt);
        Assert.Equal(new DateOnly(2026, 3, 1), document.FrontMatter.Date);
        Assert.Equal(["dotnet", "markdown"], document.FrontMatter.Tags);
        Assert.True(document.FrontMatter.Draft);
        Assert.Equal("2026-03-01", document.FrontMatter.FormattedDate);
        Assert.Equal("Body text.", document.Body);
    }

    [Fact]
    public void Parse_LeavesDefaults_WhenOnlyTitleIsGiven()
    {
        FrontMatter frontMatter = ParseLines("---<NL>title: X<NL>---<NL>");

        Assert.False(frontMatter.Draft);
        Assert.Empty(frontMatter.Tags);
        Assert.Null(frontMatter.PublishedAt);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("Off", false)]
    public void Parse_AcceptsCommonBooleanSpellingsForDraft(string raw, bool expected)
    {
        Assert.Equal(expected, ParseLines($"---<NL>draft: {raw}<NL>---<NL>").Draft);
    }

    [Theory]
    [InlineData("2026-03-01", 2026, 3, 1, 0, 0)]
    [InlineData("2026-03-01T14:30:00Z", 2026, 3, 1, 14, 30)]
    [InlineData("2026-03-01 14:30", 2026, 3, 1, 14, 30)]
    public void Parse_ReadsDateShapes(string raw, int year, int month, int day, int hour, int minute)
    {
        DateTimeOffset? publishedAt = ParseLines($"---<NL>date: {raw}<NL>---<NL>").PublishedAt;

        Assert.NotNull(publishedAt);
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, 0, publishedAt!.Value.Offset), publishedAt);
    }

    [Fact]
    public void Parse_KeepsExplicitUtcOffset_AndDoesNotShiftTheCalendarDate()
    {
        FrontMatter frontMatter = ParseLines("---<NL>date: 2026-03-01T23:30:00+09:00<NL>---<NL>");

        Assert.Equal(new TimeSpan(9, 0, 0), frontMatter.PublishedAt!.Value.Offset);
        Assert.Equal(new DateOnly(2026, 3, 1), frontMatter.Date);
    }

    [Theory]
    [InlineData("tags: dotnet, markdown")]
    [InlineData("tags: [dotnet, markdown]")]
    [InlineData("tags: 'dotnet', 'markdown'")]
    public void Parse_ReadsBothTagShapes(string line)
    {
        Assert.Equal(["dotnet", "markdown"], ParseLines($"---<NL>{line}<NL>---<NL>").Tags);
    }

    [Fact]
    public void Parse_ReadsBlockSequenceTags()
    {
        const string text = """
            ---
            tags:
              - dotnet
              - "markdown"
              - csharp
            ---
            """;

        FrontMatter frontMatter = FrontMatterParser.Parse(text).FrontMatter;

        Assert.Equal(["dotnet", "markdown", "csharp"], frontMatter.Tags);
        Assert.True(frontMatter.TryGetValue("tags", out string joined));
        Assert.Equal("dotnet, markdown, csharp", joined);
    }

    [Fact]
    public void Parse_DeDuplicatesTagsIgnoringCase_KeepingFirstSpelling()
    {
        Assert.Equal(["Dotnet", "rss"], ParseLines("---<NL>tags: [Dotnet, dotnet, DOTNET, rss]<NL>---<NL>").Tags);
    }

    [Fact]
    public void Parse_UnquotesValues_KeepingColonsAndHashesInside()
    {
        const string text = """
            ---
            title: "Q3: roadmap #2"
            author: 'O''Brien'
            slug: roadmap
            ---
            """;

        FrontMatter frontMatter = FrontMatterParser.Parse(text).FrontMatter;

        Assert.Equal("Q3: roadmap #2", frontMatter.Title);
        Assert.Equal("O'Brien", frontMatter.Scalars["author"]);
    }

    [Fact]
    public void Parse_UnescapesDoubleQuotedValues()
    {
        const string text = """
            ---
            title: "say \"hi\""
            ---
            """;

        const string expected = """
            say "hi"
            """;

        Assert.Equal(expected, FrontMatterParser.Parse(text).FrontMatter.Title);
    }

    [Fact]
    public void Parse_ReadsNestedBlockAsFreeFormDictionary()
    {
        const string text = """
            ---
            title: Post
            author:
              name: Ada
              url: https://example.com
            ---
            """;

        FrontMatter frontMatter = FrontMatterParser.Parse(text).FrontMatter;

        Assert.Equal("Ada", frontMatter.Maps["author"]["name"]);
        Assert.True(frontMatter.TryGetValue("author.name", out string value));
        Assert.Equal("Ada", value);
        Assert.False(frontMatter.TryGetValue("author.email", out _));
    }

    [Fact]
    public void Parse_SkipsWholeLineComments()
    {
        Assert.Equal("Kept", ParseLines("---<NL># a comment<NL>title: Kept<NL>---<NL>").Title);
    }

    [Fact]
    public void Parse_HandlesCrlfInput()
    {
        string text = "---\r\ntitle: Windows line endings\r\n---\r\nBody line\r\n";

        FrontMatterDocument document = FrontMatterParser.Parse(text);

        Assert.Equal("Windows line endings", document.FrontMatter.Title);
        Assert.Equal("Body line", document.Body);
        Assert.DoesNotContain('\r', document.Body);
    }

    [Fact]
    public void Parse_ClosesOnEllipsis()
    {
        FrontMatterDocument document = FrontMatterParser.Parse("---<NL>title: T<NL>...<NL>body".Replace("<NL>", "\n"));

        Assert.Equal("T", document.FrontMatter.Title);
        Assert.Equal("body", document.Body);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForKnownKeys()
    {
        FrontMatter frontMatter = ParseLines("---<NL>Title: T<NL>DATE: 2026-01-02<NL>DRAFT: true<NL>---<NL>");

        Assert.Equal("T", frontMatter.Title);
        Assert.Equal(new DateOnly(2026, 1, 2), frontMatter.Date);
        Assert.True(frontMatter.Draft);
    }

    [Theory]
    [InlineData("---<NL>title: T<NL>", "never closed")]
    [InlineData("---<NL>title: A<NL>title: B<NL>---<NL>", "duplicate key 'title'")]
    [InlineData("---<NL>not a pair<NL>---<NL>", "expected 'key: value'")]
    [InlineData("---<NL>bad key: 1<NL>---<NL>", "contains a space")]
    [InlineData("---<NL>date: march 3rd<NL>---<NL>", "must be an ISO date")]
    [InlineData("---<NL>draft: maybe<NL>---<NL>", "must be true or false")]
    [InlineData("---<NL>tags:<NL>  - a<NL>   - b<NL>---<NL>", "inconsistent indentation")]
    [InlineData("---<NL>tags:<NL>  - a<NL>  b: 1<NL>---<NL>", "mixes a sequence item")]
    [InlineData("---<NL>author:<NL>---<NL>", "has no value")]
    [InlineData("---<NL>  indented: 1<NL>---<NL>", "unexpected indentation")]
    [InlineData("---<NL>draft:<NL>  - true<NL>---<NL>", "'draft' needs a true or false value")]
    [InlineData("---<NL>author:<NL>  contact:<NL>    email: x<NL>---<NL>", "only one nesting level")]
    public void Parse_ReportsUsefulErrors(string markedText, string expectedFragment)
    {
        FrontMatterException error = Assert.Throws<FrontMatterException>(
            () => FrontMatterParser.Parse(markedText.Replace("<NL>", "\n")));

        Assert.Contains(expectedFragment, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ErrorCarriesTheLineInsideTheBlock()
    {
        FrontMatterException error = Assert.Throws<FrontMatterException>(
            () => FrontMatterParser.Parse("---\ntitle: A\ntitle: B\n---\n"));

        Assert.Equal(2, error.Line);
    }

    [Fact]
    public void TryGetValue_JoinsSequences_AndReturnsFalseForUnknownKeys()
    {
        FrontMatter frontMatter = ParseLines("---<NL>related: [a, b]<NL>---<NL>");

        Assert.True(frontMatter.TryGetValue("related", out string related));
        Assert.Equal("a, b", related);
        Assert.False(frontMatter.TryGetValue("missing", out string missing));
        Assert.Equal(string.Empty, missing);
        Assert.Equal(["a", "b"], frontMatter.Lists["related"]);
    }

    [Fact]
    public void TryGetValue_RejectsNullKey()
    {
        Assert.Throws<ArgumentNullException>(() => FrontMatter.Empty.TryGetValue(null!, out _));
    }
}
