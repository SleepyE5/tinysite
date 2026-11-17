using Tinysite.Cli;
using Tinysite.Content;

namespace Tinysite.Tests.Cli;

/// <summary>The command line: what it accepts, what it refuses, and what it says when it refuses.</summary>
public sealed class CommandLineTests
{
    private static CommandLine.BuildCommand Build(params string[] args) =>
        CommandLine.Parse(args).Build ?? throw new InvalidOperationException("expected a build command");

    [Fact]
    public void Parse_DefaultsToBuildingTheCurrentDirectoryIntoUnderscoreSite()
    {
        CommandLine.BuildCommand build = Build("build");

        Assert.Equal(".", build.Source);
        Assert.Equal("_site", build.Output);
        Assert.Equal(CommandLine.DefaultBaseUrl, build.BaseUrl);
        Assert.False(build.IncludeDrafts);
        Assert.False(build.CleanFirst);
        Assert.False(build.Quiet);
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--output")]
    [InlineData("--base-url")]
    public void Parse_RejectsAFlagWithNothingAfterIt(string flag)
    {
        CommandLineException usage = Assert.Throws<CommandLineException>(() => CommandLine.Parse(["build", flag]));
        Assert.Contains(flag, usage.Message, StringComparison.Ordinal);
        Assert.Contains("needs a value", usage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAFlagWhoseValueIsAnotherFlag()
    {
        // `--output --quiet` would otherwise publish to a directory called "--quiet".
        CommandLineException usage = Assert.Throws<CommandLineException>(
            () => CommandLine.Parse(["build", "--output", "--quiet"]));

        Assert.Contains("needs a value", usage.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--source-dir")]
    [InlineData("-s")]
    [InlineData("--draft")]
    public void Parse_RejectsOptionsThatDoNotExist(string flag)
    {
        CommandLineException usage = Assert.Throws<CommandLineException>(() => CommandLine.Parse(["build", flag]));

        Assert.Contains(flag, usage.Message, StringComparison.Ordinal);
        Assert.NotNull(usage.Hint);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("builds")]
    [InlineData("")]
    public void Parse_RejectsUnknownCommands(string command)
    {
        Assert.Throws<CommandLineException>(() => CommandLine.Parse([command]));
    }

    [Fact]
    public void Parse_WithNoArgumentsAtAllAsksForACommandRatherThanGuessing()
    {
        CommandLineException usage = Assert.Throws<CommandLineException>(() => CommandLine.Parse([]));

        Assert.Contains("needs a command", usage.Message, StringComparison.Ordinal);
        Assert.Contains("--help", usage.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_HelpAndVersionProduceInformationAndNoWork()
    {
        Assert.Contains("usage:", CommandLine.Parse(["--help"]).Information, StringComparison.Ordinal);
        Assert.Contains("usage:", CommandLine.Parse(["build", "--help"]).Information, StringComparison.Ordinal);
        Assert.Equal("tinysite " + CommandLine.Version, CommandLine.Parse(["--version"]).Information);
        Assert.Null(CommandLine.Parse(["-h"]).Build);
    }

    [Theory]
    [InlineData("https://site.example")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://user.github.io/project-name/")]
    public void Parse_AcceptsAnAbsoluteHttpBaseUrl(string url) =>
        Assert.Equal(url, Build("build", "--base-url", url).BaseUrl);

    [Theory]
    [InlineData("/blog")]
    [InlineData("site.example")]
    [InlineData("ftp://site.example")]
    [InlineData("")]
    public void Parse_RefusesABaseUrlThatWouldBreakTheFeed(string url)
    {
        // Every <link> in an RSS item and every canonical URL is built from this. A relative one
        // produces a feed that readers reject in whatever way they each find amusing.
        CommandLineException usage = Assert.Throws<CommandLineException>(
            () => CommandLine.Parse(["build", "--base-url", url]));

        Assert.Contains("absolute http(s) URL", usage.Message, StringComparison.Ordinal);
        Assert.Contains("--base-url", usage.Hint ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AllowsTheConventionalSiteDirectoryBesideContent()
    {
        using TestTree source = TestTree.ScaffoldSource();

        CommandLine.BuildCommand build = Build("build", "--source", source.Root, "--output", Path.Combine(source.Root, "_site"));

        Assert.Equal(Path.Combine(source.Root, "_site"), build.Output);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("content/posts")]
    [InlineData("static")]
    public void Parse_RefusesAnOutputDirectoryTheReaderWouldReadBack(string inside)
    {
        using TestTree source = TestTree.ScaffoldSource();

        CommandLineException usage = Assert.Throws<CommandLineException>(() => CommandLine.Parse(
            ["build", "--source", source.Root, "--output", Path.Combine(source.Root, inside.Replace('/', Path.DirectorySeparatorChar))]));

        Assert.Contains("reads as source", usage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RefusesPublishingOverItsOwnSource()
    {
        using TestTree source = TestTree.ScaffoldSource();

        CommandLineException usage = Assert.Throws<CommandLineException>(
            () => CommandLine.Parse(["build", "--source", source.Root, "--output", source.Root]));

        Assert.Contains("source directory itself", usage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NewTakesExactlyOneDirectory()
    {
        Assert.Equal("/tmp/whatever", CommandLine.Parse(["new", "/tmp/whatever"]).NewDirectory);
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(["new"]));
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(["new", "/tmp/a", "/tmp/b"]));
    }

    [Theory]
    [InlineData("https://site.example", false)]
    [InlineData("https://site.example/", false)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://localhost:8080/", true)]
    public void DefaultBaseUrlIsRecognisedWhateverTheSlashLooksLike(string url, bool isDefault) =>
        Assert.Equal(isDefault, CommandLine.IsDefaultBaseUrl(url));

    [Fact]
    public void Parse_ResolvesADotDotInTheOutputPathBeforeJudgingIt()
    {
        using TestTree source = TestTree.ScaffoldSource();

        // `content/../_site` is not inside content/, and a comparison done on the string would
        // have refused it. The check runs on the resolved path for exactly this reason.
        CommandLine.BuildCommand build = Build("build", "--source", source.Root,
            "--output", Path.Combine(source.Root, "content", "..", "_site"));
        Assert.EndsWith("_site", build.Output, StringComparison.Ordinal);

        // and the same normalisation catches the sneaky form of the mistake
        Assert.Throws<CommandLineException>(() => CommandLine.Parse(["build", "--source", source.Root,
            "--output", Path.Combine(source.Root, "content", "sub", "..")]));
    }
}

/// <summary>The process-facing half: exit codes and what lands on stdout and stderr.</summary>
public sealed class RunnerTests
{
    private sealed record Capture(int Code, string Out, string Error)
    {
        public IReadOnlyList<string> OutLines => Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public IReadOnlyList<string> ErrorLines => Error.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Capture Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        CommandLine.Command command = CommandLine.Parse(args);

        int code = command.NewDirectory is not null
            ? Scaffold.Run(command.NewDirectory, output, error)
            : BuildRunner.Run(command.Build!, output, error);

        return new Capture(code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Build_SucceedsOnTheScaffoldAndListsWhatItWrote()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        Capture run = Run("build", "--source", source.Root, "--output", output.Root, "--base-url", "https://site.example");

        Assert.Equal(0, run.Code);
        Assert.Contains(run.OutLines, line => line.StartsWith("tinysite: 2 documents", StringComparison.Ordinal));
        Assert.Contains("  index.html", run.Out);
        Assert.NotEmpty(output.Tree());
        Assert.Empty(run.Error);
    }

    [Fact]
    public void Build_WithQuietPrintsOneLine()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        Capture run = Run("build", "--source", source.Root, "--output", output.Root, "--base-url", "https://site.example", "--quiet");

        Assert.Equal(0, run.Code);
        Assert.Single(run.OutLines);
    }

    [Fact]
    public void Build_WarnsLoudlyWhenItWouldPublishALocalhostFeed()
    {
        using TestTree source = TestTree.ScaffoldSource();
        using TestTree output = TestTree.Create("out");

        Capture run = Run("build", "--source", source.Root, "--output", output.Root);

        Assert.Equal(0, run.Code);
        Assert.Contains(run.ErrorLines, line => line.Contains("--base-url was not given", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ReportsEveryBrokenDocumentAndExitsOne()
    {
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/one.md", "---\nkey without a colon\n---\nbody\n");
        _ = source.Write("content/two.md", "---\ndate: not a date\n---\nbody\n");
        using TestTree output = TestTree.Create("out");

        Capture run = Run("build", "--source", source.Root, "--output", output.Root, "--base-url", "https://site.example");

        Assert.Equal(1, run.Code);
        Assert.Contains(run.ErrorLines, line => line.Contains("one.md", StringComparison.Ordinal));
        Assert.Contains(run.ErrorLines, line => line.Contains("two.md", StringComparison.Ordinal));
        Assert.Contains(run.ErrorLines, line => line.Contains("nothing was published", StringComparison.Ordinal));
        Assert.Contains(run.ErrorLines, line => line.Contains("2 problems in the source", StringComparison.Ordinal));
        Assert.Empty(output.Tree());
    }

    [Fact]
    public void Build_CountsOneProblemInSingular()
    {
        // The summary line used to print "1 problem(s)", which is the kind of
        // string a tool says when nobody has read it out loud. One broken
        // document has to read as one problem, and two as two problems.
        using TestTree source = TestTree.ScaffoldSource();
        _ = source.Write("content/one.md", "---\nkey without a colon\n---\nbody\n");
        using TestTree output = TestTree.Create("out");

        Capture run = Run("build", "--source", source.Root, "--output", output.Root, "--base-url", "https://site.example");

        Assert.Equal(1, run.Code);
        Assert.Contains(run.ErrorLines, line => line.Contains("1 problem in the source", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Error, "(s)");
        Assert.DoesNotContain(run.Error, "1 problems");
    }

    [Fact]
    public void Build_ExplainsASourceDirectoryThatIsNotASite()
    {
        using TestTree empty = TestTree.Create("not-a-site");
        using TestTree output = TestTree.Create("out");

        Capture run = Run("build", "--source", empty.Root, "--output", output.Root, "--base-url", "https://site.example");

        Assert.Equal(1, run.Code);
        Assert.Contains("content/", run.Error);
    }

    [Fact]
    public void Scaffold_WritesATreeThatBuildsWithoutFurtherEdits()
    {
        using TestTree parent = TestTree.Create("scaffold");
        string target = Path.Combine(parent.Root, "my-site");

        Capture created = Run("new", target);
        Assert.Equal(0, created.Code);

        Capture built = Run("build", "--source", target, "--output", Path.Combine(target, "_site"), "--base-url", "https://site.example");

        Assert.Equal(0, built.Code);
        Assert.Contains("<!doctype html>", File.ReadAllText(Path.Combine(target, "_site", "index.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_RefusesToWriteIntoADirectoryThatHasThingsInIt()
    {
        using TestTree occupied = TestTree.Create("occupied");
        _ = occupied.Write("index.html", "<p>mine</p>");

        Capture run = Run("new", occupied.Root);

        Assert.Equal(1, run.Code);
        Assert.Contains("not empty", run.Error);
        Assert.Contains("mine", occupied.Read("index.html"), StringComparison.Ordinal);
    }

    [Fact]
    public void Scaffold_RunTwiceIsRefusedButTheFirstTreeSurvives()
    {
        using TestTree parent = TestTree.Create("scaffold-twice");
        string target = Path.Combine(parent.Root, "site");

        Assert.Equal(0, Run("new", target).Code);
        string layout = File.ReadAllText(Path.Combine(target, SourceReader.LayoutFileName));

        Assert.Equal(1, Run("new", target).Code);
        Assert.Equal(layout, File.ReadAllText(Path.Combine(target, SourceReader.LayoutFileName)));
    }
}
