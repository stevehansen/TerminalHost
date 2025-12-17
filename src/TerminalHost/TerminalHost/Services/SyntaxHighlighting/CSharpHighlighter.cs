using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public partial class CSharpHighlighter : SyntaxHighlighterBase
{
    private static readonly SolidColorBrush PreprocessorBrush = new(Color.FromRgb(0x9B, 0x9B, 0x9B));
    private static readonly SolidColorBrush MethodBrush = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush NamespaceBrush = new(Color.FromRgb(0x4E, 0xC9, 0xB0));

    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "async", "await", "var", "dynamic", "yield", "partial",
        "where", "when", "get", "set", "init", "add", "remove", "value", "global", "record",
        "required", "scoped", "file", "with"
    ];

    public override string[] SupportedExtensions => [".cs"];

    [GeneratedRegex(@"^\s*#.*$", RegexOptions.Compiled)]
    private static partial Regex PreprocessorRegex();

    [GeneratedRegex(@"//.*$", RegexOptions.Compiled)]
    private static partial Regex SingleLineCommentRegex();

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""|@""(?:[^""]|"""")*""|'(?:[^'\\]|\\.)'", RegexOptions.Compiled)]
    private static partial Regex StringRegex();

    [GeneratedRegex(@"\b\d+\.?\d*[fFdDmMlLuU]?\b", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\b[A-Z][a-zA-Z0-9_]*\b", RegexOptions.Compiled)]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"\b[a-zA-Z_][a-zA-Z0-9_]*\s*(?=\()", RegexOptions.Compiled)]
    private static partial Regex MethodRegex();

    [GeneratedRegex(@"\b[a-zA-Z_][a-zA-Z0-9_]*\b", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        // Handle preprocessor directives
        if (PreprocessorRegex().IsMatch(line))
        {
            AddRun(paragraph, line, PreprocessorBrush);
            return;
        }

        // Handle single-line comments
        var commentMatch = SingleLineCommentRegex().Match(line);
        string mainPart = commentMatch.Success ? line.Substring(0, commentMatch.Index) : line;
        string commentPart = commentMatch.Success ? commentMatch.Value : "";

        // Parse main part with tokens
        var tokens = TokenizeLine(mainPart);
        foreach (var (text, brush) in tokens)
        {
            AddRun(paragraph, text, brush);
        }

        // Add comment part
        if (!string.IsNullOrEmpty(commentPart))
        {
            AddRun(paragraph, commentPart, CommentBrush);
        }
    }

    private static List<(string text, SolidColorBrush brush)> TokenizeLine(string line)
    {
        var result = new List<(string, SolidColorBrush)>();
        var processed = new bool[line.Length];

        // First pass: strings
        foreach (Match match in StringRegex().Matches(line))
        {
            MarkProcessed(processed, match.Index, match.Length);
        }

        // Second pass: numbers (only if not in string)
        foreach (Match match in NumberRegex().Matches(line))
        {
            if (!IsProcessed(processed, match.Index, match.Length))
            {
                MarkProcessed(processed, match.Index, match.Length);
            }
        }

        // Build result by scanning through
        int i = 0;
        while (i < line.Length)
        {
            // Check for string
            var stringMatch = StringRegex().Match(line, i);
            if (stringMatch.Success && stringMatch.Index == i)
            {
                result.Add((stringMatch.Value, StringBrush));
                i += stringMatch.Length;
                continue;
            }

            // Check for number
            var numberMatch = NumberRegex().Match(line, i);
            if (numberMatch.Success && numberMatch.Index == i)
            {
                result.Add((numberMatch.Value, NumberBrush));
                i += numberMatch.Length;
                continue;
            }

            // Check for identifier/keyword
            var identMatch = IdentifierRegex().Match(line, i);
            if (identMatch.Success && identMatch.Index == i)
            {
                var word = identMatch.Value;
                SolidColorBrush brush;

                if (Keywords.Contains(word))
                {
                    brush = KeywordBrush;
                }
                else if (char.IsUpper(word[0]))
                {
                    brush = TypeBrush;
                }
                else if (i + word.Length < line.Length && LookAheadForParen(line, i + word.Length))
                {
                    brush = MethodBrush;
                }
                else
                {
                    brush = DefaultBrush;
                }

                result.Add((word, brush));
                i += word.Length;
                continue;
            }

            // Single character (whitespace, operators, etc.)
            result.Add((line[i].ToString(), OperatorBrush));
            i++;
        }

        return result;
    }

    private static bool LookAheadForParen(string line, int start)
    {
        for (int i = start; i < line.Length; i++)
        {
            if (line[i] == '(') return true;
            if (!char.IsWhiteSpace(line[i])) return false;
        }
        return false;
    }

    private static void MarkProcessed(bool[] processed, int start, int length)
    {
        for (int i = start; i < start + length && i < processed.Length; i++)
        {
            processed[i] = true;
        }
    }

    private static bool IsProcessed(bool[] processed, int start, int length)
    {
        for (int i = start; i < start + length && i < processed.Length; i++)
        {
            if (processed[i]) return true;
        }
        return false;
    }
}
