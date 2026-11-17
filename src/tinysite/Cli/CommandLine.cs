using Tinysite.Content;

namespace Tinysite.Cli;

/// <summary>A command line that did not make it to a build, with the reason and a hint.</summary>
public sealed class CommandLineException(string message, string? hint = null) : Exception(message)
{
    /// <summary>What to type instead, shown under the error. Null when the message says it all.</summary>
    public string? Hint { get; } = hint;
}

/// <summary>
/// Parses <c>tinysite</c> invocations.
/// </summary>
/// <remarks>
/// Kept separate from <c>Program</c> so the rules are testable without a process: exit codes,
/// what a missing value means, and which combinations are contradictory. Every failure carries a
/// hint, because a build tool that says "invalid argument" at eleven o'clock is a tool that gets
/// replaced.
/// </remarks>
public static class CommandLine
{
    /// <summary>Printed by <c>--help</c> and by any parse failure.</summary>
    public const string Help = """
        tinysite - a static site generator you can read

        usage:
          tinysite build [--source DIR] [--output DIR] [--base-url URL]
                         [--drafts] [--clean] [--quiet]
          tinysite new DIR                 write a source tree to start from
          tinysite --help                  this text
          tinysite --version               the version

        build reads layout.html, content/**.md and static/** from --source (default ".")
        and writes index.html, one page per document, the archive, a page per tag and
        feed.xml to --output (default "_site").

        --base-url is used for canonical links and the feed. The default is a localhost
        preview address and is reported in the build log, because a feed published with
        it in place points at nobody's machine but the author's.

        exit status: 0 built, 1 the source is broken, 2 the command line was.
        """;

    /// <summary>The base URL used when none was given, which is a preview address, not a real one.</summary>
    public const string DefaultBaseUrl = "http://localhost:8080";

    /// <summary>True when the base URL is the default and therefore worth warning about.</summary>
    public static bool IsDefaultBaseUrl(string baseUrl) =>
        string.Equals(baseUrl.TrimEnd('/'), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);

    /// <summary>What <c>tinysite build</c> was asked to do.</summary>
    public sealed record BuildCommand(
        string Source,
        string Output,
        string BaseUrl,
        bool IncludeDrafts,
        bool CleanFirst,
        bool Quiet);

    /// <summary>Mutable accumulator for the flags of one build; the record above is what callers see.</summary>
    private sealed class BuildDraft
    {
        internal string Source { get; set; } = ".";

        internal string Output { get; set; } = "_site";

        internal string BaseUrl { get; set; } = DefaultBaseUrl;

        internal bool IncludeDrafts { get; set; }

        internal bool CleanFirst { get; set; }

        internal bool Quiet { get; set; }

        internal BuildCommand ToCommand() =>
            new BuildCommand(Source, Output, BaseUrl, IncludeDrafts, CleanFirst, Quiet);
    }

    /// <summary>The parsed command. One of these two, never both.</summary>
    public sealed record Command(BuildCommand? Build, string? NewDirectory)
    {
        /// <summary>Set when the invocation only asked for help or the version.</summary>
        public string? Information { get; init; }
    }

    /// <summary>
    /// Parses arguments. Throws <see cref="CommandLineException"/> rather than returning a code,
    /// so no caller can forget to stop after a failure.
    /// </summary>
    public static Command Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new CommandLineException("tinysite needs a command.", "Try `tinysite --help`.");
        }

        string command = args[0];

        if (command is "--help" or "-h" or "help")
        {
            return new Command(null, null) { Information = Help };
        }

        if (command is "--version" or "-V" or "version")
        {
            return new Command(null, null) { Information = "tinysite " + Version };
        }

        if (command == "new")
        {
            string target = RequireValue(args, 1, flag: "new <directory>", hint: "tinysite new ./my-site");
            RejectTrailing(args, 2);
            return new Command(null, target);
        }

        if (command != "build")
        {
            throw new CommandLineException($"unknown command '{command}'.", "Commands: build, new, --help, --version.");
        }

        var build = new BuildDraft();

        for (int index = 1; index < args.Length; index++)
        {
            string flag = args[index];
            switch (flag)
            {
                case "--source": build.Source = RequireValue(args, ++index, flag); break;
                case "--output": build.Output = RequireValue(args, ++index, flag); break;
                case "--base-url": build.BaseUrl = RequireValue(args, ++index, flag); break;
                case "--drafts": build.IncludeDrafts = true; break;
                case "--clean": build.CleanFirst = true; break;
                case "--quiet": build.Quiet = true; break;
                case "--help" or "-h": return new Command(null, null) { Information = Help };
                default:
                    throw new CommandLineException(
                        $"unknown option '{flag}'.",
                        "Run `tinysite build --help` for the options this command takes.");
            }
        }

        string? unsafeHere = UnsafeOutputLocation(build.Source, build.Output);
        if (unsafeHere is not null)
        {
            // `--source . --output _site` is the normal layout and is allowed: the reader only
            // looks at content/ and static/, so output beside them is never read back. Output
            // *inside* one of those directories is different -- it puts generated files where
            // the next build looks for sources.
            throw new CommandLineException(
                $"'{build.Output}' would be written inside {unsafeHere}, which tinysite reads as source.",
                "Put the output beside content/, not under it: --output _site is the convention.");
        }

        ValidateBaseUrl(build.BaseUrl);

        return new Command(build.ToCommand(), null);
    }

    /// <summary>
    /// The name of a source directory the output would land inside, or null when the pair is safe.
    /// </summary>
    internal static string? UnsafeOutputLocation(string source, string output)
    {
        string fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        string fullOutput = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(fullSource, fullOutput, StringComparison.Ordinal))
        {
            return "the source directory itself";
        }

        foreach (string name in new[] { SourceReader.ContentDirectoryName, SourceReader.StaticDirectoryName })
        {
            string inside = fullSource + Path.DirectorySeparatorChar + name + Path.DirectorySeparatorChar;
            if (fullOutput.StartsWith(inside, StringComparison.Ordinal)
                || string.Equals(fullOutput, inside.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
            {
                return $"'{name}/'";
            }
        }

        return null;
    }

    /// <summary>
    /// The base URL ends up inside an RSS feed, where a relative or schemeless value produces a
    /// feed that every reader rejects differently. Better to fail now than to ship a feed that
    /// looks fine in a browser and does nothing in a reader.
    /// </summary>
    internal static void ValidateBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new CommandLineException(
                $"--base-url must be an absolute http(s) URL, got '{baseUrl}'.",
                "For example: --base-url https://example.github.io/site-name");
        }
    }

    private static string RequireValue(string[] args, int index, string flag, string? hint = null)
    {
        if (index >= args.Length || args[index].StartsWith('-'))
        {
            throw new CommandLineException($"{flag} needs a value.", hint ?? $"Run `tinysite --help`.");
        }

        return args[index];
    }

    private static void RejectTrailing(string[] args, int index)
    {
        if (index < args.Length)
        {
            throw new CommandLineException($"unexpected argument '{args[index]}'.", "tinysite new takes one directory.");
        }
    }

    /// <summary>Version reported by <c>--version</c>, kept next to the help text it appears in.</summary>
    public const string Version = "0.1.0";
}
