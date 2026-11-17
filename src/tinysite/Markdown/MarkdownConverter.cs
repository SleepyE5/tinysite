using System.Text;
using System.Text.RegularExpressions;
using Tinysite.Text;

namespace Tinysite.Markdown;

/// <summary>
/// A Markdown to HTML converter written from scratch: one block pass over the lines, then one
/// inline pass per text run.
/// </summary>
/// <remarks>
/// <para>
/// Supported: ATX headings, paragraphs, emphasis, strong, inline code, fenced code blocks with
/// a language class, links and images with optional titles, unordered and ordered lists with
/// nesting, blockquotes, horizontal rules, backslash escapes.
/// </para>
/// <para>
/// Deliberately not supported, and said so in the README rather than silently mangled: raw HTML,
/// reference-style links, tables, setext headings and definition lists. Every character that is
/// not markup in this dialect is escaped, so a document cannot inject HTML.
/// </para>
/// </remarks>
public static class MarkdownConverter
{
    /// <summary>Hard ceiling on inline nesting so a pathological document cannot overflow the stack.</summary>
    private const int MaxInlineDepth = 48;

    /// <summary>Hard ceiling on block nesting (blockquotes inside lists inside blockquotes).</summary>
    private const int MaxBlockDepth = 24;

    // The closing hash run is optional and, as in CommonMark, only counts when a space
    // separates it from the heading text: "# a heading#######" keeps its hashes.
    private static readonly Regex HeadingPattern =
        new(@"^(?<level>#{1,6})[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.Compiled);

    private static readonly Regex HorizontalRulePattern =
        new(@"^[ ]{0,3}(?:[-*_][ \t]*){3,}$", RegexOptions.Compiled);

    private static readonly Regex FencePattern =
        new(@"^[ ]{0,3}(?<fence>```+|~~~+)[ \t]*(?<info>[^`]*)$", RegexOptions.Compiled);

    private static readonly Regex QuotePattern =
        new(@"^[ ]{0,3}>[ ]?(?<text>.*)$", RegexOptions.Compiled);

    private static readonly Regex ListItemPattern =
        new(@"^(?<indent>[ ]*)(?:(?<bullet>[-*+])|(?<number>\d{1,9})[.)])[ \t]+(?<text>.*)$", RegexOptions.Compiled);

    /// <summary>Converts a Markdown document to an HTML fragment.</summary>
    public static string ToHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string[] lines = TextLines.Normalize(markdown).Split('\n');
        var html = new StringBuilder(markdown.Length / 2);
        foreach (string block in ConvertBlocks(lines, 0, lines.Length, blockDepth: 0))
        {
            html.Append(block).Append('\n');
        }

        return html.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Splits a run of lines into finished HTML blocks. Used recursively for list items and for
    /// the inside of a blockquote, which is what makes <c>&gt; ## Heading</c> work.
    /// </summary>
    private static List<string> ConvertBlocks(string[] lines, int start, int end, int blockDepth)
    {
        var blocks = new List<string>();
        int i = start;

        // Bounded recursion, but text is never dropped: past the budget the remaining lines
        // become ordinary paragraphs so the reader still sees what they wrote.
        if (blockDepth > MaxBlockDepth)
        {
            for (int rest = start; rest < end; rest++)
            {
                if (!IsBlank(lines[rest]))
                {
                    blocks.Add($"<p>{HtmlEscape.Escape(lines[rest].Trim())}</p>");
                }
            }

            return blocks;
        }

        while (i < end)
        {
            string line = lines[i];

            if (IsBlank(line))
            {
                i++;
                continue;
            }

            Match fence = FencePattern.Match(line);
            if (fence.Success)
            {
                int consumed = AppendFencedCode(lines, i, end, fence, blocks);
                i += consumed;
                continue;
            }

            if (HorizontalRulePattern.IsMatch(line))
            {
                blocks.Add("<hr />");
                i++;
                continue;
            }

            Match heading = HeadingPattern.Match(line);
            if (heading.Success)
            {
                int level = heading.Groups["level"].Value.Length;
                blocks.Add($"<h{level}>{ConvertInline(heading.Groups["text"].Value, 0)}</h{level}>");
                i++;
                continue;
            }

            if (QuotePattern.IsMatch(line))
            {
                int consumed = AppendBlockquote(lines, i, end, blockDepth, blocks);
                i += consumed;
                continue;
            }

            if (ListItemPattern.IsMatch(line))
            {
                int consumed = AppendList(lines, i, end, blockDepth, blocks);
                i += consumed;
                continue;
            }

            int paragraphEnd = i;
            while (paragraphEnd < end && !IsBlank(lines[paragraphEnd]) && !StartsNewBlock(lines[paragraphEnd]))
            {
                paragraphEnd++;
            }

            var paragraph = new StringBuilder();
            for (int p = i; p < paragraphEnd; p++)
            {
                if (paragraph.Length > 0)
                {
                    paragraph.Append('\n');
                }

                paragraph.Append(lines[p].Trim());
            }

            blocks.Add($"<p>{ConvertInline(paragraph.ToString(), 0)}</p>");
            i = Math.Max(paragraphEnd, i + 1);
        }

        return blocks;
    }

    private static bool StartsNewBlock(string line) =>
        FencePattern.IsMatch(line)
        || HorizontalRulePattern.IsMatch(line)
        || HeadingPattern.IsMatch(line)
        || QuotePattern.IsMatch(line)
        || ListItemPattern.IsMatch(line);

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static int AppendFencedCode(
        string[] lines, int start, int end, Match fence, List<string> blocks)
    {
        string fenceText = fence.Groups["fence"].Value;
        string info = fence.Groups["info"].Value.Trim();
        string language = info.Length > 0 ? info.Split([' ', '\t'])[0] : string.Empty;
        string classAttribute = language.Length > 0
            ? $" class=\"language-{HtmlEscape.Escape(language)}\""
            : string.Empty;

        var code = new StringBuilder();
        int i = start + 1;
        bool closed = false;

        for (; i < end; i++)
        {
            string candidate = lines[i].Trim();
            if (candidate.Length >= fenceText.Length
                && candidate.TrimStart(fenceText[0]) is { Length: 0 }
                && candidate.All(c => c == fenceText[0]))
            {
                closed = true;
                break;
            }

            if (code.Length > 0)
            {
                code.Append('\n');
            }

            code.Append(lines[i]);
        }

        blocks.Add($"<pre><code{classAttribute}>{HtmlEscape.Escape(code.ToString())}</code></pre>");

        // An unclosed fence swallows the rest of the document, which is what readers expect.
        return closed ? i - start + 1 : end - start;
    }

    private static int AppendBlockquote(string[] lines, int start, int end, int blockDepth, List<string> blocks)
    {
        var quoted = new List<string>();
        int i = start;

        for (; i < end; i++)
        {
            Match match = QuotePattern.Match(lines[i]);
            if (!match.Success)
            {
                break;
            }

            quoted.Add(match.Groups["text"].Value);
        }

        string[] inner = [.. quoted];
        List<string> innerBlocks = ConvertBlocks(inner, 0, inner.Length, blockDepth + 1);
        var body = new StringBuilder();
        foreach (string block in innerBlocks)
        {
            _ = body.Append(block).Append('\n');
        }

        blocks.Add($"<blockquote>\n{body.ToString().TrimEnd('\n')}\n</blockquote>");
        return i - start;
    }

    private static int AppendList(string[] lines, int start, int end, int blockDepth, List<string> blocks)
    {
        var items = new List<ListItem>();
        ListItem? current = null;
        int i = start;
        int blankRun = 0;

        for (; i < end; i++)
        {
            string line = lines[i];

            if (IsBlank(line))
            {
                blankRun++;
                continue;
            }

            Match match = ListItemPattern.Match(line);
            if (match.Success)
            {
                int indent = match.Groups["indent"].Value.Length;
                bool orderedItem = match.Groups["number"].Success;

                // "- a" then "1. b" at the same level is a different list, not another item,
                // and not a child of the item before it.
                if (items.Count > 0 && orderedItem != items[0].Ordered && indent <= items[0].Indent)
                {
                    break;
                }

                current = new ListItem
                {
                    Indent = match.Groups["indent"].Value.Length,
                    Ordered = match.Groups["number"].Success,
                    Content = match.Groups["text"].Value,
                };
                items.Add(current);
                blankRun = 0;
                continue;
            }

            // A blank line followed by an unindented paragraph ends the list; indented text
            // continues the item it belongs to.
            if (blankRun > 0 && TextLines.IndentWidth(line) == 0)
            {
                break;
            }

            if (current is null || TextLines.IndentWidth(line) <= current.Indent)
            {
                break;
            }

            current.Content = current.Content.Length == 0
                ? line.Trim()
                : current.Content + " " + line.Trim();
        }

        if (items.Count == 0)
        {
            return 1;
        }

        ListItem root = BuildListTree(items);
        blocks.Add(RenderList(root.Children, root.Ordered, blockDepth));
        return i - start;
    }

    /// <summary>Turns the flat, indent-tagged item list into a tree of nested lists.</summary>
    private static ListItem BuildListTree(List<ListItem> items)
    {
        // The synthetic root carries the outermost indent. Seeding it with -1 instead would
        // make every second sibling look deeper than its parent and nest under the first item.
        var root = new ListItem { Indent = items[0].Indent, Ordered = items[0].Ordered };
        var trail = new List<(int Indent, ListItem Node, List<ListItem> Children)>
        {
            (root.Indent, root, root.Children),
        };

        foreach (ListItem item in items)
        {
            while (trail.Count > 1 && item.Indent < trail[^1].Indent)
            {
                trail.RemoveAt(trail.Count - 1);
            }

            if (item.Indent > trail[^1].Indent)
            {
                if (trail[^1].Children.Count == 0)
                {
                    // Deeper than anything above it with no parent item: keep it at this level.
                    trail[^1].Children.Add(item);
                    continue;
                }

                ListItem parent = trail[^1].Children[^1];
                trail.Add((item.Indent, parent, parent.Children));
            }

            trail[^1].Children.Add(item);
        }

        return root;
    }

    private static string RenderList(List<ListItem> items, bool ordered, int blockDepth)
    {
        string open = ordered ? "<ol>" : "<ul>";
        string close = ordered ? "</ol>" : "</ul>";
        var html = new StringBuilder(open);

        foreach (ListItem item in items)
        {
            _ = html.Append("<li>");

            if (item.Children.Count > 0)
            {
                // The item's own text stays a paragraph, the children follow inside the same <li>.
                string text = item.Content.Length == 0
                    ? string.Empty
                    : ConvertInline(item.Content, blockDepth + 1);
                _ = html.Append(text)
                    .Append('\n')
                    .Append(RenderList(item.Children, item.Children[0].Ordered, blockDepth));
            }
            else
            {
                _ = html.Append(ConvertInline(item.Content, blockDepth + 1));
            }

            _ = html.Append("</li>");
        }

        _ = html.Append(close);
        return html.ToString();
    }

    /// <summary>A single list item while the tree is being built.</summary>
    private sealed class ListItem
    {
        public int Indent { get; init; }

        public bool Ordered { get; init; }

        public string Content { get; set; } = string.Empty;

        public List<ListItem> Children { get; } = [];
    }

    private static string ConvertInline(string text, int depth) =>
        depth > MaxInlineDepth ? HtmlEscape.Escape(text) : InlineScanner.Run(text, depth);

    /// <summary>
    /// The inline pass. Recognises code spans, links, images, emphasis and backslash escapes;
    /// everything else is escaped character by character.
    /// </summary>
    private static class InlineScanner
    {
        private static readonly char[] EscapePunctuation =
            ['!', '#', '$', '%', '&', '*', '+', '-', '.', '<', '>', '=', '?', '@', '^', '_', '`', '{', '|', '}', '~', ':', ';', '/', '(', ')', '[', ']', '\\', '\''];

        internal static string Run(string text, int depth)
        {
            var html = new StringBuilder(text.Length + 16);
            var literal = new StringBuilder();
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                switch (c)
                {
                    case '\\' when i + 1 < text.Length && Array.IndexOf(EscapePunctuation, text[i + 1]) >= 0:
                        Flush(html, literal);
                        _ = literal.Append(text[i + 1]);
                        Flush(html, literal);
                        i += 2;
                        continue;

                    case '`':
                        int codeEnd = TryCodeSpan(text, i, out string code);
                        if (codeEnd > 0)
                        {
                            Flush(html, literal);
                            _ = html.Append("<code>").Append(HtmlEscape.Escape(code)).Append("</code>");
                            i = codeEnd;
                            continue;
                        }

                        break;

                    case '!' when TryLinkOrImage(text, i, image: true, depth, out string imageHtml, out int imageNext):
                        Flush(html, literal);
                        _ = html.Append(imageHtml);
                        i = imageNext;
                        continue;

                    case '[' when TryLinkOrImage(text, i, image: false, depth, out string linkHtml, out int linkNext):
                        Flush(html, literal);
                        _ = html.Append(linkHtml);
                        i = linkNext;
                        continue;

                    case '*' or '_' when TryEmphasis(text, i, depth, out string emphasisHtml, out int emphasisNext):
                        Flush(html, literal);
                        _ = html.Append(emphasisHtml);
                        i = emphasisNext;
                        continue;
                }

                _ = literal.Append(c);
                i++;
            }

            Flush(html, literal);
            return html.ToString();
        }

        private static void Flush(StringBuilder html, StringBuilder literal)
        {
            if (literal.Length > 0)
            {
                _ = html.Append(HtmlEscape.Escape(literal.ToString()));
                _ = literal.Clear();
            }
        }

        /// <summary>Matches a code span delimited by an equal-length run of backticks.</summary>
        private static int TryCodeSpan(string text, int start, out string content)
        {
            content = string.Empty;
            int markerLength = RunLength(text, start, '`');
            int cursor = start + markerLength;

            while (cursor < text.Length)
            {
                if (text[cursor] == '`')
                {
                    int candidate = RunLength(text, cursor, '`');
                    if (candidate == markerLength)
                    {
                        string inner = text[(start + markerLength)..cursor];
                        // A single space on both sides is decoration, not content.
                        if (inner.Length >= 2 && inner[0] == ' ' && inner[^1] == ' ')
                        {
                            inner = inner[1..^1];
                        }

                        content = inner;
                        return cursor + markerLength;
                    }

                    cursor += candidate;
                    continue;
                }

                cursor++;
            }

            return -1;
        }

        /// <summary>
        /// Renders <c>[text](url "title")</c> and <c>![alt](src "title")</c>. Returns false when the
        /// syntax is incomplete, in which case the brackets stay literal text.
        /// </summary>
        private static bool TryLinkOrImage(string text, int start, bool image, int depth, out string html, out int next)
        {
            html = string.Empty;
            next = start;

            int labelOpen = image ? start + 1 : start;
            if (image && (start + 1 >= text.Length || text[start + 1] != '['))
            {
                return false;
            }

            int labelClose = MatchBracket(text, labelOpen, '[', ']');
            if (labelClose < 0 || labelClose + 1 >= text.Length || text[labelClose + 1] != '(')
            {
                return false;
            }

            int targetClose = MatchParenthesis(text, labelClose + 1);
            if (targetClose < 0)
            {
                return false;
            }

            string label = text[(labelOpen + 1)..labelClose];
            string? target = ExtractTarget(text[(labelClose + 2)..targetClose], out string title);
            if (target is null)
            {
                return false;
            }

            string attributeTitle = title.Length > 0 ? $" title=\"{HtmlEscape.Escape(title)}\"" : string.Empty;

            if (HtmlEscape.IsDangerousUrl(target))
            {
                // The words survive, the executable target does not.
                html = image
                    ? string.Empty
                    : $"<a{attributeTitle}>{Run(label, depth + 1)}</a>";
                next = targetClose + 1;
                return true;
            }

            string url = HtmlEscape.EscapeUrl(target);
            html = image
                ? $"<img src=\"{url}\" alt=\"{HtmlEscape.Escape(label)}\"{attributeTitle} />"
                : $"<a href=\"{url}\"{attributeTitle}>{Run(label, depth + 1)}</a>";

            next = targetClose + 1;
            return true;
        }

        /// <summary>
        /// Splits <c>url "title"</c> or <c>&lt;url with space&gt; 'title'</c>. Returns null when the
        /// remainder cannot be a target, which keeps the whole construct literal.
        /// </summary>
        private static string? ExtractTarget(string inner, out string title)
        {
            title = string.Empty;
            string trimmed = inner.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            if (trimmed[0] == '<')
            {
                int angleClose = trimmed.IndexOf('>');
                if (angleClose < 0)
                {
                    return null;
                }

                string? bracketedTitle = ParseTitle(trimmed[(angleClose + 1)..]);
                if (bracketedTitle is null)
                {
                    return null;
                }

                title = bracketedTitle;
                return trimmed[1..angleClose];
            }

            // Without angle brackets the target ends at the first space that is not inside quotes.
            int split = -1;
            char quote = '\0';
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c is '"' or '\'')
                {
                    quote = quote == '\0' ? c : (quote == c ? '\0' : quote);
                    continue;
                }

                if (char.IsWhiteSpace(c) && quote == '\0')
                {
                    split = i;
                    break;
                }
            }

