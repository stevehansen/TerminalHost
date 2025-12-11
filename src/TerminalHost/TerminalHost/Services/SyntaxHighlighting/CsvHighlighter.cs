using Color = System.Windows.Media.Color;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public class CsvHighlighter : SyntaxHighlighterBase
{
    // Column colors - distinct colors for easy visual differentiation
    private static readonly SolidColorBrush[] ColumnBrushes =
    [
        new(Color.FromRgb(0x9C, 0xDC, 0xFE)),  // Light blue
        new(Color.FromRgb(0xCE, 0x91, 0x78)),  // Orange
        new(Color.FromRgb(0x4E, 0xC9, 0xB0)),  // Teal
        new(Color.FromRgb(0xDC, 0xDC, 0xAA)),  // Yellow
        new(Color.FromRgb(0xC5, 0x86, 0xC0)),  // Purple
        new(Color.FromRgb(0xB5, 0xCE, 0xA8)),  // Light green
        new(Color.FromRgb(0x56, 0x9C, 0xD6)),  // Blue
        new(Color.FromRgb(0xD1, 0x6D, 0x6D)),  // Red
        new(Color.FromRgb(0x6A, 0x99, 0x55)),  // Green
        new(Color.FromRgb(0xD7, 0xBA, 0x7D)),  // Gold
    ];

    private static readonly SolidColorBrush SeparatorBrush = new(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly SolidColorBrush HeaderBrush = new(Color.FromRgb(0xFF, 0xFF, 0xFF));

    private readonly char _separator;
    private bool _isFirstLine = true;

    public CsvHighlighter() : this(',') { }

    protected CsvHighlighter(char separator)
    {
        _separator = separator;
    }

    public override string[] SupportedExtensions => [".csv"];

    public new FlowDocument CreateHighlightedDocument(string content, int? highlightLine = null)
    {
        _isFirstLine = true;
        return base.CreateHighlightedDocument(content, highlightLine);
    }

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        var columns = ParseCsvLine(line, _separator);
        bool isHeader = _isFirstLine;
        _isFirstLine = false;

        for (int i = 0; i < columns.Count; i++)
        {
            // Add separator before column (except first)
            if (i > 0)
            {
                AddRun(paragraph, _separator.ToString(), SeparatorBrush);
            }

            // Get color for this column
            var brush = isHeader
                ? HeaderBrush
                : ColumnBrushes[i % ColumnBrushes.Length];

            // Add the column value
            AddRun(paragraph, columns[i], brush);
        }
    }

    private static List<string> ParseCsvLine(string line, char separator)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Check for escaped quote
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // Skip next quote
                    }
                    else
                    {
                        inQuotes = false;
                        current.Append(c);
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    current.Append(c);
                }
                else if (c == separator)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        // Add the last column
        result.Add(current.ToString());

        return result;
    }
}

public class TsvHighlighter : CsvHighlighter
{
    public TsvHighlighter() : base('\t') { }

    public override string[] SupportedExtensions => [".tsv"];
}
