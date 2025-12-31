namespace TerminalHost.Core.Domain;

public class FileEditRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public int? LineNumber { get; init; }
}
