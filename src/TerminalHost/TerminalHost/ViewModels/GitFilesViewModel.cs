using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Git Changes panel (Ctrl+G).
/// Supports Panel, Popup, and Window display states.
/// Provides interactive staging, unstaging, and commit functionality.
/// </summary>
public partial class GitFilesViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private TerminalPairTabViewModel? _currentTerminalTab;

    #region IPanelableViewModel Implementation

    public override string PanelId => "gitChanges";
    public override string PanelTitle => "Git Changes";
    public override string PanelIcon => "\u0394"; // Δ (Delta symbol)
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "📦",
            Tooltip = "Stash changes (Ctrl+Shift+S for stash manager)",
            Command = QuickStashCommand
        },
        new PanelHeaderCommand
        {
            Icon = "↻",
            Tooltip = "Refresh file list",
            Command = RefreshGitFilesCommand
        }
    ];

    #endregion

    #region Git Properties

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
    private bool _isDragging;

    [ObservableProperty]
    private bool _isLoading;

    #endregion

    #region Commit Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubjectLength))]
    [NotifyPropertyChangedFor(nameof(IsSubjectTooLong))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommitCommand))]
    private string _commitMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommitCommand))]
    private bool _amendCommit;

    public int SubjectLength => CommitMessage.Split('\n').FirstOrDefault()?.Length ?? 0;
    public bool IsSubjectTooLong => SubjectLength > 72;
    public bool HasStagedFiles => StagedFiles.Count > 0;

    #endregion

    #region Events

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;

    #endregion

    public GitFilesViewModel(
        IGitStatusService gitStatusService,
        IFilePreviewService filePreviewService,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IProcessService processService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _filePreviewService = filePreviewService;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _toastService = toastService;

        // Set defaults for git changes - defaults to Popup
        DisplayState = PanelDisplayState.Popup;
        Width = 1100;
        Height = 700;
    }

    #region Overrides

    protected override void OnClose()
    {
        SelectedGitFile = null;
        DiffText = "";
        CommitMessage = "";
        AmendCommit = false;
        _currentTerminalTab = null;
        base.OnClose();
    }

    #endregion

    #region Commands

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        if (terminalTab.GitStatus?.IsGitRepository != true)
        {
            _dialogService.ShowInfo(
                "The selected tab is not a Git repository or Git status is unavailable.",
                "Git Changes");
            return;
        }

        await LoadDataAsync(terminalTab);

        // Request to be shown in the appropriate mode
        RequestShow();
    }

    /// <summary>
    /// Loads git files data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        Title = $"Git Changes - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshGitFilesAsync();
    }

    [RelayCommand]
    private void Close()
    {
        OnClose();
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
            OnPropertyChanged(nameof(HasStagedFiles));
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var files = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        GitFiles = new ObservableCollection<GitFileStatus>(files);
        StagedFiles = new ObservableCollection<GitFileStatus>(files.Where(f => f.IsStaged));
        UnstagedFiles = new ObservableCollection<GitFileStatus>(files.Where(f => !f.IsStaged));
        IsEmptyStateVisible = !GitFiles.Any();

        SelectedGitFile = null;
        DiffText = "";

        // Preserve selection if possible, otherwise select first file
        if (GitFiles.Any())
        {
            SelectedGitFile = UnstagedFiles.FirstOrDefault() ?? StagedFiles.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasStagedFiles));
        UpdateStagingButtonsState();
    }

    [RelayCommand]
    private async Task QuickStashAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
            return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        try
        {
            var result = await _gitStatusService.CreateStashAsync(workingDirectory);

            if (result.Success)
            {
                _toastService.Show("Changes stashed", ToastType.Success);
                await RefreshGitFilesAsync();

                // Also refresh the terminal tab's git status
                var status = await _gitStatusService.GetGitStatusAsync(workingDirectory);
                _currentTerminalTab.GitStatus = status;
            }
            else
            {
                var error = result.Error ?? "Unknown error";
                if (error.Contains("No local changes to save"))
                {
                    _toastService.Show("No changes to stash", ToastType.Warning);
                }
                else
                {
                    _dialogService.ShowWarning($"Failed to stash changes:\n{error}", "Git Stash");
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to stash changes: {ex.Message}", "Git Stash");
        }
    }

    public bool CanPreviewFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted && !SelectedGitFile.IsSubmodule;

    [RelayCommand(CanExecute = nameof(CanPreviewFile))]
    private void PreviewFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs { FilePath = fullPath });
        OnClose();
    }

    public bool CanEditFile => SelectedGitFile != null && SelectedGitFile.Status != GitFileStatusType.Deleted && !SelectedGitFile.IsSubmodule;

    [RelayCommand(CanExecute = nameof(CanEditFile))]
    private void EditFile()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var fullPath = System.IO.Path.Combine(_currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);
        FileEditRequested?.Invoke(this, new FileEditRequestedEventArgs { FilePath = fullPath });
        OnClose();
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
                _processService.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else
            {
                _processService.Start("explorer.exe", directory!);
            }
        }
    }

    #endregion

    #region Staging Commands

    public bool CanStageFile => SelectedGitFile != null && !SelectedGitFile.IsStaged;

    [RelayCommand(CanExecute = nameof(CanStageFile))]
    private async Task StageFileAsync()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.StageFileAsync(
                _currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);

            if (result.Success)
            {
                _toastService.Show($"Staged: {SelectedGitFile.FileName}", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to stage: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanUnstageFile => SelectedGitFile != null && SelectedGitFile.IsStaged;

    [RelayCommand(CanExecute = nameof(CanUnstageFile))]
    private async Task UnstageFileAsync()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UnstageFileAsync(
                _currentTerminalTab.Pair.WorkingDirectory, SelectedGitFile.FilePath);

            if (result.Success)
            {
                _toastService.Show($"Unstaged: {SelectedGitFile.FileName}", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to unstage: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanStageAll => UnstagedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStageAll))]
    private async Task StageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.StageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);

            if (result.Success)
            {
                _toastService.Show($"Staged all {UnstagedFiles.Count} files", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to stage all: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanUnstageAll => StagedFiles.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUnstageAll))]
    private async Task UnstageAllAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UnstageAllAsync(_currentTerminalTab.Pair.WorkingDirectory);

            if (result.Success)
            {
                _toastService.Show($"Unstaged all {StagedFiles.Count} files", ToastType.Success);
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Failed to unstage all: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanDiscardChanges => SelectedGitFile != null && !SelectedGitFile.IsStaged;

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private async Task DiscardChangesAsync()
    {
        if (SelectedGitFile == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;

        var confirmed = _dialogService.ShowConfirmation(
            $"Are you sure you want to discard changes to '{SelectedGitFile.FileName}'?\n\nThis cannot be undone.",
            "Discard Changes");

        if (!confirmed) return;

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var filePath = SelectedGitFile.FilePath;

            // For untracked files, delete the file
            if (SelectedGitFile.Status == GitFileStatusType.Untracked)
            {
                var fullPath = System.IO.Path.Combine(workingDirectory, filePath);
                if (_fileSystem.FileExists(fullPath))
                {
                    _fileSystem.DeleteFile(fullPath);
                    _toastService.Show($"Deleted: {SelectedGitFile.FileName}", ToastType.Success);
                }
            }
            else
            {
                var result = await _gitStatusService.DiscardChangesAsync(workingDirectory, filePath);

                if (result.Success)
                {
                    _toastService.Show($"Discarded changes: {SelectedGitFile.FileName}", ToastType.Success);
                }
                else
                {
                    _toastService.Show($"Failed to discard: {result.Error}", ToastType.Error);
                }
            }

            await RefreshGitFilesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Commit Commands

    public bool CanCreateCommit => HasStagedFiles && !string.IsNullOrWhiteSpace(CommitMessage);

    [RelayCommand(CanExecute = nameof(CanCreateCommit))]
    private async Task CreateCommitAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateCommitAsync(
                _currentTerminalTab.Pair.WorkingDirectory,
                CommitMessage,
                AmendCommit);

            if (result.Success)
            {
                _toastService.Show(AmendCommit ? "Commit amended" : "Commit created", ToastType.Success);
                CommitMessage = "";
                AmendCommit = false;
                await RefreshGitFilesAsync();
            }
            else
            {
                _toastService.Show($"Commit failed: {result.Error}", ToastType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void InsertConventionalPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(CommitMessage) || !CommitMessage.Contains(':'))
        {
            CommitMessage = $"{prefix}: {CommitMessage}";
        }
    }

    #endregion

    #region Event Handlers

    partial void OnSelectedGitFileChanged(GitFileStatus? value)
    {
        UpdateButtonsEnabledState();
        LoadDiffForSelectedFileAsync(value);
    }

    #endregion

    #region Private Methods

    private async void LoadDiffForSelectedFileAsync(GitFileStatus? file)
    {
        if (file == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            DiffText = "";
            return;
        }

        // Handle submodules specially - don't try to load a diff as it can hang
        if (file.IsSubmodule)
        {
            DiffText = $"Submodule: {file.FilePath}\n\nDiff preview is not available for submodules.\n\nTo view submodule changes, navigate to the submodule directory\nand use git commands directly.";
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
        UpdateStagingButtonsState();
    }

    private void UpdateStagingButtonsState()
    {
        StageFileCommand.NotifyCanExecuteChanged();
        UnstageFileCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        CreateCommitCommand.NotifyCanExecuteChanged();
    }

    #endregion
}
