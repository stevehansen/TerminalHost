namespace TerminalHost.Core.Domain;

public enum GitFileStatusType
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Ignored,
    Conflicted,
    TypeChanged
}

public class GitFileStatus
{
    public string FilePath { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? "";
    public GitFileStatusType Status { get; set; }
    public bool IsStaged { get; set; }

    // For renamed files
    public string? OriginalPath { get; set; }

    // For submodules
    public bool IsSubmodule { get; set; }

    public string StatusIcon => IsSubmodule ? "S" : Status switch
    {
        GitFileStatusType.Modified => "M",
        GitFileStatusType.Added => "A",
        GitFileStatusType.Deleted => "D",
        GitFileStatusType.Renamed => "R",
        GitFileStatusType.Copied => "C",
        GitFileStatusType.Untracked => "?",
        GitFileStatusType.Ignored => "!",
        GitFileStatusType.Conflicted => "U",
        GitFileStatusType.TypeChanged => "T",
        _ => "?"
    };

    public string StatusColor => IsSubmodule ? "#9CDCFE" : Status switch // Light blue for submodules
    {
        GitFileStatusType.Modified => "#E2C08D",   // Yellow/orange
        GitFileStatusType.Added => "#4EC9B0",      // Green
        GitFileStatusType.Deleted => "#F14C4C",    // Red
        GitFileStatusType.Renamed => "#569CD6",    // Blue
        GitFileStatusType.Copied => "#569CD6",     // Blue
        GitFileStatusType.Untracked => "#808080",  // Gray
        GitFileStatusType.Ignored => "#555555",    // Dark gray
        GitFileStatusType.Conflicted => "#FF6B6B", // Bright red
        GitFileStatusType.TypeChanged => "#C586C0", // Purple
        _ => "#CCCCCC"
    };

    public string StatusDescription => IsSubmodule ? "Submodule" : Status switch
    {
        GitFileStatusType.Modified => "Modified",
        GitFileStatusType.Added => "Added",
        GitFileStatusType.Deleted => "Deleted",
        GitFileStatusType.Renamed => OriginalPath != null ? $"Renamed from {OriginalPath}" : "Renamed",
        GitFileStatusType.Copied => "Copied",
        GitFileStatusType.Untracked => "Untracked",
        GitFileStatusType.Ignored => "Ignored",
        GitFileStatusType.Conflicted => "Conflict",
        GitFileStatusType.TypeChanged => "Type changed",
        _ => "Unknown"
    };
}
