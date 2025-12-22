using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHost.Core.Domain;

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

    public ObservableCollection<FileSystemNode> Children { get; } = [];

    // Computed properties
    public string Extension => IsDirectory ? "" : System.IO.Path.GetExtension(FullPath).ToLowerInvariant();

    public string Icon => FileIconMapper.GetIcon(FullPath, IsDirectory, IsExpanded);

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

    public Brush? RowBackground => GitStatus switch
    {
        GitFileStatusType.Modified => new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0x00)),   // Yellow tint
        GitFileStatusType.Added => new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0xFF, 0x00)),      // Green tint
        GitFileStatusType.Deleted => new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0x00, 0x00)),    // Red tint
        GitFileStatusType.Untracked => new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)),  // Gray tint
        GitFileStatusType.Renamed => new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x80, 0xFF)),    // Blue tint
        GitFileStatusType.Conflicted => new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x00, 0x00)), // Stronger red
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
        OnPropertyChanged(nameof(RowBackground));
    }

    public static FileSystemNode CreateDummy()
    {
        return new FileSystemNode { Name = "Loading...", IsLoading = true };
    }
}
