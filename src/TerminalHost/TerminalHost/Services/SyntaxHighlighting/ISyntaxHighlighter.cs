using Color = System.Windows.Media.Color;
using System.Windows.Documents;

namespace TerminalHost.Services.SyntaxHighlighting;

public interface ISyntaxHighlighter
{
    string[] SupportedExtensions { get; }
    FlowDocument CreateHighlightedDocument(string content, int? highlightLine = null);
}
