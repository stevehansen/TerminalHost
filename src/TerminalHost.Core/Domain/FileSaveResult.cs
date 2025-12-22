namespace TerminalHost.Core.Domain;

public class FileSaveResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
}
