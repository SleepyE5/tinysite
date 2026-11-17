using System.Text;

namespace Tinysite.Tests;

/// <summary>
/// A throwaway directory that cleans itself up, with the file writes a test needs.
/// </summary>
/// <remarks>
/// Tests that build a site are about the interaction of the reader, the builder and the
/// filesystem, so they are run against a real filesystem rather than an abstraction invented for
/// them. UTF-8 without a BOM everywhere: the writer under test does the same, and a test that
/// disagreed would pass while the bytes were wrong.
/// </remarks>
internal sealed class TestTree : IDisposable
{
    private TestTree(string root) => Root = root;

    /// <summary>Absolute path of the directory.</summary>
    internal string Root { get; }

    internal static TestTree Create(string purpose = "tinysite")
    {
        string root = Path.Combine(Path.GetTempPath(), purpose + "-" + Guid.NewGuid().ToString("N")[..12]);
        _ = Directory.CreateDirectory(root);
        return new TestTree(root);
    }

    /// <summary>Writes a file below the root, creating directories as needed, and returns its full path.</summary>
    internal string Write(string relative, string text)
    {
        string target = Absolute(relative);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return target;
    }

    /// <summary>Writes raw bytes, for the static-file path where content must not be reinterpreted.</summary>
    internal string WriteBytes(string relative, ReadOnlySpan<byte> bytes)
    {
        string target = Absolute(relative);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes.ToArray());
        return target;
    }

    internal string Read(string relative) => File.ReadAllText(Absolute(relative));

    internal byte[] ReadBytes(string relative) => File.ReadAllBytes(Absolute(relative));

    internal bool Exists(string relative) => File.Exists(Absolute(relative)) || Directory.Exists(Absolute(relative));

    internal string Absolute(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every file below <paramref name="relative"/>, as <c>/</c>-separated relative paths, sorted.</summary>
    internal IReadOnlyList<string> Tree(string relative = ".")
    {
        string root = Absolute(relative);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>A source tree in the shape <c>tinysite new</c> writes, so the test builds real input.</summary>
    internal static TestTree ScaffoldSource()
    {
        TestTree tree = Create("tinysite-source");
        foreach ((string name, string text) in Tinysite.Cli.Scaffold.Files)
        {
            _ = tree.Write(name, text);
        }

        return tree;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test; the OS will collect it.
        }
    }
}
