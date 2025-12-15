using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public partial class JavaScriptHighlighter : SyntaxHighlighterBase
{
    private static readonly SolidColorBrush FunctionBrush = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush TemplateBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));

    private static readonly HashSet<string> Keywords =
    [
        "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for",
        "function", "if", "import", "in", "instanceof", "let", "new", "null", "return",
        "static", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined",
        "var", "void", "while", "with", "yield", "async", "await", "of", "from", "as"
    ];

    private static readonly HashSet<string> TypeScriptKeywords =
    [
        "interface", "type", "namespace", "module", "declare", "abstract", "implements",
        "private", "protected", "public", "readonly", "keyof", "infer", "never", "unknown",
        "any", "string", "number", "boolean", "object", "symbol", "bigint"
    ];

    public override string[] SupportedExtensions => [".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs"];

    [GeneratedRegex(@"//.*$", RegexOptions.Compiled)]
    private static partial Regex SingleLineCommentRegex();

    [GeneratedRegex(@"(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`)", RegexOptions.Compiled)]
    private static partial Regex StringRegex();

    [GeneratedRegex(@"\b\d+\.?\d*(?:[eE][+-]?\d+)?n?\b", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\b[a-zA-Z_$][a-zA-Z0-9_$]*\b", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        // Handle comments
        var commentMatch = SingleLineCommentRegex().Match(line);
        string mainPart = commentMatch.Success ? line.Substring(0, commentMatch.Index) : line;
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
            // Check for string (including template literals)
            var stringMatch = StringRegex().Match(text, i);
            if (stringMatch.Success && stringMatch.Index == i)
            {
                var strVal = stringMatch.Value;
                if (strVal.StartsWith("`"))
                {
                    AddRun(paragraph, strVal, TemplateBrush);
                }
                else
                {
                    AddRun(paragraph, strVal, StringBrush);
                }
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

                if (Keywords.Contains(word) || TypeScriptKeywords.Contains(word))
                {
                    brush = KeywordBrush;
                }
                else if (char.IsUpper(word[0]) && word.Length > 1)
                {
                    brush = TypeBrush;
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
        for (int j = start; j < text.Length; j++)
        {
            if (text[j] == '(') return true;
            if (!char.IsWhiteSpace(text[j])) return false;
        }
        return false;
    }
}
