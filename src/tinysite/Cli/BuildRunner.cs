using System.Diagnostics;
using Tinysite.Content;
using Tinysite.Publishing;

namespace Tinysite.Cli;

/// <summary>
/// Runs one build and turns the result into lines on a terminal.
/// </summary>
/// <remarks>
/// Split from <c>Main</c> so the whole thing is testable in-process: the exit codes and the exact
/// wording of a warning are behaviour a user depends on, and neither belongs behind a
/// <c>Process.Start</c>.
/// </remarks>
public static class BuildRunner
{
    /// <summary>Exit codes: 0 built, 1 the source is broken, 2 the command line was.</summary>
    public const int Ok = 0;

    public const int SourceBroken = 1;

    /// <summary>
    /// Builds and reports. Returns the process exit code rather than throwing, so the caller is
    /// one line.
    /// </summary>
    public static int Run(CommandLine.BuildCommand command, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = new SiteOptions
        {
            SourceDirectory = Path.GetFullPath(command.Source),
            OutputDirectory = Path.GetFullPath(command.Output),
            BaseUrl = command.BaseUrl,
            IncludeDrafts = command.IncludeDrafts,
        };

        try
        {
            if (command.CleanFirst)
            {
                SiteBuilder.Clean(options.OutputDirectory);
            }

            Stopwatch watch = Stopwatch.StartNew();
            BuildResult result = SiteBuilder.Build(options);
            watch.Stop();

            Report(command, options, result, watch.Elapsed, output, error);
            return Ok;
        }
        catch (SiteContentProblemException problem)
        {
            foreach (string message in problem.Messages)
            {
                error.WriteLine($"tinysite: {message}");
            }

            int count = problem.Messages.Count;
            error.WriteLine(
                $"tinysite: {count} {(count == 1 ? "problem" : "problems")} in the source; nothing was published.");
            return SourceBroken;
        }
        catch (SiteContentException failure)
        {
            error.WriteLine($"tinysite: {failure.Message}");
            return SourceBroken;
        }
    }

    private static void Report(
        CommandLine.BuildCommand command,
        SiteOptions options,
        BuildResult result,
        TimeSpan elapsed,
        TextWriter output,
        TextWriter error)
    {
        foreach (string warning in result.Warnings)
        {
            error.WriteLine($"tinysite: warning: {warning}");
        }

        if (CommandLine.IsDefaultBaseUrl(options.BaseUrl))
        {
            error.WriteLine(
                $"tinysite: warning: --base-url was not given; links and the feed use {options.BaseUrl}.");
        }

        // The one line is what a script reads; --quiet removes the list under it, not the verdict.
        output.WriteLine(
            $"tinysite: {result.Documents} documents ({result.Posts} dated), "
            + $"{result.FileCount} files -> {Rel(options.OutputDirectory)} in {elapsed.TotalMilliseconds:F0} ms");

        if (command.Quiet)
        {
            return;
        }

        foreach (string file in result.Files.Order(StringComparer.Ordinal))
        {
            output.WriteLine($"  {file}");
        }

        if (result.DraftsSkipped > 0)
        {
            output.WriteLine($"  ({result.DraftsSkipped} draft(s) held back; --drafts publishes them)");
        }
    }

    private static string Rel(string fullPath)
    {
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
        return relative.Contains("..", StringComparison.Ordinal) ? fullPath : relative;
    }
}
