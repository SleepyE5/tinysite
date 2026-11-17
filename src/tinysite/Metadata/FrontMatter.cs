using System.Globalization;

namespace Tinysite.Metadata;

/// <summary>
/// The metadata block at the top of a Markdown document or layout template.
/// </summary>
/// <remarks>
/// Only <c>title</c>, <c>date</c>, <c>tags</c> and <c>draft</c> are special. Every other
/// key is free-form and reaches the layout template as a <c>{{placeholder}}</c>; nested
/// keys become dotted placeholders such as <c>{{author.name}}</c>.
/// </remarks>
public sealed record FrontMatter
{
    private static readonly Dictionary<string, string> EmptyScalars =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, IReadOnlyList<string>> EmptyLists =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> EmptyMaps =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A block with no keys at all, used for documents that carry no front matter.</summary>
    public static FrontMatter Empty { get; } = new();

    /// <summary>The <c>title</c> key, if present.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The <c>date</c> key. A bare date is read as midnight with a zero offset so that
    /// output never depends on the machine's time zone.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>The <c>tags</c> key, normalised and de-duplicated, order preserved.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The <c>draft</c> key; absent means <see langword="false"/>.</summary>
    public bool Draft { get; init; }

    /// <summary>Every scalar value, the known keys included.</summary>
    public IReadOnlyDictionary<string, string> Scalars { get; init; } = EmptyScalars;

    /// <summary>Every key whose value was written as a sequence, the <c>tags</c> key included.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; init; } = EmptyLists;

    /// <summary>Every key whose value was written as a nested block of <c>key: value</c> lines.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Maps { get; init; } = EmptyMaps;

    /// <summary>The calendar date the author wrote, or <see langword="null"/> without a <c>date</c> key.</summary>
    public DateOnly? Date => PublishedAt.HasValue ? DateOnly.FromDateTime(PublishedAt.Value.Date) : null;

    /// <summary>Looks up a scalar, then a joined sequence, then a dotted nested key.</summary>
    public bool TryGetValue(string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (Scalars.TryGetValue(key, out string? scalar))
        {
            value = scalar;
            return true;
        }

        if (Lists.TryGetValue(key, out IReadOnlyList<string>? items))
        {
            value = string.Join(", ", items);
            return true;
        }

        int dot = key.IndexOf('.');
        if (dot > 0 && dot < key.Length - 1
            && Maps.TryGetValue(key[..dot], out IReadOnlyDictionary<string, string>? map)
            && map.TryGetValue(key[(dot + 1)..], out string? nested))
        {
            value = nested;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>The date formatted for a page, or <see langword="null"/> when there is no date.</summary>
    public string? FormattedDate =>
        PublishedAt?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
