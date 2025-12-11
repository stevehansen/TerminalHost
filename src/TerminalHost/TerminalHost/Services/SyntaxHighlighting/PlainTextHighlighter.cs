using Color = System.Windows.Media.Color;
using System.Windows.Documents;

namespace TerminalHost.Services.SyntaxHighlighting;

public class PlainTextHighlighter : SyntaxHighlighterBase
{
    public override string[] SupportedExtensions => [".txt", ".log", ".ini", ".cfg", ".conf"];

    protected override void HighlightLine(Paragraph paragraph, string line)
    {
        AddRun(paragraph, line, DefaultBrush);
    }
}