            if (split < 0)
            {
                return trimmed;
            }

            string remainder = trimmed[split..].Trim();
            string? parsed = ParseTitle(remainder);
            if (parsed is null)
            {
                return null;
            }

            title = parsed;
            return trimmed[..split];
        }

        /// <summary>Returns null when the text is not a title, so the caller can refuse the link.</summary>
        private static string? ParseTitle(string remainder)
        {
            remainder = remainder.Trim();
            if (remainder.Length == 0)
            {
                return string.Empty;
            }

            char quote = remainder[0];
            if (quote is not ('"' or '\''))
            {
                return null;
            }

            return remainder[^1] == quote && remainder.Length >= 2
                ? remainder[1..^1]
                : null;
        }

        /// <summary>Finds the matching closer for a bracket or parenthesis run, counting depth.</summary>
        private static int MatchBracket(string text, int open, char openChar, char closeChar)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }

                if (text[i] == openChar)
                {
                    depth++;
                }
                else if (text[i] == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int MatchParenthesis(string text, int open)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }

                if (c is '"' or '\'')
                {
                    quote = quote == '\0' ? c : (quote == c ? '\0' : quote);
                    continue;
                }

                if (quote != '\0')
                {
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Matches <c>**strong**</c>, <c>*em*</c>, <c>***both***</c> and the underscore forms.
        /// A marker with no closer is left as literal text, which is what makes
        /// <c>*oops</c> print an asterisk instead of eating the rest of the paragraph.
        /// </summary>
        private static bool TryEmphasis(string text, int start, int depth, out string html, out int next)
        {
            html = string.Empty;
            next = start;

            char marker = text[start];

            // Intra-word underscores (snake_case_name) are identifiers, not emphasis.
            if (marker == '_' && start > 0 && char.IsLetterOrDigit(text[start - 1]))
            {
                return false;
            }

            int runLength = RunLength(text, start, marker);

            // A left-flanking opener has to be followed by a non-space, which is what keeps
            // "a * b * c" from turning into emphasis around a space.
            int afterOpener = start + runLength;
            if (afterOpener >= text.Length || char.IsWhiteSpace(text[afterOpener]))
            {
                return false;
            }

            int closer = FindCloser(text, afterOpener, marker, runLength);
            if (closer < 0)
            {
                return false;
            }

            string inner = text[afterOpener..closer];
            if (inner.Trim().Length == 0)
            {
                return false;
            }

            string rendered = Run(inner, depth + 1);
            html = runLength switch
            {
                1 => $"<em>{rendered}</em>",
                2 => $"<strong>{rendered}</strong>",
                _ => $"<strong><em>{rendered}</em></strong>",
            };

            next = closer + RunLength(text, closer, marker);
            return true;
        }

        private static int FindCloser(string text, int from, char marker, int runLength)
        {
            int minimum = runLength >= 3 ? 3 : runLength;
            for (int i = from; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }

                if (text[i] != marker)
                {
                    continue;
                }

                int candidate = RunLength(text, i, marker);
                if (candidate < minimum || i == from)
                {
                    continue;
                }

                // A right-flanking closer has to be preceded by a non-space.
                if (char.IsWhiteSpace(text[i - 1]))
                {
                    continue;
                }

                string inner = text[from..i];
                if (inner.Trim().Length == 0)
                {
                    return -1;
                }

                // An opener run of the same marker inside the candidate span must have its own
                // closer, otherwise "**a *b" would close the strong run with a stray star.
                if (inner.Contains(marker, StringComparison.Ordinal) && candidate < runLength)
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        private static int RunLength(string text, int start, char marker)
        {
            int length = 0;
            while (start + length < text.Length && text[start + length] == marker)
            {
                length++;
            }

            return length;
        }
    }
}
