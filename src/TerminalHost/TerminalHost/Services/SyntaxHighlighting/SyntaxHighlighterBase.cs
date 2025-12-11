using Color = System.Windows.Media.Color;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public abstract class SyntaxHighlighterBase : ISyntaxHighlighter
{
    // Common colors matching VS Code dark theme
    protected static readonly SolidColorBrush KeywordBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));     // Blue for keywords
    protected static readonly SolidColorBrush TypeBrush = new(Color.FromRgb(0x4E, 0xC9, 0xB0));        // Teal for types
    protected static readonly SolidColorBrush StringBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));      // Orange for strings
    protected static readonly SolidColorBrush NumberBrush = new(Color.FromRgb(0xB5, 0xCE, 0xA8));      // Light green for numbers
    protected static readonly SolidColorBrush CommentBrush = new(Color.FromRgb(0x6A, 0x99, 0x55));     // Green for comments
    protected static readonly SolidColorBrush PropertyBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));    // Light blue for properties
    protected static readonly SolidColorBrush OperatorBrush = new(Color.FromRgb(0xD4, 0xD4, 0xD4));    // Light gray for operators
    protected static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xCC, 0xCC, 0xCC));     // Default text color
    protected static readonly SolidColorBrush BracketBrush = new(Color.FromRgb(0xCC, 0xCC, 0xCC));     // Gray for brackets

    protected static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    protected static readonly SolidColorBrush LineHighlightBrush = new(Color.FromRgb(0x26, 0x4F, 0x78));
    protected static readonly SolidColorBrush LineNumberBrush = new(Color.FromRgb(0x85, 0x85, 0x85));

    protected static readonly System.Windows.Media.FontFamily CodeFont = new("Cascadia Code NF, Consolas, Courier New");
    protected const double FontSize = 13;

    public abstract string[] SupportedExtensions { get; }

    public FlowDocument CreateHighlightedDocument(string content, int? highlightLine = null)
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

            // Add highlighted content
            HighlightLine(paragraph, line);

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    protected abstract void HighlightLine(Paragraph paragraph, string line);

    protected void AddRun(Paragraph paragraph, string text, SolidColorBrush brush)
    {
        if (!string.IsNullOrEmpty(text))
        {
            paragraph.Inlines.Add(new Run(text) { Foreground = brush });
        }
    }
}
