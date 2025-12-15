using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public partial class PythonHighlighter : SyntaxHighlighterBase
{
    private static readonly SolidColorBrush BuiltinBrush = new(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly SolidColorBrush DecoratorBrush = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush FunctionBrush = new(Color.FromRgb(0xDC, 0xDC, 0xAA));

    private static readonly HashSet<string> Keywords =
    [
        "False", "None", "True", "and", "as", "assert", "async", "await", "break",
        "class", "continue", "def", "del", "elif", "else", "except", "finally",
        "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal",
        "not", "or", "pass", "raise", "return", "try", "while", "with", "yield"
    ];

    private static readonly HashSet<string> Builtins =
    [
        "print", "len", "range", "str", "int", "float", "list", "dict", "set",
        "tuple", "bool", "type", "isinstance", "open", "input", "abs", "all",
        "any", "enumerate", "filter", "map", "max", "min", "sorted", "sum", "zip"
    ];

    public override string[] SupportedExtensions => [".py", ".pyw"];

    [GeneratedRegex(@"#.*$", RegexOptions.Compiled)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"@[\w.]+", RegexOptions.Compiled)]
    private static partial Regex DecoratorRegex();

    [GeneratedRegex(@"(""""""[\s\S]*?""""""|'''[\s\S]*?'''|f?""(?:[^""\\]|\\.)*""|f?'(?:[^'\\]|\\.)*')", RegexOptions.Compiled)]
    private static partial Regex StringRegex();

    [GeneratedRegex(@"\b\d+\.?\d*(?:[eE][+-]?\d+)?j?\b", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\b[a-zA-Z_][a-zA-Z0-9_]*\b", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        // Handle decorators
        var decoratorMatch = DecoratorRegex().Match(line);
        if (decoratorMatch.Success && line.TrimStart().StartsWith("@"))
        {
            AddRun(paragraph, line.Substring(0, decoratorMatch.Index), DefaultBrush);
            AddRun(paragraph, decoratorMatch.Value, DecoratorBrush);
            HighlightRemainder(paragraph, line.Substring(decoratorMatch.Index + decoratorMatch.Length));
            return;
        }

        // Handle comments
        var commentMatch = CommentRegex().Match(line);
        string mainPart = commentMatch.Success ? line.Substring(0, commentMatch.Index) : line;
        string commentPart = commentMatch.Success ? commentMatch.Value : "";

        TokenizeAndHighlight(paragraph, mainPart);

        if (!string.IsNullOrEmpty(commentPart))
        {
            AddRun(paragraph, commentPart, CommentBrush);
        }
    }

    private void HighlightRemainder(Paragraph paragraph, string text)
    {
        var commentMatch = CommentRegex().Match(text);
        string mainPart = commentMatch.Success ? text.Substring(0, commentMatch.Index) : text;
        string commentPart = commentMatch.Success ? commentMatch.Value : "";

        TokenizeAndHighlight(paragraph, mainPart);

        if (!string.IsNullOrEmpty(commentPart))
        {
            AddRun(paragraph, commentPart, CommentBrush);
        }
    }

    private void TokenizeAndHighlight(Paragraph paragraph, string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            // Check for string
            var stringMatch = StringRegex().Match(text, i);
            if (stringMatch.Success && stringMatch.Index == i)
            {
                AddRun(paragraph, stringMatch.Value, StringBrush);
                i += stringMatch.Length;
                continue;
            }

            // Check for number
            var numberMatch = NumberRegex().Match(text, i);
            if (numberMatch.Success && numberMatch.Index == i)
            {
                AddRun(paragraph, numberMatch.Value, NumberBrush);
                i += numberMatch.Length;
                continue;
            }

            // Check for identifier
            var identMatch = IdentifierRegex().Match(text, i);
            if (identMatch.Success && identMatch.Index == i)
            {
                var word = identMatch.Value;
                SolidColorBrush brush;

                if (Keywords.Contains(word))
                {
                    brush = KeywordBrush;
                }
                else if (Builtins.Contains(word))
                {
                    brush = BuiltinBrush;
                }
                else if (i + word.Length < text.Length && LookAheadForParen(text, i + word.Length))
                {
                    brush = FunctionBrush;
                }
                else
                {
                    brush = DefaultBrush;
                }

                AddRun(paragraph, word, brush);
                i += word.Length;
                continue;
            }

            // Single character
            AddRun(paragraph, text[i].ToString(), OperatorBrush);
            i++;
        }
    }

    private static bool LookAheadForParen(string text, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '(') return true;
            if (!char.IsWhiteSpace(text[i])) return false;
        }
        return false;
    }
}
