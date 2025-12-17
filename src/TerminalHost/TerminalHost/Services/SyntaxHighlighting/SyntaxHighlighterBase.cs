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

    protected static System.Windows.Media.FontFamily CodeFont => (System.Windows.Media.FontFamily)Application.Current.Resources["FontFamilyMonospace"];
    protected static double FontSize => (double)Application.Current.Resources["FontSizeCode"];

    public abstract string[] SupportedExtensions { get; }

    public virtual FlowDocument CreateHighlightedDocument(string content, int? highlightLine = null)
    {
        var document = new FlowDocument
        {
            Background = BackgroundBrush,
            Foreground = DefaultBrush,
            FontFamily = CodeFont,
            FontSize = FontSize,
            PagePadding = new Thickness(0), // Remove default padding for better table fit
            PageWidth = 10000
        };

        var table = new Table { CellSpacing = 0 };
        // Line number column
        table.Columns.Add(new TableColumn { Width = new GridLength(50) });
        // Code column
        table.Columns.Add(new TableColumn { Width = GridLength.Auto }); // Or star? Auto works well for code

        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);
        document.Blocks.Add(table);

        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].TrimEnd('\r');
            
            var row = new TableRow();
            
            // Highlight the specified line
            if (highlightLine.HasValue && lineNumber == highlightLine.Value)
            {
                row.Background = LineHighlightBrush;
            }

            // Line number cell
            var numParagraph = new Paragraph(new Run($"{lineNumber}  "))
            {
                TextAlignment = TextAlignment.Right,
                Foreground = LineNumberBrush,
                Margin = new Thickness(0, 0, 8, 0),
                LineHeight = 18
            };
            row.Cells.Add(new TableCell(numParagraph));

            // Content cell
            var contentParagraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 18
            };
            HighlightLine(contentParagraph, line);
            row.Cells.Add(new TableCell(contentParagraph));

            rowGroup.Rows.Add(row);
        }

        return document;
    }

    protected abstract void HighlightLine(Paragraph paragraph, string line);

    protected static void AddRun(Paragraph paragraph, string text, SolidColorBrush brush)
    {
        if (!string.IsNullOrEmpty(text))
        {
            paragraph.Inlines.Add(new Run(text) { Foreground = brush });
        }
    }
}
