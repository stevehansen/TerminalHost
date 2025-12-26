using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitFilesViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private TerminalPairTabViewModel? _currentTerminalTab;

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _gitFiles = [];

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _stagedFiles = [];

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _unstagedFiles = [];

    [ObservableProperty]
    private GitFileStatus? _selectedGitFile;

    [ObservableProperty]
    private string _diffText = "";

    [ObservableProperty]
    private string _title = "Git Changes";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isEmptyStateVisible;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private double _width = 1100;

    [ObservableProperty]
    private double _height = 700;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _commitMessage = "";

    [ObservableProperty]
    private bool _isAmend;

    [ObservableProperty]
    private int _commitMessageLength;

    private const int MaxCommitMessageLength = 72; // Conventional max for first line

    public GitFilesViewModel(IGitStatusService gitStatusService, IFilePreviewService filePreviewService, IDialogService dialogService, IFileSystem fileSystem, IProcessService processService, IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _filePreviewService = filePreviewService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _toastService = toastService;
        _diffText = "";
    }

    partial void OnCommitMessageChanged(string value)
    {
        // Get first line length for the counter
        var firstLineEnd = value.IndexOf('\n');
        CommitMessageLength = firstLineEnd >= 0 ? firstLineEnd : value.Length;
    }

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        if (terminalTab.GitStatus?.IsGitRepository != true)
        {
            _dialogService.ShowInfo( 
                "The selected tab is not a Git repository or Git status is unavailable.",
                "Git Changes");
            _currentTerminalTab = null; 
            return;
        }

        Title = $"Git Changes - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshGitFilesAsync();
        
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SelectedGitFile = null;
        DiffText = "";
        CommitMessage = "";
        IsAmend = false;
        _currentTerminalTab = null;
    }

    [RelayCommand]
    private async Task RefreshGitFilesAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            GitFiles.Clear();
            StagedFiles.Clear();
            UnstagedFiles.Clear();
            IsEmptyStateVisible = true;
            SelectedGitFile = null;
            DiffText = "";
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var (staged, unstaged) = await _gitStatusService.GetStagedAndUnstagedFilesAsync(workingDirectory);

        StagedFiles = new ObservableCollection<GitFileStatus>(staged);
        UnstagedFiles = new ObservableCollection<GitFileStatus>(unstaged);

        // Also update combined list for compatibility
        var allFiles = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);
        GitFiles = new ObservableCollection<GitFileStatus>(allFiles);

        IsEmptyStateVisible = !StagedFiles.Any() && !UnstagedFiles.Any();

        // Update command states
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();

        SelectedGitFile = null;
        DiffText = "";

        // Select first unstaged file if any, otherwise first staged
        if (UnstagedFiles.Any())
        {
            SelectedGitFile = UnstagedFiles.First();
        }
        else if (StagedFiles.Any())
        {
            SelectedGitFile = StagedFiles.First();
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
            DiffText = "";
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var diff = await _gitStatusService.GetFileDiffAsync(workingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            DiffText = diff;
        }
        else
        {
            DiffText = "";
        }
    }

    private void UpdateButtonsEnabledState()
    {
        PreviewFileCommand.NotifyCanExecuteChanged();
        EditFileCommand.NotifyCanExecuteChanged();
        ExploreFileCommand.NotifyCanExecuteChanged();
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

        if (_fileSystem.DirectoryExists(directory))
        {
            if (_fileSystem.FileExists(fullPath))
            {
                _processService.RevealInFinder(fullPath);
            }
            else
            {
                _processService.OpenFolder(directory!);
            }
        }
    }

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;

    #region Staging Commands

    [RelayCommand]
    private async Task StageFileAsync(GitFileStatus file)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var success = await _gitStatusService.StageFileAsync(_currentTerminalTab.Pair.WorkingDirectory, file.FilePath);
        if (success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show($"Failed to stage {file.FileName}", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task UnstageFileAsync(GitFileStatus file)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var success = await _gitStatusService.UnstageFileAsync(_currentTerminalTab.Pair.WorkingDirectory, file.FilePath);
        if (success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show($"Failed to unstage {file.FileName}", ToastType.Error);
        }
    }

    public bool CanStageAll => UnstagedFiles.Any();
    [RelayCommand(CanExecute = nameof(CanStageAll))]
    private async Task StageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var success = await _gitStatusService.StageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);
        if (success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show("Failed to stage all files", ToastType.Error);
        }
    }

    public bool CanUnstageAll => StagedFiles.Any();
    [RelayCommand(CanExecute = nameof(CanUnstageAll))]
    private async Task UnstageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var success = await _gitStatusService.UnstageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);
        if (success)
        {
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show("Failed to unstage all files", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task DiscardChangesAsync(GitFileStatus file)
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        // Confirm discard
        var confirmed = _dialogService.ShowConfirmation(
            $"Discard changes to {file.FileName}?\n\nThis will permanently delete your changes.",
            "Discard Changes");

        if (!confirmed) return;

        var success = await _gitStatusService.DiscardChangesAsync(_currentTerminalTab.Pair.WorkingDirectory, file.FilePath);
        if (success)
        {
            _toastService.Show($"Discarded changes to {file.FileName}", ToastType.Success);
            await RefreshGitFilesAsync();
        }
        else
        {
            _toastService.Show($"Failed to discard changes to {file.FileName}", ToastType.Error);
        }
    }

    #endregion

    #region Commit Commands

    public bool CanCommit => StagedFiles.Any() && (!string.IsNullOrWhiteSpace(CommitMessage) || IsAmend);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        using var toast = _toastService.ShowProgress("Creating commit...");

        var (success, error) = await _gitStatusService.CommitAsync(
            _currentTerminalTab.Pair.WorkingDirectory,
            CommitMessage,
            IsAmend);

        if (success)
        {
            toast.Complete("Commit created");
            CommitMessage = "";
            IsAmend = false;
            await RefreshGitFilesAsync();
        }
        else
        {
            toast.Fail(error ?? "Commit failed");
        }
    }

    [RelayCommand]
    private void InsertCommitPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(CommitMessage))
        {
            CommitMessage = $"{prefix}: ";
        }
        else if (!CommitMessage.StartsWith($"{prefix}:"))
        {
            // Replace existing prefix if any
            var colonIndex = CommitMessage.IndexOf(':');
            if (colonIndex > 0 && colonIndex < 15) // Assume prefix is short
            {
                CommitMessage = $"{prefix}:{CommitMessage.Substring(colonIndex + 1)}";
            }
            else
            {
                CommitMessage = $"{prefix}: {CommitMessage}";
            }
        }
    }

    #endregion
}
