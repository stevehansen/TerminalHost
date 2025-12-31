namespace TerminalHost.Core.Domain;

/// <summary>
/// Portable data model for file system nodes.
/// Used by IFileExplorerService. Platform-specific UI can create
/// observable wrappers around this data.
/// </summary>
public class FileSystemNodeData
{
    /// <summary>File or directory name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Full path to the file or directory.</summary>
    public string FullPath { get; set; } = "";

    /// <summary>Whether this node is a directory.</summary>
    public bool IsDirectory { get; set; }

    /// <summary>Git status type, if applicable.</summary>
    public GitFileStatusType? GitStatus { get; set; }

    /// <summary>Whether the file is ignored by .gitignore.</summary>
    public bool IsGitIgnored { get; set; }

    /// <summary>File extension (empty for directories).</summary>
    public string Extension => IsDirectory ? "" : System.IO.Path.GetExtension(FullPath).ToLowerInvariant();

    /// <summary>Icon for display.</summary>
    public string Icon => FileIconMapper.GetIcon(FullPath, IsDirectory, false);

    /// <summary>Git status icon character.</summary>
    public string GitStatusIcon => GitStatus switch
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
        _ => ""
    };

    /// <summary>Git status color as hex string.</summary>
    public string GitStatusColorHex => GitStatus switch
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
}
