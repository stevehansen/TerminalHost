namespace TerminalHost.Services;

public class GitOperationResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}
