namespace TerminalHost.Domain;

public class FilePreviewRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public bool OpenInEditMode { get; init; }
}