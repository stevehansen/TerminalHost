using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for File History panel.
/// Shows all commits that modified a specific file.
/// </summary>
public partial class FileHistoryViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private const int DefaultCommitCount = 50;
    private const int LoadMoreCount = 25;

    #region IPanelableViewModel Implementation

    public override string PanelId => "fileHistory";
    public override string PanelTitle => "File History";
    public override string PanelIcon => "📜";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    #endregion

    #region Properties

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _title = "File History";

    [ObservableProperty]
    private ObservableCollection<GitCommit> _commits = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCommit))]
    [NotifyCanExecuteChangedFor(nameof(CopyHashCommand))]
    [NotifyCanExecuteChangedFor(nameof(ViewFileAtCommitCommand))]
    private GitCommit? _selectedCommit;

    [ObservableProperty]
    private string _diffText = "";

    [ObservableProperty]
    private string _fileContentAtCommit = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingDiff;

    [ObservableProperty]
    private bool _showFileContent;

    public bool HasSelectedCommit => SelectedCommit != null;
    public bool HasCommits => Commits.Count > 0;

    #endregion

    #region Events

    /// <summary>
    /// Raised when user requests to view file content at a specific commit.
    /// </summary>
    public event EventHandler<FileAtCommitRequestedEventArgs>? FileAtCommitRequested;

    #endregion

    public FileHistoryViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;

        // Set defaults - defaults to Panel
        DisplayState = PanelDisplayState.Panel;
        Width = 1000;
        Height = 700;
    }

    #region Overrides

    protected override void OnClose()
    {
        SelectedCommit = null;
        DiffText = "";
        FileContentAtCommit = "";
        ShowFileContent = false;
        WorkingDirectory = "";
        FilePath = "";
        FileName = "";
        Commits.Clear();
        base.OnClose();
    }

    #endregion

    #region Commands

    public async Task OpenAsync(string workingDirectory, string filePath)
    {
        WorkingDirectory = workingDirectory;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Title = $"History: {FileName}";

        await RefreshCommitsAsync();
        RequestShow();
    }

    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    [RelayCommand]
    private async Task RefreshCommitsAsync()
    {
        if (string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
        {
            Commits.Clear();
            return;
        }

        IsLoading = true;
        try
        {
            // Use existing GetCommitHistoryAsync with file path - it already supports --follow
            var commits = await _gitStatusService.GetCommitHistoryAsync(
                WorkingDirectory,
                DefaultCommitCount,
                author: null,
                filePath: FilePath);

            Commits = new ObservableCollection<GitCommit>(commits);
            OnPropertyChanged(nameof(HasCommits));

            // Select first commit
            if (Commits.Count > 0)
            {
                SelectedCommit = Commits[0];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreCommitsAsync()
    {
        if (string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
            return;

        IsLoading = true;
        try
        {
            var totalCount = Commits.Count + LoadMoreCount;
            var commits = await _gitStatusService.GetCommitHistoryAsync(
                WorkingDirectory,
                totalCount,
                author: null,
                filePath: FilePath);

            // Add new commits that aren't already in the list
            foreach (var commit in commits.Skip(Commits.Count))
            {
                Commits.Add(commit);
            }
            OnPropertyChanged(nameof(HasCommits));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanCopyHash => SelectedCommit != null;

    [RelayCommand(CanExecute = nameof(CanCopyHash))]
    private void CopyHash()
    {
        if (SelectedCommit == null) return;

        try
        {
            System.Windows.Clipboard.SetText(SelectedCommit.Hash);
            _toastService.Show("Hash copied to clipboard", ToastType.Success);
        }
        catch
        {
            _toastService.Show("Failed to copy hash", ToastType.Error);
        }
    }

    public bool CanViewFileAtCommit => SelectedCommit != null;

    [RelayCommand(CanExecute = nameof(CanViewFileAtCommit))]
    private async Task ViewFileAtCommitAsync()
    {
        if (SelectedCommit == null || string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
            return;

        IsLoadingDiff = true;
        try
        {
            var content = await _gitStatusService.GetFileContentAtCommitAsync(
                WorkingDirectory,
                FilePath,
                SelectedCommit.Hash);

            if (content != null)
            {
                FileContentAtCommit = content;
                ShowFileContent = true;

                // Also raise event for external handling if needed
                FileAtCommitRequested?.Invoke(this, new FileAtCommitRequestedEventArgs
                {
                    FilePath = FilePath,
                    CommitHash = SelectedCommit.Hash,
                    Content = content
                });
            }
            else
            {
                _toastService.Show("Could not retrieve file content", ToastType.Warning);
            }
        }
        finally
        {
            IsLoadingDiff = false;
        }
    }

    [RelayCommand]
    private void ShowDiffView()
    {
        ShowFileContent = false;
    }

    #endregion

    #region Event Handlers

    partial void OnSelectedCommitChanged(GitCommit? value)
    {
        LoadCommitDiffAsync(value);
    }

    #endregion

    #region Private Methods

    private async void LoadCommitDiffAsync(GitCommit? commit)
    {
        if (commit == null || string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
        {
            DiffText = "";
            return;
        }

        IsLoadingDiff = true;
        ShowFileContent = false;
        try
        {
            // Get diff for this specific file in this commit
            var diff = await _gitStatusService.GetCommitDiffAsync(
                WorkingDirectory,
                commit.Hash,
                FilePath);

            DiffText = diff ?? "";
        }
        finally
        {
            IsLoadingDiff = false;
        }
    }

    #endregion
}

/// <summary>
/// Event args for file at commit request.
/// </summary>
public class FileAtCommitRequestedEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public required string CommitHash { get; init; }
    public required string Content { get; init; }
}
