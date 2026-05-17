namespace TerminalHost.Core.Domain;

public class FileViewerRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public FileViewerMode Mode { get; init; } = FileViewerMode.Preview;
}

public enum FileViewerMode
{
    Preview,
    Edit,
    SideBySide
}

public class FileHistoryRequestedEventArgs : EventArgs
{
    public required string WorkingDirectory { get; init; }
    public required string FilePath { get; init; }
}

public class FileBlameRequestedEventArgs : EventArgs
{
    public required string WorkingDirectory { get; init; }
    public required string FilePath { get; init; }
}
