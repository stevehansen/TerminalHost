using Color = System.Windows.Media.Color;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

/// <summary>
/// Syntax highlighter for git diff and patch files.
/// Supports unified diff format with colorized additions, deletions, and metadata.
/// </summary>
public partial class DiffHighlighter : SyntaxHighlighterBase
{
    // Diff-specific colors matching common git diff coloring
    private static readonly SolidColorBrush AdditionBrush = new(Color.FromRgb(0x6A, 0x99, 0x55));       // Green for additions
    private static readonly SolidColorBrush DeletionBrush = new(Color.FromRgb(0xF1, 0x4C, 0x4C));       // Red for deletions
    private static readonly SolidColorBrush HunkHeaderBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));     // Blue for @@ hunk headers
    private static readonly SolidColorBrush FileHeaderBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));     // Orange for file headers
    private static readonly SolidColorBrush MetaBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));           // Light blue for metadata
    private static readonly SolidColorBrush IndexBrush = new(Color.FromRgb(0x85, 0x85, 0x85));          // Gray for index lines

    // Background colors for additions/deletions (subtle highlighting)
    private static readonly SolidColorBrush AdditionBackgroundBrush = new(Color.FromRgb(0x23, 0x36, 0x23));
    private static readonly SolidColorBrush DeletionBackgroundBrush = new(Color.FromRgb(0x3E, 0x1E, 0x1E));

    public override string[] SupportedExtensions => [".diff", ".patch"];

    // Regex patterns for diff elements
    [GeneratedRegex(@"^diff --git", RegexOptions.Compiled)]
    private static partial Regex DiffHeaderRegex();

    [GeneratedRegex(@"^(---|\+\+\+) ", RegexOptions.Compiled)]
    private static partial Regex FileHeaderRegex();

    [GeneratedRegex(@"^@@\s+.*\s+@@", RegexOptions.Compiled)]
    private static partial Regex HunkHeaderRegex();

    [GeneratedRegex(@"^\+(?!\+\+)", RegexOptions.Compiled)]
    private static partial Regex AdditionRegex();

    [GeneratedRegex(@"^-(?!--)", RegexOptions.Compiled)]
    private static partial Regex DeletionRegex();

    [GeneratedRegex(@"^index [0-9a-f]+\.\.[0-9a-f]+", RegexOptions.Compiled)]
    private static partial Regex IndexRegex();

    [GeneratedRegex(@"^(new file mode|deleted file mode|old mode|new mode|similarity index|rename from|rename to|copy from|copy to|Binary files)", RegexOptions.Compiled)]
    private static partial Regex MetaRegex();

    public override FlowDocument CreateHighlightedDocument(string content, int? highlightLine = null)
    {
        var document = new FlowDocument
        {
            Background = BackgroundBrush,
            Foreground = DefaultBrush,
            FontFamily = CodeFont,
            FontSize = FontSize,
            PagePadding = new Thickness(8),
            PageWidth = 10000
        };

        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].TrimEnd('\r');
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 0),
                LineHeight = 18
            };

            // Highlight the specified line
            if (highlightLine.HasValue && lineNumber == highlightLine.Value)
            {
                paragraph.Background = LineHighlightBrush;
            }

            // Add line number
            paragraph.Inlines.Add(new Run($"{lineNumber,4}  ")
            {
                Foreground = LineNumberBrush
            });

            // Add highlighted content with appropriate background
            HighlightDiffLine(paragraph, line);

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        HighlightDiffLine(paragraph, line);
    }

    private void HighlightDiffLine(Paragraph paragraph, string line)
    {
        // Diff header: diff --git a/file b/file
        if (DiffHeaderRegex().IsMatch(line))
        {
            AddRun(paragraph, line, FileHeaderBrush);
            return;
        }

        // File headers: --- a/file or +++ b/file
        if (FileHeaderRegex().IsMatch(line))
        {
            AddRun(paragraph, line, FileHeaderBrush);
            return;
        }

        // Hunk header: @@ -1,5 +1,5 @@
        var hunkMatch = HunkHeaderRegex().Match(line);
        if (hunkMatch.Success)
        {
            AddRun(paragraph, hunkMatch.Value, HunkHeaderBrush);
            // Add context after @@ ... @@ (function name, etc.)
            if (line.Length > hunkMatch.Length)
            {
                AddRun(paragraph, line.Substring(hunkMatch.Length), MetaBrush);
            }
            return;
        }

        // Index line: index abc123..def456 100644
        if (IndexRegex().IsMatch(line))
        {
            AddRun(paragraph, line, IndexBrush);
            return;
        }

        // Meta information
        if (MetaRegex().IsMatch(line))
        {
            AddRun(paragraph, line, MetaBrush);
            return;
        }

        // Addition line: +added content
        if (AdditionRegex().IsMatch(line))
        {
            var run = new Run(line)
            {
                Foreground = AdditionBrush,
                Background = AdditionBackgroundBrush
            };
            paragraph.Inlines.Add(run);
            return;
        }

        // Deletion line: -removed content
        if (DeletionRegex().IsMatch(line))
        {
            var run = new Run(line)
            {
                Foreground = DeletionBrush,
                Background = DeletionBackgroundBrush
            };
            paragraph.Inlines.Add(run);
            return;
        }

        // Context line (unchanged) or any other line
        AddRun(paragraph, line, DefaultBrush);
    }
}
