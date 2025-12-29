using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Commit History panel (Ctrl+H).
/// Supports Panel, Popup, and Window display states.
/// </summary>
public partial class CommitHistoryViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private TerminalPairTabViewModel? _currentTerminalTab;
    private const int DefaultCommitCount = 50;
    private const int LoadMoreCount = 25;

    #region IPanelableViewModel Implementation

    public override string PanelId => "commitHistory";
    public override string PanelTitle => "Commit History";
    public override string PanelIcon => "⏱"; // Clock symbol
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    #endregion

    #region Properties

    [ObservableProperty]
    private ObservableCollection<GitCommit> _commits = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCommit))]
    [NotifyCanExecuteChangedFor(nameof(CopyHashCommand))]
    private GitCommit? _selectedCommit;

    [ObservableProperty]
    private GitCommitDetails? _commitDetails;

    [ObservableProperty]
    private GitCommitFile? _selectedFile;

    [ObservableProperty]
    private string _diffText = "";

    [ObservableProperty]
    private string _title = "Commit History";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _authorFilter = "";

    public bool HasSelectedCommit => SelectedCommit != null;
    public bool HasCommits => Commits.Count > 0;

    #endregion

    public CommitHistoryViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;

        // Set defaults - defaults to Popup
        DisplayState = PanelDisplayState.Popup;
        Width = 1200;
        Height = 750;
    }

    #region Overrides

    protected override void OnClose()
    {
        SelectedCommit = null;
        CommitDetails = null;
        SelectedFile = null;
        DiffText = "";
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
                "Commit History");
            return;
        }

        await LoadDataAsync(terminalTab);
        RequestShow();
    }

    /// <summary>
    /// Loads commit history data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        Title = $"Commit History - {terminalTab.Title}";
        Info = terminalTab.Pair.WorkingDirectory;

        await RefreshCommitsAsync();
    }

    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    [RelayCommand]
    private async Task RefreshCommitsAsync()
    {
        if (_currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            Commits.Clear();
            return;
        }

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var author = string.IsNullOrWhiteSpace(AuthorFilter) ? null : AuthorFilter;
            var commits = await _gitStatusService.GetCommitHistoryAsync(workingDirectory, DefaultCommitCount, author);

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
        if (_currentTerminalTab?.Pair.WorkingDirectory == null) return;

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var author = string.IsNullOrWhiteSpace(AuthorFilter) ? null : AuthorFilter;
            var totalCount = Commits.Count + LoadMoreCount;
            var commits = await _gitStatusService.GetCommitHistoryAsync(workingDirectory, totalCount, author);

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

    [RelayCommand]
    private async Task ApplyAuthorFilterAsync()
    {
        await RefreshCommitsAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        AuthorFilter = "";
        SearchText = "";
        await RefreshCommitsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCopyHash))]
    private async Task CherryPickAsync()
    {
        if (SelectedCommit == null || _currentTerminalTab == null) return;

        var confirm = _dialogService.ShowConfirmation(
            $"Cherry-pick commit {SelectedCommit.ShortHash}?\n\n{SelectedCommit.Subject}",
            "Cherry-pick Commit");

        if (!confirm) return;

        var workDir = _currentTerminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.CherryPickAsync(workDir, SelectedCommit.Hash);

        if (result.Success)
        {
            _toastService.Show($"Cherry-picked {SelectedCommit.ShortHash}", ToastType.Success);
        }
        else
        {
            _toastService.Show($"Cherry-pick failed: {result.Error}", ToastType.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyHash))]
    private async Task RevertCommitAsync()
    {
        if (SelectedCommit == null || _currentTerminalTab == null) return;

        var confirm = _dialogService.ShowConfirmation(
            $"Revert commit {SelectedCommit.ShortHash}?\n\n{SelectedCommit.Subject}\n\nThis will create a new commit that undoes the changes.",
            "Revert Commit");

        if (!confirm) return;

        var workDir = _currentTerminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.RevertAsync(workDir, SelectedCommit.Hash);

        if (result.Success)
        {
            _toastService.Show($"Reverted {SelectedCommit.ShortHash}", ToastType.Success);
            await RefreshCommitsAsync();
        }
        else
        {
            _toastService.Show($"Revert failed: {result.Error}", ToastType.Error);
        }
    }

    #endregion

    #region Event Handlers

    partial void OnSelectedCommitChanged(GitCommit? value)
    {
        LoadCommitDetailsAsync(value);
    }

    partial void OnSelectedFileChanged(GitCommitFile? value)
    {
        LoadFileDiffAsync(value);
    }

    #endregion

    #region Private Methods

    private async void LoadCommitDetailsAsync(GitCommit? commit)
    {
        if (commit == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            CommitDetails = null;
            SelectedFile = null;
            DiffText = "";
            return;
        }

        IsLoadingDetails = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            CommitDetails = await _gitStatusService.GetCommitDetailsAsync(workingDirectory, commit.Hash);

            // Select first file for diff
            if (CommitDetails?.Files.Count > 0)
            {
                SelectedFile = CommitDetails.Files[0];
            }
            else
            {
                SelectedFile = null;
                // Load full commit diff if no files
                DiffText = await _gitStatusService.GetCommitDiffAsync(workingDirectory, commit.Hash) ?? "";
            }
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    private async void LoadFileDiffAsync(GitCommitFile? file)
    {
        if (file == null || SelectedCommit == null || _currentTerminalTab?.Pair.WorkingDirectory == null)
        {
            DiffText = "";
            return;
        }

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
        var diff = await _gitStatusService.GetCommitDiffAsync(workingDirectory, SelectedCommit.Hash, file.FilePath);
        DiffText = diff ?? "";
    }

    #endregion
}
