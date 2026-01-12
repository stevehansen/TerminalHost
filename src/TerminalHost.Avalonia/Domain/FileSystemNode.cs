using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.Domain;

public partial class FileSystemNode : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _fullPath = "";

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private FileSystemNode? _parent;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _childrenLoaded;

    // Git integration
    [ObservableProperty]
    private GitFileStatusType? _gitStatus;

    [ObservableProperty]
    private bool _isGitIgnored;

    // Submodule support
    [ObservableProperty]
    private bool _isSubmodule;

    [ObservableProperty]
    private SubmoduleStatus _submoduleStatus = SubmoduleStatus.None;

    public ObservableCollection<FileSystemNode> Children { get; } = [];

    // Computed properties
    public string Extension => IsDirectory ? "" : System.IO.Path.GetExtension(FullPath).ToLowerInvariant();

    public string Icon => IsSubmodule ? "📦" : FileIconMapper.GetIcon(FullPath, IsDirectory, IsExpanded);

    public string SubmoduleStatusIcon => SubmoduleStatus switch
    {
        SubmoduleStatus.Clean => "✓",
        SubmoduleStatus.Modified => "●",
        SubmoduleStatus.Uninitialized => "○",
        _ => ""
    };

    public string SubmoduleStatusColor => SubmoduleStatus switch
    {
        SubmoduleStatus.Clean => "#4EC9B0",      // Green
        SubmoduleStatus.Modified => "#E2C08D",   // Yellow/orange
        SubmoduleStatus.Uninitialized => "#808080", // Gray
        _ => "#CCCCCC"
    };

    public string SubmoduleTooltip => SubmoduleStatus switch
    {
        SubmoduleStatus.Clean => "Submodule (clean)",
        SubmoduleStatus.Modified => "Submodule (modified)",
        SubmoduleStatus.Uninitialized => "Submodule (not initialized)",
        _ => ""
    };

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

    public string GitStatusColor => GitStatus switch
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

    /// <summary>
    /// Row background color as hex string (with alpha). UI layer converts to brush.
    /// </summary>
    public string? RowBackgroundHex => GitStatus switch
    {
        GitFileStatusType.Modified => "#20FFFF00",   // Yellow tint
        GitFileStatusType.Added => "#2000FF00",      // Green tint
        GitFileStatusType.Deleted => "#20FF0000",    // Red tint
        GitFileStatusType.Untracked => "#20808080",  // Gray tint
        GitFileStatusType.Renamed => "#200080FF",    // Blue tint
        GitFileStatusType.Conflicted => "#40FF0000", // Stronger red
        _ => null
    };

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Icon));
    }

    partial void OnGitStatusChanged(GitFileStatusType? value)
    {
        OnPropertyChanged(nameof(GitStatusIcon));
        OnPropertyChanged(nameof(GitStatusColor));
        OnPropertyChanged(nameof(RowBackgroundHex));
    }

    partial void OnIsSubmoduleChanged(bool value)
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(SubmoduleTooltip));
    }

    partial void OnSubmoduleStatusChanged(SubmoduleStatus value)
    {
        OnPropertyChanged(nameof(SubmoduleStatusIcon));
        OnPropertyChanged(nameof(SubmoduleStatusColor));
        OnPropertyChanged(nameof(SubmoduleTooltip));
    }

    public static FileSystemNode CreateDummy()
    {
        return new FileSystemNode { Name = "Loading...", IsLoading = true };
    }
}
