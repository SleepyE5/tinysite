using Tinysite.Content;
using Tinysite.Publishing;

namespace Tinysite.Cli;

/// <summary>
/// Writes a source tree that builds on the first try.
/// </summary>
/// <remarks>
/// The point of <c>tinysite new</c> is that the next command a stranger types is
/// <c>tinysite build</c> and something readable appears. So the scaffold is not a stub: it has a
/// layout with its site values, one dated post, one undated page, and a stylesheet, and it is
/// exactly the input the test suite builds to check a real end-to-end publish.
/// </remarks>
public static class Scaffold
{
    /// <summary>Files written by <see cref="Run"/>, in the order a reader would want them listed.</summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SourceReader.LayoutFileName] = """
            ---
            title: My site
            description: Written by me, published by tinysite.
            language: en
            ---
            <!doctype html>
            <html lang="{{language}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{full_title}}</title>
            <meta name="description" content="{{description}}">
            <link rel="canonical" href="{{canonical}}">
            <link rel="alternate" type="application/rss+xml" title="{{site_title}}" href="{{feed_url}}">
            <link rel="stylesheet" href="{{base_path}}/style.css">
            </head>
            <body>
            <header>
              <a href="{{base_path}}/">{{site_title}}</a>
              <nav><a href="{{base_path}}/archive/">Archive</a> <a href="{{base_path}}/tags/">Tags</a></nav>
            </header>
            <main>
            {{content}}
            </main>
            <footer>
              <p>Published with tinysite. <a href="{{base_path}}/feed.xml">Feed</a></p>
            </footer>
            </body>
            </html>

            """,

        ["content/posts/2026-01-01-hello.md"] = """
            ---
            title: Hello
            tags: [first]
            description: The post that comes with a new tinysite source tree.
            ---
            This file is `content/posts/2026-01-01-hello.md`. The date in its name is what makes it
            a post: a document in `content/` with a `yyyy-mm-dd-` prefix appears in the archive and
            the feed, and one without it is a page.

            ## Markdown

            Headings, lists, fenced code, tables, links and images are converted by tinysite's own
            parser. A link to `/about/` is left alone, because it is a path a reader can follow.

            - a list
            - with two items

            ```csharp
            Console.WriteLine("fenced code keeps its language class");
            ```

            """,

        ["content/about.md"] = """
            ---
            title: About
            ---
            This is a page rather than a post: no date in the file name, so it is published at
            `/about/` and left out of the archive and the feed.

            """,

        ["static/style.css"] = """
            /* Everything in static/ is copied byte for byte, with no processing at all. */
            :root { color-scheme: light dark; --fg: #1c1c1c; --bg: #fdfdfb; }
            @media (prefers-color-scheme: dark) { :root { --fg: #e6e4df; --bg: #17191c; } }
            body { font: 16px/1.6 Georgia, serif; color: var(--fg); background: var(--bg); margin: 0; }
            header, main, footer { max-width: 42rem; margin: 0 auto; padding: 1rem; }
            header nav { display: inline; margin-left: 1rem; }
            pre { background: #22262b; color: #e6e4df; padding: .75rem; overflow: auto; }
            .posts { list-style: none; padding: 0; }
            .posts time { color: #777; }

            """,

        ["README.md"] = """
            # My site

            A [tinysite](https://github.com/SleepyE5/tinysite) source tree.

            ```sh
            tinysite build --source . --output _site --base-url https://you.github.io/site
            ```

            `layout.html` is the only template, with `{{placeholders}}` filled from a document's
            front matter. Markdown lives in `content/`; anything with a `yyyy-mm-dd-` file name
            becomes a post and joins `/archive/` and `/feed.xml`. Files in `static/` are copied
            unchanged.

            Delete this file if you would rather keep your notes somewhere else; tinysite ignores
            it because it is not in `content/`.
            """,
    };

    /// <summary>
    /// Writes the scaffold into <paramref name="directory"/>, which must not already hold files.
    /// </summary>
    /// <returns>Exit code: 0 when it wrote, 1 when it refused.</returns>
    public static int Run(string directory, TextWriter output, TextWriter error)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string root = Path.GetFullPath(directory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            error.WriteLine($"tinysite: '{root}' is not empty, and a half-written scaffold is worse than none.");
            error.WriteLine("tinysite: point `new` at a new directory, or run `tinysite build` against this one.");
            return BuildRunner.SourceBroken;
        }

        foreach ((string relative, string text) in Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            output.WriteLine($"  {relative}");
        }

        output.WriteLine($"tinysite: wrote {Files.Count} files to {directory}.");
        output.WriteLine("tinysite: next: tinysite build --source . --output _site --base-url https://your-site.example");
        return BuildRunner.Ok;
    }
}
