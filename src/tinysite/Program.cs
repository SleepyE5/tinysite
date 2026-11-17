using Tinysite.Cli;

namespace Tinysite;

/// <summary>
/// The process boundary, and nothing else: parse, dispatch, translate an unexpected failure into
/// a line and an exit code.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CommandLine.Command command;

        try
        {
            command = CommandLine.Parse(args);
        }
        catch (CommandLineException usage)
        {
            Console.Error.WriteLine($"tinysite: {usage.Message}");
            if (usage.Hint is not null)
            {
                Console.Error.WriteLine($"tinysite: {usage.Hint}");
            }

            return 2;
        }

        if (command.Information is not null)
        {
            Console.Out.WriteLine(command.Information);
            return 0;
        }

        if (command.NewDirectory is not null)
        {
            return Scaffold.Run(command.NewDirectory, Console.Out, Console.Error);
        }

        try
        {
            return BuildRunner.Run(command.Build!, Console.Out, Console.Error);
        }
        catch (Exception unexpected)
        {
            // A generator should not have a failure mode it has not named, but when it does the
            // reader deserves the exception type and a hint about the source file, not a stack
            // trace and no exit code.
            Console.Error.WriteLine($"tinysite: unexpected {unexpected.GetType().Name}: {unexpected.Message}");
            Console.Error.WriteLine("tinysite: the source may be fine; report this with the file it was reading.");
            return 1;
        }
    }
}
