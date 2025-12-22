namespace TerminalHost.Core.Domain;

public class FileEditRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
}
