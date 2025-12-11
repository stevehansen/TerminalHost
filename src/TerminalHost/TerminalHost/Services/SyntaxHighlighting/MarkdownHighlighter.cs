using Color = System.Windows.Media.Color;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public partial class MarkdownHighlighter : SyntaxHighlighterBase
{
    private static readonly SolidColorBrush HeadingBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush BoldBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush ItalicBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));
    private static readonly SolidColorBrush CodeBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush LinkBrush = new(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly SolidColorBrush ListBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush BlockquoteBrush = new(Color.FromRgb(0x6A, 0x99, 0x55));

    public override string[] SupportedExtensions => [".md", ".markdown"];

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)([-*+]|\d+\.)\s+", RegexOptions.Compiled)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^>\s*", RegexOptions.Compiled)]
    private static partial Regex BlockquoteRegex();

    [GeneratedRegex(@"^```.*$", RegexOptions.Compiled)]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex(@"`[^`]+`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*[^*]+\*\*|__[^_]+__", RegexOptions.Compiled)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)[^*]+\*(?!\*)|(?<!_)_(?!_)[^_]+_(?!_)", RegexOptions.Compiled)]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        // Check for heading
        var headingMatch = HeadingRegex().Match(line);
        if (headingMatch.Success)
        {
            AddRun(paragraph, headingMatch.Groups[1].Value + " ", HeadingBrush);
            HighlightInlineElements(paragraph, headingMatch.Groups[2].Value);
            return;
        }

        // Check for code fence
        if (CodeFenceRegex().IsMatch(line))
        {
            AddRun(paragraph, line, CodeBrush);
            return;
        }

        // Check for blockquote
        var blockquoteMatch = BlockquoteRegex().Match(line);
        if (blockquoteMatch.Success)
        {
            AddRun(paragraph, blockquoteMatch.Value, BlockquoteBrush);
            HighlightInlineElements(paragraph, line.Substring(blockquoteMatch.Length));
            return;
        }

        // Check for list item
        var listMatch = ListRegex().Match(line);
        if (listMatch.Success)
        {
            AddRun(paragraph, listMatch.Value, ListBrush);
            HighlightInlineElements(paragraph, line.Substring(listMatch.Length));
            return;
        }

        // Regular line with inline elements
        HighlightInlineElements(paragraph, line);
    }

    private void HighlightInlineElements(Paragraph paragraph, string text)
    {
        var tokens = new List<(int start, int length, string content, SolidColorBrush brush)>();

        // Collect all inline matches
        foreach (Match match in InlineCodeRegex().Matches(text))
        {
            tokens.Add((match.Index, match.Length, match.Value, CodeBrush));
        }

        foreach (Match match in BoldRegex().Matches(text))
        {
            if (!IsOverlapping(tokens, match.Index, match.Length))
            {
                tokens.Add((match.Index, match.Length, match.Value, BoldBrush));
            }
        }

        foreach (Match match in ItalicRegex().Matches(text))
        {
            if (!IsOverlapping(tokens, match.Index, match.Length))
            {
                tokens.Add((match.Index, match.Length, match.Value, ItalicBrush));
            }
        }

        foreach (Match match in LinkRegex().Matches(text))
        {
            if (!IsOverlapping(tokens, match.Index, match.Length))
            {
                tokens.Add((match.Index, match.Length, match.Value, LinkBrush));
            }
        }

        // Sort by position
        tokens.Sort((a, b) => a.start.CompareTo(b.start));

        // Output with highlighting
        int pos = 0;
        foreach (var token in tokens)
        {
            if (token.start > pos)
            {
                AddRun(paragraph, text.Substring(pos, token.start - pos), DefaultBrush);
            }
            AddRun(paragraph, token.content, token.brush);
            pos = token.start + token.length;
        }

        if (pos < text.Length)
        {
            AddRun(paragraph, text.Substring(pos), DefaultBrush);
        }
    }

    private static bool IsOverlapping(List<(int start, int length, string content, SolidColorBrush brush)> tokens, int start, int length)
    {
        foreach (var token in tokens)
        {
            if (start < token.start + token.length && start + length > token.start)
            {
                return true;
            }
        }
        return false;
    }
}
