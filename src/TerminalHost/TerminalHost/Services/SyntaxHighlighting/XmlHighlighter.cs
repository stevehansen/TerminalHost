using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace TerminalHost.Services.SyntaxHighlighting;

public partial class XmlHighlighter : SyntaxHighlighterBase
{
    private static readonly SolidColorBrush TagBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));           // Blue for tags
    private static readonly SolidColorBrush AttributeNameBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE)); // Light blue for attributes
    private static readonly SolidColorBrush AttributeValueBrush = new(Color.FromRgb(0xCE, 0x91, 0x78)); // Orange for attribute values
    private static readonly SolidColorBrush BracketXmlBrush = new(Color.FromRgb(0x80, 0x80, 0x80));    // Gray for < > /
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(0xD4, 0xD4, 0xD4));          // Light for text content

    public override string[] SupportedExtensions => [".xml", ".xaml", ".csproj", ".props", ".targets", ".config", ".svg", ".html", ".htm", ".xhtml"];

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<\?.*?\?>", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex ProcessingInstructionRegex();

    [GeneratedRegex(@"</?[\w:.-]+|/?>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"([\w:.-]+)\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.Compiled)]
    private static partial Regex AttributeRegex();

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            // Check for comment start
            if (i < line.Length - 3 && line.Substring(i, 4) == "<!--")
            {
                int endComment = line.IndexOf("-->", i + 4);
                if (endComment >= 0)
                {
                    AddRun(paragraph, line.Substring(i, endComment - i + 3), CommentBrush);
                    i = endComment + 3;
                    continue;
                }
                else
                {
                    AddRun(paragraph, line.Substring(i), CommentBrush);
                    return;
                }
            }

            // Check for processing instruction
            if (i < line.Length - 1 && line.Substring(i, 2) == "<?")
            {
                int endPI = line.IndexOf("?>", i + 2);
                if (endPI >= 0)
                {
                    AddRun(paragraph, line.Substring(i, endPI - i + 2), CommentBrush);
                    i = endPI + 2;
                    continue;
                }
            }

            // Check for tag
            if (line[i] == '<')
            {
                int tagEnd = FindTagEnd(line, i);
                if (tagEnd > i)
                {
                    HighlightTag(paragraph, line.Substring(i, tagEnd - i));
                    i = tagEnd;
                    continue;
                }
            }

            // Find next special character
            int nextSpecial = FindNextSpecial(line, i);
            if (nextSpecial > i)
            {
                AddRun(paragraph, line.Substring(i, nextSpecial - i), TextBrush);
                i = nextSpecial;
            }
            else
            {
                AddRun(paragraph, line[i].ToString(), TextBrush);
                i++;
            }
        }
    }

    private void HighlightTag(Paragraph paragraph, string tag)
    {
        int i = 0;

        // Opening bracket and tag name
        if (tag.StartsWith("</"))
        {
            AddRun(paragraph, "</", BracketXmlBrush);
            i = 2;
        }
        else if (tag.StartsWith("<"))
        {
            AddRun(paragraph, "<", BracketXmlBrush);
            i = 1;
        }

        // Tag name
        int nameEnd = i;
        while (nameEnd < tag.Length && (char.IsLetterOrDigit(tag[nameEnd]) || tag[nameEnd] == ':' || tag[nameEnd] == '.' || tag[nameEnd] == '-'))
        {
            nameEnd++;
        }
        if (nameEnd > i)
        {
            AddRun(paragraph, tag.Substring(i, nameEnd - i), TagBrush);
            i = nameEnd;
        }

        // Attributes and closing
        while (i < tag.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(tag[i]))
            {
                int wsEnd = i;
                while (wsEnd < tag.Length && char.IsWhiteSpace(tag[wsEnd])) wsEnd++;
                AddRun(paragraph, tag.Substring(i, wsEnd - i), DefaultBrush);
                i = wsEnd;
                continue;
            }

            // Check for closing bracket
            if (tag[i] == '>' || (i < tag.Length - 1 && tag.Substring(i, 2) == "/>"))
            {
                AddRun(paragraph, tag.Substring(i), BracketXmlBrush);
                return;
            }

            // Check for attribute
            var attrMatch = AttributeRegex().Match(tag, i);
            if (attrMatch.Success && attrMatch.Index == i)
            {
                var attrName = attrMatch.Groups[1].Value;
                var attrValue = attrMatch.Groups[2].Value;

                AddRun(paragraph, attrName, AttributeNameBrush);
                AddRun(paragraph, "=", OperatorBrush);
                AddRun(paragraph, attrValue, AttributeValueBrush);

                i = attrMatch.Index + attrMatch.Length;
                continue;
            }

            // Any other character
            AddRun(paragraph, tag[i].ToString(), DefaultBrush);
            i++;
        }
    }

    private static int FindTagEnd(string line, int start)
    {
        bool inString = false;
        char stringChar = '"';

        for (int i = start + 1; i < line.Length; i++)
        {
            char c = line[i];

            if (inString)
            {
                if (c == stringChar)
                {
                    inString = false;
                }
            }
            else
            {
                if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == '>')
                {
                    return i + 1;
                }
            }
        }

        return line.Length;
    }

    private static int FindNextSpecial(string line, int start)
    {
        for (int i = start; i < line.Length; i++)
        {
            if (line[i] == '<') return i;
        }
        return line.Length;
    }
}
