using System.Text;

namespace TerminalHost.Core.Domain;

public class FileEditResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public Encoding? Encoding { get; init; }
    public int LineCount { get; init; }
    public long FileSize { get; init; }
    public bool IsReadOnly { get; init; }

    public bool IsSuccess => Content != null && Error == null;
}
