using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;
using System.Globalization;

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
    private readonly ICommitGraphService _commitGraphService;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private readonly IAiExecutionService _aiService;
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
    [NotifyCanExecuteChangedFor(nameof(ExplainCommitCommand))]
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

    [ObservableProperty]
    private bool _isCompactView;

    [ObservableProperty]
    private string _filePathFilter = "";

    [ObservableProperty]
    private DateTime? _afterDate;

    [ObservableProperty]
    private DateTime? _beforeDate;

    [ObservableProperty]
    private bool _isFilterExpanded;

    [ObservableProperty]
    private string _afterDateText = "";

    [ObservableProperty]
    private string _beforeDateText = "";

    [ObservableProperty]
    private List<GraphNode> _graphNodes = [];

    [ObservableProperty]
    private int _graphWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommitExplanation))]
    private string _commitAiExplanation = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommitCommand))]
    private bool _isExplainingCommit;

    public bool HasCommitExplanation => !string.IsNullOrEmpty(CommitAiExplanation);

    public bool HasSelectedCommit => SelectedCommit != null;
    public bool HasCommits => Commits.Count > 0;

    #endregion

    public CommitHistoryViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService,
        ICommitGraphService commitGraphService,
        IProcessService processService,
        IConfigurationService configurationService,
        IAiExecutionService aiExecutionService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;
        _commitGraphService = commitGraphService;
        _processService = processService;
        _configurationService = configurationService;
        _aiService = aiExecutionService;

        // Set defaults - defaults to Panel
        DisplayState = PanelDisplayState.Panel;
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
            GraphNodes = [];
            return;
        }

        IsLoading = true;
        try
        {
            var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;
            var author = string.IsNullOrWhiteSpace(AuthorFilter) ? null : AuthorFilter;
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
            var fileFilter = string.IsNullOrWhiteSpace(FilePathFilter) ? null : FilePathFilter;
            DateTimeOffset? after = AfterDate.HasValue ? new DateTimeOffset(AfterDate.Value) : null;
            DateTimeOffset? before = BeforeDate.HasValue ? new DateTimeOffset(BeforeDate.Value) : null;

            var commits = await _gitStatusService.GetCommitHistoryAsync(
                workingDirectory, DefaultCommitCount, author, fileFilter, search, after, before);

            Commits = new ObservableCollection<GitCommit>(commits);

            // Insert WIP entry at top if repo is dirty
            if (_currentTerminalTab.GitStatus?.IsDirty == true)
            {
                var modifiedFiles = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);
                var count = modifiedFiles.Count;
                Commits.Insert(0, new GitCommit
                {
                    IsWipEntry = true,
                    ShortHash = "\u270e",
                    Subject = "Working Changes",
                    AuthorName = "",
                    RelativeDate = $"{count} file(s) modified"
                });
            }

            // Compute commit graph
            GraphNodes = _commitGraphService.ComputeGraph(Commits.ToList());
            GraphWidth = (GraphNodes.Count > 0 ? GraphNodes.Max(n => n.Column) + 1 : 1) * 20;

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
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
            var fileFilter = string.IsNullOrWhiteSpace(FilePathFilter) ? null : FilePathFilter;
            DateTimeOffset? after = AfterDate.HasValue ? new DateTimeOffset(AfterDate.Value) : null;
            DateTimeOffset? before = BeforeDate.HasValue ? new DateTimeOffset(BeforeDate.Value) : null;
            var totalCount = Commits.Count + LoadMoreCount;
            var commits = await _gitStatusService.GetCommitHistoryAsync(
                workingDirectory, totalCount, author, fileFilter, search, after, before);

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
    private void ToggleCompactView()
    {
        IsCompactView = !IsCompactView;
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        AuthorFilter = "";
        SearchText = "";
        FilePathFilter = "";
        AfterDate = null;
        BeforeDate = null;
        AfterDateText = "";
        BeforeDateText = "";
        await RefreshCommitsAsync();
    }

    [RelayCommand]
    private void ToggleFilterExpanded()
    {
        IsFilterExpanded = !IsFilterExpanded;
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        // Parse date texts
        if (!string.IsNullOrWhiteSpace(AfterDateText) &&
            DateTime.TryParseExact(AfterDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var after))
        {
            AfterDate = after;
        }
        else
        {
            AfterDate = null;
        }

        if (!string.IsNullOrWhiteSpace(BeforeDateText) &&
            DateTime.TryParseExact(BeforeDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var before))
        {
            BeforeDate = before;
        }
        else
        {
            BeforeDate = null;
        }

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

    public bool CanExplainCommit => SelectedCommit != null && !IsExplainingCommit;

    [RelayCommand(CanExecute = nameof(CanExplainCommit))]
    private async Task ExplainCommitAsync()
    {
        if (SelectedCommit == null || _currentTerminalTab?.Pair.WorkingDirectory == null) return;
        if (!_aiService.IsAiAvailable()) return;

        var workingDirectory = _currentTerminalTab.Pair.WorkingDirectory;

        IsExplainingCommit = true;
        try
        {
            // Get the diff content
            string diff;
            if (!string.IsNullOrWhiteSpace(DiffText))
            {
                diff = DiffText;
            }
            else
            {
                var (exitCode, output, _) = await _processService.RunAsync(
                    "git", $"show {SelectedCommit.Hash} --stat -p",
                    workingDirectory, timeout: TimeSpan.FromSeconds(15));
                diff = exitCode == 0 ? output : "";
            }

            if (string.IsNullOrWhiteSpace(diff))
            {
                _toastService.Show("No diff available for this commit", ToastType.Warning);
                return;
            }

            const int maxDiffLength = 12_000;
            if (diff.Length > maxDiffLength)
                diff = diff[..maxDiffLength] + "\n\n[... diff truncated ...]";

            var prompt = $"Explain this commit in 2-4 sentences. What changed and why (infer intent from context and message). Plain English, no markdown.\n\nMessage: {SelectedCommit.Subject}\n\nDiff:\n{diff}";

            var result = await _aiService.ExecuteAsync(prompt, workingDirectory, "Explaining commit");
            if (result.Success)
                CommitAiExplanation = result.Output!;
        }
        finally
        {
            IsExplainingCommit = false;
        }
    }

    [RelayCommand]
    private void CopyCommitExplanation()
    {
        if (!string.IsNullOrEmpty(CommitAiExplanation))
        {
            System.Windows.Clipboard.SetText(CommitAiExplanation);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }

    [RelayCommand]
    private void DismissCommitExplanation() => CommitAiExplanation = "";

    #endregion

    #region Event Handlers

    partial void OnSelectedCommitChanged(GitCommit? value)
    {
        CommitAiExplanation = "";
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

            // Load working changes for WIP entry
            if (commit.IsWipEntry)
            {
                var modifiedFiles = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);
                var wipDetails = new GitCommitDetails
                {
                    IsWipEntry = true,
                    ShortHash = "\u270e",
                    Subject = "Working Changes",
                    AuthorName = "",
                    RelativeDate = $"{modifiedFiles.Count} file(s) modified",
                    Files = modifiedFiles.Select(f => new GitCommitFile
                    {
                        FilePath = f.FilePath,
                        Status = f.Status,
                        Insertions = 0,
                        Deletions = 0
                    }).ToList(),
                    TotalInsertions = 0,
                    TotalDeletions = 0
                };
                CommitDetails = wipDetails;

                if (CommitDetails.Files.Count > 0)
                {
                    SelectedFile = CommitDetails.Files[0];
                }
                else
                {
                    SelectedFile = null;
                    DiffText = "No working changes.";
                }
                return;
            }

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

        // For WIP entries, use working directory diff instead of commit diff
        if (SelectedCommit.IsWipEntry)
        {
            var diff = await _gitStatusService.GetFileDiffAsync(workingDirectory, file.FilePath);
            DiffText = diff ?? "";
            return;
        }

        var commitDiff = await _gitStatusService.GetCommitDiffAsync(workingDirectory, SelectedCommit.Hash, file.FilePath);
        DiffText = commitDiff ?? "";
    }

    #endregion
}
