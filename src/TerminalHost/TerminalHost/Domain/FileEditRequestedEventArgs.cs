using System;

namespace TerminalHost.Domain;

public class FileEditRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
}