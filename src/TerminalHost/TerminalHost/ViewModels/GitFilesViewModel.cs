using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows; // For FlowDocument, MessageBox
using System.Windows.Documents;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.Services.SyntaxHighlighting;
using TerminalHost.ViewModels; // For TerminalPairTabViewModel

namespace TerminalHost.ViewModels;

public partial class GitFilesViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IFilePreviewService _filePreviewService;
    private TerminalPairTabViewModel? _currentTerminalTab; // Context from MainViewModel
    private readonly DiffHighlighter _diffHighlighter = new();

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _gitFiles = new();

    [ObservableProperty]
    private GitFileStatus? _selectedGitFile;

    [ObservableProperty]
    private FlowDocument _diffDocument = new();

    [ObservableProperty]
    private string _title = "Git Changes";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDragging; // For popup dragging
    
    // View properties for positioning/sizing the popup (similar to DetectedLinksViewModel)
    [ObservableProperty]
    private double _width = 1100;
    
    [ObservableProperty]
    private double _height = 700;
    
    [ObservableProperty]
    private double _horizontalOffset;
    
    [ObservableProperty]
    private double _verticalOffset;

    public GitFilesViewModel(IGitStatusService gitStatusService, IFilePreviewService filePreviewService)
    {
        _gitStatusService = gitStatusService;
        _filePreviewService = filePreviewService;
        // Initialize with an empty document to avoid null reference in XAML
        _diffDocument = CreateInfoDocument("Select a file to view diff");
    }

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        if (terminalTab.GitStatus?.IsGitRepository != true)
        {
            DialogService.ShowInfo(
                "The selected tab is not a Git repository or Git status is unavailable.",
                "Git Changes");
            _currentTerminalTab = null; // Clear context if not a git repo
            return;
        }

        Title = $"Git Changes - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshGitFilesAsync();

        // Calculate initial offset if needed, or rely on XAML placement
        // For now, let XAML handle initial placement, or MainWindow.xaml.cs might override
        
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SelectedGitFile = null;
        // Clear diff document
        DiffDocument = CreateInfoDocument("Select a file to view diff");
        _currentTerminalTab = null; // Clear context
    }

    [RelayCommand]
    private async Task RefreshGitFilesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            GitFiles.Clear();
            IsEmptyStateVisible = true;
            SelectedGitFile = null;
            DiffDocument = CreateInfoDocument("No Git repository selected.");
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var files = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        GitFiles = new ObservableCollection<GitFileStatus>(files);
        IsEmptyStateVisible = !GitFiles.Any();

        // Clear selection and diff
        SelectedGitFile = null;
        DiffDocument = CreateInfoDocument(IsEmptyStateVisible ? "No changes to display." : "Select a file to view diff.");

        // Auto-select first file if any
        if (GitFiles.Any())
        {
            SelectedGitFile = GitFiles.First();
        }
    }

    partial void OnSelectedGitFileChanged(GitFileStatus? value)
    {
        UpdateButtonsEnabledState();
        LoadDiffForSelectedFileAsync(value);
    }

    private async void LoadDiffForSelectedFileAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            DiffDocument = CreateInfoDocument("Select a file to view diff.");
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var diff = await _gitStatusService.GetFileDiffAsync(workingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            DiffDocument = _diffHighlighter.CreateHighlightedDocument(diff, null);
        }
        else
        {
            DiffDocument = CreateInfoDocument("No changes to display.");
        }
    }

    private void UpdateButtonsEnabledState()
    {
        PreviewFileCommand.NotifyCanExecuteChanged();
        EditFileCommand.NotifyCanExecuteChanged();
        ExploreFileCommand.NotifyCanExecuteChanged();
    }

    private static FlowDocument CreateInfoDocument(string message)
    {
        return new FlowDocument
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            PageWidth = 10000 // Effectively disables wrapping for diffs
        };
    }

    public bool CanPreviewFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted;
    [RelayCommand(CanExecute = nameof(CanPreviewFile))]
    private void PreviewFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = fullPath });
        Close();
    }

    public bool CanEditFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted;
    [RelayCommand(CanExecute = nameof(CanEditFile))]
    private void EditFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FileEditRequested?.Invoke(this, new FileEditRequestedEventArgs { FilePath = fullPath });
        Close();
    }

    public bool CanExploreFile => SelectedGitFile != null;
    [RelayCommand(CanExecute = nameof(CanExploreFile))]
    private void ExploreFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (System.IO.Directory.Exists(directory))
        {
            // Open explorer and select the file if it exists
            if (System.IO.File.Exists(fullPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", directory);
            }
        }
    }

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;
}
