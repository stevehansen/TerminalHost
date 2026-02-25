using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class CommitHistoryViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;
    private readonly IConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly IAiExecutionService _aiExecutionService;

    private const int PageSize = 50;
    private int _currentPage;
    private bool _hasMoreCommits = true;
    private string _currentWorkingDirectory = "";

    [ObservableProperty]
    private ObservableCollection<GitCommit> _commits = [];

    [ObservableProperty]
    private GitCommit? _selectedCommit;

    [ObservableProperty]
    private GitCommitDetails? _commitDetails;

    [ObservableProperty]
    private GitCommitFile? _selectedFile;

    [ObservableProperty]
    private string _fileDiff = "";

    [ObservableProperty]
    private string _title = "Commit History";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private double _width = 1200;

    [ObservableProperty]
    private double _height = 750;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    // Advanced filter properties
    [ObservableProperty]
    private string? _authorFilter;

    [ObservableProperty]
    private string? _filePathFilter;

    [ObservableProperty]
    private bool _showFilters;

    // AI explain properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAiExplanation))]
    private string? _aiExplanation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommitCommand))]
    private bool _isAiExplaining;

    public bool HasAiExplanation => !string.IsNullOrEmpty(AiExplanation);
    public bool CanExplainCommit => SelectedCommit != null && !IsAiExplaining;

    public CommitHistoryViewModel(
        IGitStatusService gitStatusService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IToastService toastService,
        IConfigurationService configurationService,
        IProcessService processService,
        IAiExecutionService aiExecutionService)
    {
        _gitStatusService = gitStatusService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _toastService = toastService;
        _configurationService = configurationService;
        _processService = processService;
        _aiExecutionService = aiExecutionService;
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            StatusMessage = "Please select a project tab first.";
            IsOpen = true;
            return;
        }

        if (terminalTab.GitStatus?.IsGitRepository != true)
        {
            _dialogService.ShowInfo(
                "The selected tab is not a Git repository.",
                "Commit History");
            return;
        }

        await LoadDataAsync(terminalTab);

        IsOpen = true;
    }

    /// <summary>
    /// Loads commit history data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Commit History - {terminalTab.Title}";
        StatusMessage = "";
        SearchText = "";
        _currentPage = 0;
        _hasMoreCommits = true;

        Commits.Clear();
        CommitDetails = null;
        SelectedCommit = null;
        SelectedFile = null;
        FileDiff = "";

        await LoadCommitsAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Commits.Clear();
        CommitDetails = null;
        SelectedCommit = null;
        SelectedFile = null;
        FileDiff = "";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _currentPage = 0;
        _hasMoreCommits = true;
        Commits.Clear();
        CommitDetails = null;
        SelectedCommit = null;
        SelectedFile = null;
        FileDiff = "";
        await LoadCommitsAsync();
    }

    private async Task LoadCommitsAsync()
    {
        if (string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var author = string.IsNullOrWhiteSpace(AuthorFilter) ? null : AuthorFilter;
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
            var fileFilter = string.IsNullOrWhiteSpace(FilePathFilter) ? null : FilePathFilter;
            // Core.IGitStatusService uses count parameter instead of skip/take
            // Load all needed commits and skip manually
            var totalNeeded = (_currentPage + 1) * PageSize;
            var allCommits = await _gitStatusService.GetCommitHistoryAsync(
                _currentWorkingDirectory,
                count: totalNeeded,
                author: author,
                filePath: fileFilter,
                searchText: search);
            var commits = allCommits.Skip(_currentPage * PageSize).Take(PageSize).ToList();

            if (commits.Count < PageSize)
                _hasMoreCommits = false;

            foreach (var commit in commits)
            {
                Commits.Add(commit);
            }

            StatusMessage = Commits.Count == 0 ? "No commits found." : "";

            // Select first commit if none selected
            if (SelectedCommit == null && Commits.Count > 0)
            {
                SelectedCommit = Commits[0];
            }

            LoadMoreCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanLoadMore => _hasMoreCommits && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        _currentPage++;
        await LoadCommitsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        // Debounce search - for simplicity, we'll trigger on explicit refresh
        // Could add a timer here for auto-search
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RefreshAsync();
    }

    partial void OnSelectedCommitChanged(GitCommit? value)
    {
        AiExplanation = null;

        if (value != null)
        {
            LoadCommitDetailsAsync(value);
        }
        else
        {
            CommitDetails = null;
            SelectedFile = null;
            FileDiff = "";
        }

        CopyHashCommand.NotifyCanExecuteChanged();
        ExplainCommitCommand.NotifyCanExecuteChanged();
    }

    private async void LoadCommitDetailsAsync(GitCommit commit)
    {
        if (string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        IsLoadingDetails = true;
        SelectedFile = null;
        FileDiff = "";

        try
        {
            CommitDetails = await _gitStatusService.GetCommitDetailsAsync(
                _currentWorkingDirectory,
                commit.Hash);

            // Auto-select first file if available
            if (CommitDetails?.Files.Count > 0)
            {
                SelectedFile = CommitDetails.Files[0];
            }
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    partial void OnSelectedFileChanged(GitCommitFile? value)
    {
        if (value != null && SelectedCommit != null)
        {
            LoadFileDiffAsync(SelectedCommit.Hash, value.FilePath);
        }
        else
        {
            FileDiff = "";
        }
    }

    private async void LoadFileDiffAsync(string commitHash, string filePath)
    {
        if (string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        try
        {
            // GetFileDiffInCommitAsync doesn't exist in Core - use GetCommitDiffAsync with filePath
            var diff = await _gitStatusService.GetCommitDiffAsync(
                _currentWorkingDirectory,
                commitHash,
                filePath);

            FileDiff = diff ?? "";
        }
        catch
        {
            FileDiff = "";
        }
    }

    public bool CanCopyHash => SelectedCommit != null;

    [RelayCommand(CanExecute = nameof(CanCopyHash))]
    private async Task CopyHashAsync()
    {
        if (SelectedCommit == null) return;

        await _clipboardService.SetTextAsync(SelectedCommit.Hash);
        _toastService.Show("Commit hash copied", ToastType.Success);
    }

    [RelayCommand]
    private async Task CopyShortHashAsync()
    {
        if (SelectedCommit == null) return;

        await _clipboardService.SetTextAsync(SelectedCommit.ShortHash);
        _toastService.Show("Short hash copied", ToastType.Success);
    }

    #region Filters

    [RelayCommand]
    private void ToggleFilters() => ShowFilters = !ShowFilters;

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        AuthorFilter = null;
        FilePathFilter = null;
        SearchText = "";
        await RefreshAsync();
    }

    #endregion

    #region AI Explain

    [RelayCommand(CanExecute = nameof(CanExplainCommit))]
    private async Task ExplainCommitAsync()
    {
        if (SelectedCommit == null || string.IsNullOrEmpty(_currentWorkingDirectory)) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        IsAiExplaining = true;
        try
        {
            var diff = await _gitStatusService.GetCommitDiffAsync(_currentWorkingDirectory, SelectedCommit.Hash);
            if (string.IsNullOrEmpty(diff))
            {
                _toastService.Show("No diff available for this commit", ToastType.Warning);
                return;
            }

            if (diff.Length > 20000) diff = diff[..20000];

            var prompt = $"Explain this commit in 2-4 sentences. What changed and why (infer intent from context and message). Plain English, no markdown.\n\nMessage: {SelectedCommit.Subject}\n\n{diff}";
            var result = await _aiExecutionService.ExecuteAsync(prompt, _currentWorkingDirectory, "Explaining commit", timeout: TimeSpan.FromSeconds(60));

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                AiExplanation = result.Output.Trim();
        }
        finally
        {
            IsAiExplaining = false;
        }
    }

    [RelayCommand]
    private void DismissAiExplanation() => AiExplanation = null;

    [RelayCommand]
    private async Task CopyAiExplanationAsync()
    {
        if (!string.IsNullOrEmpty(AiExplanation))
        {
            await _clipboardService.SetTextAsync(AiExplanation);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }

    #endregion

    #region Cherry-pick and Revert

    public bool CanCherryPickOrRevert => SelectedCommit != null;

    [RelayCommand(CanExecute = nameof(CanCherryPickOrRevert))]
    private async Task CherryPickAsync()
    {
        if (SelectedCommit == null || string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        var confirm = _dialogService.ShowConfirmation(
            $"Cherry-pick commit {SelectedCommit.ShortHash}?\n\n" +
            $"This will apply the changes from this commit to your current branch.\n\n" +
            $"Subject: {SelectedCommit.Subject}",
            "Cherry-pick Commit");

        if (!confirm) return;

        try
        {
            var result = await _gitStatusService.CherryPickAsync(_currentWorkingDirectory, SelectedCommit.Hash);

            if (result.Success)
            {
                _toastService.Show($"Cherry-picked {SelectedCommit.ShortHash}", ToastType.Success);
                await RefreshAsync();
            }
            else
            {
                if (result.Error?.Contains("conflict") == true)
                {
                    var action = _dialogService.ShowConfirmation(
                        "Cherry-pick resulted in conflicts.\n\n" +
                        "Would you like to abort the cherry-pick?",
                        "Conflicts Detected");

                    if (action)
                    {
                        await _gitStatusService.CherryPickAbortAsync(_currentWorkingDirectory);
                        _toastService.Show("Cherry-pick aborted", ToastType.Warning);
                    }
                    else
                    {
                        _toastService.Show("Resolve conflicts and commit manually", ToastType.Info);
                    }
                }
                else
                {
                    _dialogService.ShowError(result.Error ?? "Failed to cherry-pick", "Cherry-pick Failed");
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to cherry-pick: {ex.Message}", "Error");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCherryPickOrRevert))]
    private async Task RevertCommitAsync()
    {
        if (SelectedCommit == null || string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        var confirm = _dialogService.ShowConfirmation(
            $"Revert commit {SelectedCommit.ShortHash}?\n\n" +
            $"This will create a new commit that undoes all changes from this commit.\n\n" +
            $"Subject: {SelectedCommit.Subject}",
            "Revert Commit");

        if (!confirm) return;

        try
        {
            var result = await _gitStatusService.RevertAsync(_currentWorkingDirectory, SelectedCommit.Hash);

            if (result.Success)
            {
                _toastService.Show($"Reverted {SelectedCommit.ShortHash}", ToastType.Success);
                await RefreshAsync();
            }
            else
            {
                if (result.Error?.Contains("conflict") == true)
                {
                    var action = _dialogService.ShowConfirmation(
                        "Revert resulted in conflicts.\n\n" +
                        "Would you like to abort the revert?",
                        "Conflicts Detected");

                    if (action)
                    {
                        await _gitStatusService.RevertAbortAsync(_currentWorkingDirectory);
                        _toastService.Show("Revert aborted", ToastType.Warning);
                    }
                    else
                    {
                        _toastService.Show("Resolve conflicts and commit manually", ToastType.Info);
                    }
                }
                else
                {
                    _dialogService.ShowError(result.Error ?? "Failed to revert", "Revert Failed");
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to revert: {ex.Message}", "Error");
        }
    }

    #endregion
}
