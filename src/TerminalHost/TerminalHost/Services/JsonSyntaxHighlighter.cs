using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace TerminalHost.Services;

public static class JsonSyntaxHighlighter
{
    // Colors matching VS Code dark theme
    private static readonly SolidColorBrush KeyBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));      // Light blue for keys
    private static readonly SolidColorBrush StringBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));   // Orange for strings
    private static readonly SolidColorBrush NumberBrush = new(Color.FromRgb(0xB5, 0xCE, 0xA8));   // Light green for numbers
    private static readonly SolidColorBrush BoolNullBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6)); // Blue for booleans/null
    private static readonly SolidColorBrush BracketBrush = new(Color.FromRgb(0xCC, 0xCC, 0xCC)); // Gray for brackets
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xCC, 0xCC, 0xCC)); // Default text color

    // Regex patterns for JSON tokens
    private static readonly Regex JsonTokenRegex = new(
        @"(?<key>""(?:[^""\\]|\\.)*"")\s*:" +          // Keys (strings before colon)
        @"|(?<string>""(?:[^""\\]|\\.)*"")" +           // String values
        @"|(?<number>-?\d+\.?\d*(?:[eE][+-]?\d+)?)" +   // Numbers
        @"|(?<bool>true|false)" +                       // Booleans
        @"|(?<null>null)" +                             // Null
        @"|(?<bracket>[\[\]{}])" +                      // Brackets
        @"|(?<colon>:)" +                               // Colon
        @"|(?<comma>,)",                                // Comma
        RegexOptions.Compiled);

    public static FlowDocument CreateHighlightedDocument(string json)
    {
        var document = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = DefaultBrush,
            FontFamily = (FontFamily)Application.Current.Resources["FontFamilyMonospace"],
            FontSize = (double)Application.Current.Resources["FontSizeCode"],
            PagePadding = new Thickness(8)
        };

        var paragraph = new Paragraph();

        int lastIndex = 0;
        foreach (Match match in JsonTokenRegex.Matches(json))
        {
            // Add any text between matches (whitespace, etc.)
            if (match.Index > lastIndex)
            {
                var betweenText = json.Substring(lastIndex, match.Index - lastIndex);
                paragraph.Inlines.Add(new Run(betweenText) { Foreground = DefaultBrush });
            }

            // Add the matched token with appropriate color
            var run = new Run(match.Value);

            if (match.Groups["key"].Success)
            {
                run.Foreground = KeyBrush;
            }
            else if (match.Groups["string"].Success)
            {
                run.Foreground = StringBrush;
            }
            else if (match.Groups["number"].Success)
            {
                run.Foreground = NumberBrush;
            }
            else if (match.Groups["bool"].Success || match.Groups["null"].Success)
            {
                run.Foreground = BoolNullBrush;
            }
            else if (match.Groups["bracket"].Success)
            {
                run.Foreground = BracketBrush;
            }
            else
            {
                run.Foreground = DefaultBrush;
            }

            paragraph.Inlines.Add(run);
            lastIndex = match.Index + match.Length;
        }

        // Add any remaining text after the last match
        if (lastIndex < json.Length)
        {
            paragraph.Inlines.Add(new Run(json.Substring(lastIndex)) { Foreground = DefaultBrush });
        }

        document.Blocks.Add(paragraph);
        return document;
    }

    public static string GetPlainText(FlowDocument document)
    {
        return new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd();
    }
}
