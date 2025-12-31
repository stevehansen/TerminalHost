using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHost.Core.Domain;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for displaying a file change in the timeline.
/// </summary>
public partial class FileChangeViewModel : ObservableObject
{
    private readonly FileChange _fileChange;

    public FileChangeViewModel(FileChange fileChange)
    {
        _fileChange = fileChange;
    }

    public string Path => _fileChange.Path;
    public string FileName => _fileChange.FileName;
    public int Additions => _fileChange.Additions;
    public int Deletions => _fileChange.Deletions;
    public string Summary => _fileChange.ChangeSummary;
}
