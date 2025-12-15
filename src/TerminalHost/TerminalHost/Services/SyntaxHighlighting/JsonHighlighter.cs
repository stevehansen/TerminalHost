using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace TerminalHost.Services.SyntaxHighlighting;

public partial class JsonHighlighter : SyntaxHighlighterBase
{
    public override string[] SupportedExtensions => [".json", ".jsonc"];

    [GeneratedRegex(
        @"(?<key>""(?:[^""\\]|\\.)*"")\s*:" +
        @"|(?<string>""(?:[^""\\]|\\.)*"")" +
        @"|(?<number>-?\d+\.?\d*(?:[eE][+-]?\d+)?)" +
        @"|(?<bool>true|false)" +
        @"|(?<null>null)" +
        @"|(?<bracket>[\[\]{}])" +
        @"|(?<colon>:)" +
        @"|(?<comma>,)",
        RegexOptions.Compiled)]
    private static partial Regex JsonTokenRegex();

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        int lastIndex = 0;
        foreach (Match match in JsonTokenRegex().Matches(line))
        {
            if (match.Index > lastIndex)
            {
                AddRun(paragraph, line.Substring(lastIndex, match.Index - lastIndex), DefaultBrush);
            }

            if (match.Groups["key"].Success)
            {
                AddRun(paragraph, match.Value, PropertyBrush);
            }
            else if (match.Groups["string"].Success)
            {
                AddRun(paragraph, match.Value, StringBrush);
            }
            else if (match.Groups["number"].Success)
            {
                AddRun(paragraph, match.Value, NumberBrush);
            }
            else if (match.Groups["bool"].Success || match.Groups["null"].Success)
            {
                AddRun(paragraph, match.Value, KeywordBrush);
            }
            else if (match.Groups["bracket"].Success)
            {
                AddRun(paragraph, match.Value, BracketBrush);
            }
            else
            {
                AddRun(paragraph, match.Value, DefaultBrush);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            AddRun(paragraph, line.Substring(lastIndex), DefaultBrush);
        }
    }
}
