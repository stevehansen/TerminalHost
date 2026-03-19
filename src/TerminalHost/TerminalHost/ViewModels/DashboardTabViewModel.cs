using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the GitHub Dashboard tab (Ctrl+Shift+H).
/// </summary>
public partial class DashboardTabViewModel : ObservableObject, ITabViewModel
{
    private readonly IGitHubService _gitHubService;
    private readonly IConfigurationService _configService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private readonly ITimerService _timerService;
    private List<string>? _cachedRecentPaths;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IAiExecutionService _aiService;
    private readonly IAppTimer _refreshTimer;

    #region ITabViewModel Implementation

    public string Title => "Dashboard";
    public string TabIcon => "G";  // GitHub icon
    public string WorkingDirectory => "";
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsVisibleInFocusMode => true;  // Dashboard always visible

    [ObservableProperty]
    private bool _isSelected;
    public string DisplayTitle => Title;
    public bool ShowActivitySpinner => false;
    public bool ShowCompletedIndicator => false;
    public bool IsWaitingForInput => false;
    public bool ShowWaitingIndicator => false;
    public bool ShowClaudeTaskIndicator => false;  // Not a project tab
    public bool IsTerminalInitialized => true;
    public Task InitializeTerminalsAsync() => Task.CompletedTask;

    public event EventHandler? CloseRequested;

    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { }
    public void ClearUnreadActivity() { }

    #endregion

    /// <summary>
    /// Event raised when PR Review Mode should be opened for a specific PR.
    /// </summary>
    public event EventHandler<PrReviewRequestedEventArgs>? PrReviewRequested;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private DateTime _lastRefreshed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionItems))]
    [NotifyPropertyChangedFor(nameof(IsReviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsMyPRsSelected))]
    [NotifyPropertyChangedFor(nameof(IsIssuesSelected))]
    [NotifyPropertyChangedFor(nameof(IsCIFailedSelected))]
    private string _selectedSection = "Review";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionItems))]
    [NotifyPropertyChangedFor(nameof(ReviewRequestsCount))]
    [NotifyCanExecuteChangedFor(nameof(PrioritizePrsCommand))]
    private ObservableCollection<GitHubPullRequest> _reviewRequests = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionItems))]
    [NotifyPropertyChangedFor(nameof(MyPullRequestsCount))]
    private ObservableCollection<GitHubPullRequest> _myPullRequests = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionItems))]
    [NotifyPropertyChangedFor(nameof(MyIssuesCount))]
    private ObservableCollection<GitHubIssue> _myIssues = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionItems))]
    [NotifyPropertyChangedFor(nameof(FailedRunsCount))]
    private ObservableCollection<GitHubWorkflowRun> _failedRuns = [];

    [ObservableProperty]
    private ObservableCollection<RepositoryItem> _recentRepos = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCiFailureCommand))]
    private object? _selectedItem;

    // Feature 8: CI failure analysis
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCiFailureAnalysis))]
    private string _ciFailureAnalysis = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCiFailureCommand))]
    private bool _isAnalyzingCiFailure;

    public bool HasCiFailureAnalysis => !string.IsNullOrEmpty(CiFailureAnalysis);

    // Feature 9: PR prioritization
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrPriorityAdvice))]
    private string _prPriorityAdvice = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrioritizePrsCommand))]
    private bool _isGeneratingPrPriority;

    public bool HasPrPriorityAdvice => !string.IsNullOrEmpty(PrPriorityAdvice);

    // Selection state helpers for UI binding
    public bool IsReviewSelected => SelectedSection == "Review";
    public bool IsMyPRsSelected => SelectedSection == "MyPRs";
    public bool IsIssuesSelected => SelectedSection == "Issues";
    public bool IsCIFailedSelected => SelectedSection == "CIFailed";

    public bool IsGitHubCliAvailable => _gitHubService.IsGitHubCliAvailable();

    public int ReviewRequestsCount => ReviewRequests.Count;
    public int MyPullRequestsCount => MyPullRequests.Count;
    public int MyIssuesCount => MyIssues.Count;
    public int FailedRunsCount => FailedRuns.Count;

    public DashboardTabViewModel(
        IGitHubService gitHubService,
        IConfigurationService configService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IFileSystem fileSystem,
        IProcessService processService,
        IToastService toastService,
        ITimerService timerService,
        IFolderPickerService folderPickerService,
        IAiExecutionService aiExecutionService)
    {
        _gitHubService = gitHubService;
        _configService = configService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;
        _toastService = toastService;
        _timerService = timerService;
        _folderPickerService = folderPickerService;
        _aiService = aiExecutionService;

        // Setup auto-refresh timer
        _refreshTimer = _timerService.CreateTimer(TimeSpan.FromMinutes(5), async () => await RefreshAsync());

        // Populate recent repos from open tabs
        PopulateRecentRepos();
    }

    public async Task InitializeAsync()
    {
        // Force UI to update the CLI availability status
        OnPropertyChanged(nameof(IsGitHubCliAvailable));

        if (!_gitHubService.IsGitHubCliAvailable())
        {
            StatusMessage = "GitHub CLI (gh) is not available. Please install and authenticate it.";
            return;
        }

        await RefreshAsync();

        // Start auto-refresh timer
        var config = _configService.Load();
        if (config.Settings.Dashboard.Enabled)
        {
            _refreshTimer.Interval = TimeSpan.FromMinutes(config.Settings.Dashboard.RefreshIntervalMinutes);
            _refreshTimer.Start();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!_gitHubService.IsGitHubCliAvailable())
        {
            StatusMessage = "GitHub CLI not available. Click 'Retry' to check again.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Refreshing...";
        CiFailureAnalysis = "";
        PrPriorityAdvice = "";

        try
        {
            // Fetch all data in parallel
            var reviewTask = _gitHubService.GetReviewRequestsAsync();
            var myPrsTask = _gitHubService.GetMyPullRequestsAsync();
            var issuesTask = _gitHubService.GetMyIssuesAsync();
            var failedRunsTask = _gitHubService.GetFailedWorkflowRunsAsync();

            await Task.WhenAll(reviewTask, myPrsTask, issuesTask, failedRunsTask);

            // Sort by UpdatedAt descending (most recent first)
            ReviewRequests = new ObservableCollection<GitHubPullRequest>(
                (await reviewTask).OrderByDescending(p => p.UpdatedAt));
            MyPullRequests = new ObservableCollection<GitHubPullRequest>(
                (await myPrsTask).OrderByDescending(p => p.UpdatedAt));
            MyIssues = new ObservableCollection<GitHubIssue>(
                (await issuesTask).OrderByDescending(i => i.UpdatedAt));
            FailedRuns = new ObservableCollection<GitHubWorkflowRun>(
                (await failedRunsTask).OrderByDescending(r => r.UpdatedAt));

            // Update recent repos
            PopulateRecentRepos();

            LastRefreshed = DateTime.Now;
            StatusMessage = $"Last updated: {LastRefreshed:HH:mm}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RetryGitHubCli()
    {
        _gitHubService.ResetAvailabilityCache();
        OnPropertyChanged(nameof(IsGitHubCliAvailable));

        if (IsGitHubCliAvailable)
        {
            _ = RefreshAsync();
        }
        else
        {
            StatusMessage = "GitHub CLI still not available. Please run: gh auth login";
        }
    }

    [RelayCommand]
    private void SelectSection(string section)
    {
        SelectedSection = section;
        SelectedItem = null;
    }

    [RelayCommand]
    private void OpenInBrowser(object? item)
    {
        string? url = item switch
        {
            GitHubPullRequest pr => $"https://github.com/{pr.Repository}/pull/{pr.Number}",
            GitHubIssue issue => $"https://github.com/{issue.Repository}/issues/{issue.Number}",
            GitHubWorkflowRun run => run.HtmlUrl,
            _ => null
        };

        if (!string.IsNullOrEmpty(url))
        {
            _processService.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private bool CanCheckoutPullRequest(object? item) => item is GitHubPullRequest;

    [RelayCommand(CanExecute = nameof(CanCheckoutPullRequest))]
    private async Task CheckoutPullRequestAsync(object? item)
    {
        if (item is not GitHubPullRequest pr) return;

        // Ensure we have a local clone
        var localPath = FindLocalRepository(pr.Repository);
        if (string.IsNullOrEmpty(localPath))
        {
            localPath = await EnsureRepositoryAvailableAsync(pr);
            if (string.IsNullOrEmpty(localPath))
                return; // Clone failed or was cancelled
        }

        // Checkout the PR branch with progress toast
        using var toast = _toastService.ShowProgress($"Checking out PR #{pr.Number}...");
        StatusMessage = $"Checking out PR #{pr.Number}...";
        var (success, error) = await _gitHubService.CheckoutPullRequestAsync(localPath, pr.Number);

        if (success)
        {
            _mainViewModel.OpenProjectTab(localPath);
            StatusMessage = $"Checked out PR #{pr.Number}";
            toast.Complete($"PR #{pr.Number} checked out");
        }
        else
        {
            var errorMsg = !string.IsNullOrWhiteSpace(error) ? error : "Unknown error";
            toast.Fail($"Checkout failed");
            StatusMessage = $"Checkout failed: {errorMsg}";
            _dialogService.ShowWarning($"Failed to checkout PR #{pr.Number}:\n\n{errorMsg}", "Checkout Failed");
        }
    }

    /// <summary>
    /// Ensures a repository is available locally (clone or browse).
    /// Returns the local path or null if cancelled/failed.
    /// </summary>
    private async Task<string?> EnsureRepositoryAvailableAsync(GitHubPullRequest pr)
    {
        // Ask user what to do - browse or clone
        var repoName = pr.Repository.Contains('/') ? pr.Repository.Split('/').Last() : pr.Repository;
        var result = _dialogService.ShowConfirmation(
            $"Repository '{pr.Repository}' not found locally.\n\n" +
            "Would you like to browse for an existing folder?\n" +
            "(Click 'No' to clone automatically)",
            "Repository Not Found");

        if (result)
        {
            // Browse for existing folder
            var selectedFolder = _folderPickerService.PickFolder($"Select local folder for {repoName}");
            return selectedFolder; // Returns null if user cancelled
        }

        // Clone automatically
        var config = _configService.Load();
        var cloneDir = config.Settings.Repositories.CloneDirectory;

        if (string.IsNullOrEmpty(cloneDir))
        {
            cloneDir = _folderPickerService.PickFolder("Select directory to clone repositories into");
            if (string.IsNullOrEmpty(cloneDir))
            {
                return null; // User cancelled
            }

            config.Settings.Repositories.CloneDirectory = cloneDir;
            _configService.Save(config);
        }

        StatusMessage = $"Cloning {pr.Repository}...";
        var localPath = await _gitHubService.CloneRepositoryAsync(pr.Repository, cloneDir);

        if (string.IsNullOrEmpty(localPath))
        {
            _dialogService.ShowWarning($"Failed to clone {pr.Repository}", "Clone Failed");
            return null;
        }

        return localPath;
    }

    private bool CanOpenReviewMode(object? item) => item is GitHubPullRequest;

    [RelayCommand(CanExecute = nameof(CanOpenReviewMode))]
    private async Task OpenReviewModeAsync(object? item)
    {
        if (item is not GitHubPullRequest pr) return;

        // Check if we have a local clone
        var localPath = FindLocalRepository(pr.Repository);

        if (string.IsNullOrEmpty(localPath))
        {
            // Need to find/clone repo first
            localPath = await EnsureRepositoryAvailableAsync(pr);
            if (string.IsNullOrEmpty(localPath))
            {
                return; // Clone failed or was cancelled
            }
        }

        // Always checkout the PR branch (handles fetching, creating branch, switching)
        using var toast = _toastService.ShowProgress($"Checking out PR #{pr.Number} for review...");
        StatusMessage = $"Checking out PR #{pr.Number}...";
        var (success, error) = await _gitHubService.CheckoutPullRequestAsync(localPath, pr.Number);

        if (!success)
        {
            var errorMsg = !string.IsNullOrWhiteSpace(error) ? error : "Unknown error";
            toast.Fail($"Checkout failed");
            StatusMessage = $"Checkout failed: {errorMsg}";
            _dialogService.ShowWarning($"Failed to checkout PR #{pr.Number}:\n\n{errorMsg}", "Checkout Failed");
            return;
        }

        StatusMessage = $"Checked out PR #{pr.Number}";
        toast.Complete($"PR #{pr.Number} ready for review");

        // Open the project tab if not already open
        _mainViewModel.OpenProjectTab(localPath);

        // Raise event to open PR Review Mode
        PrReviewRequested?.Invoke(this, new PrReviewRequestedEventArgs(localPath, pr));
    }

    [RelayCommand]
    private void OpenRepository(RepositoryItem repo)
    {
        if (repo == null || string.IsNullOrEmpty(repo.LocalPath)) return;

        _mainViewModel.OpenProjectTab(repo.LocalPath);
    }

    #region Feature 8: CI Failure Analysis

    public bool CanAnalyzeCiFailure => SelectedItem is GitHubWorkflowRun && !IsAnalyzingCiFailure;

    [RelayCommand(CanExecute = nameof(CanAnalyzeCiFailure))]
    private async Task AnalyzeCiFailureAsync()
    {
        if (SelectedItem is not GitHubWorkflowRun run) return;

        var workDir = FindLocalRepository(run.Repository);

        // Get CI logs via gh CLI
        var logArgs = $"run view {run.Id} --log-failed";
        if (!string.IsNullOrEmpty(workDir))
        {
            var (logExitCode, logOutput, logError) = await _processService.RunAsync(
                "gh", logArgs, workDir, timeout: TimeSpan.FromSeconds(30));
            var log = logExitCode == 0 && !string.IsNullOrWhiteSpace(logOutput) ? logOutput : logError;
            if (string.IsNullOrWhiteSpace(log)) log = "(no log available)";
            if (log.Length > 8000) log = log[..8000] + "\n[truncated]";

            var prompt = $"Analyze this CI failure and provide a brief diagnosis (2-4 sentences). What failed, likely cause, and suggested fix.\n\nWorkflow: {run.WorkflowName}\nBranch: {run.Branch}\n\nFailure log:\n{log}";
            await RunAiForCiAnalysis(prompt, workDir);
        }
        else
        {
            // No local repo — run without logs, just metadata
            var prompt = $"Analyze this CI failure and provide a brief diagnosis (2-4 sentences). What failed, likely cause, and suggested fix.\n\nWorkflow: {run.WorkflowName}\nBranch: {run.Branch}\nConclusion: {run.Conclusion}\nCommit: {run.CommitMessage}\n\n(No detailed logs available — repo not cloned locally)";
            await RunAiForCiAnalysis(prompt, null);
        }
    }

    private async Task RunAiForCiAnalysis(string prompt, string? workDir)
    {
        if (!_aiService.IsAiAvailable()) return;

        IsAnalyzingCiFailure = true;
        try
        {
            var result = await _aiService.ExecuteAsync(prompt, workDir ?? "", "Analyzing CI failure");
            if (result.Success)
                CiFailureAnalysis = result.Output!;
        }
        finally
        {
            IsAnalyzingCiFailure = false;
        }
    }

    [RelayCommand]
    private void DismissCiFailureAnalysis() => CiFailureAnalysis = "";

    #endregion

    #region Feature 9: PR Prioritization

    public bool CanPrioritizePrs => ReviewRequests.Count > 0 && !IsGeneratingPrPriority;

    [RelayCommand(CanExecute = nameof(CanPrioritizePrs))]
    private async Task PrioritizePrsAsync()
    {
        if (!_aiService.IsAiAvailable()) return;

        var prList = string.Join("\n", ReviewRequests.Select(pr =>
            $"#{pr.Number} by {pr.Author}: {pr.Title} ({pr.DiffSummary}, CI: {pr.CiStatus ?? "unknown"}, updated {pr.TimeSinceUpdate})"));

        // Use first open tab's working directory as fallback
        var workDir = _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>().FirstOrDefault()?.Pair.WorkingDirectory ?? "";

        var prompt = $"Given these pull requests awaiting review, suggest a priority order. Consider: risk (large diffs), failing CI, and staleness. Format as a numbered list: #number \u2014 one-sentence rationale.\n\nPRs:\n{prList}";

        IsGeneratingPrPriority = true;
        try
        {
            var result = await _aiService.ExecuteAsync(prompt, workDir, "Prioritizing PRs");
            if (result.Success)
                PrPriorityAdvice = result.Output!;
        }
        finally
        {
            IsGeneratingPrPriority = false;
        }
    }

    [RelayCommand]
    private void DismissPrPriorityAdvice() => PrPriorityAdvice = "";

    #endregion

    [RelayCommand]
    private void Close()
    {
        _refreshTimer.Stop();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private string? FindLocalRepository(string repoFullName)
    {
        // Check open tabs first
        foreach (var tab in _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>())
        {
            var gitConfigPath = System.IO.Path.Combine(tab.Pair.WorkingDirectory, ".git", "config");
            if (_fileSystem.FileExists(gitConfigPath))
            {
                try
                {
                    var content = _fileSystem.ReadAllText(gitConfigPath);
                    if (content.Contains(repoFullName, StringComparison.OrdinalIgnoreCase))
                    {
                        return tab.Pair.WorkingDirectory;
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        // Check configured scan paths
        var config = _configService.Load();
        foreach (var scanPath in config.Settings.Repositories.ScanPaths)
        {
            if (!_fileSystem.DirectoryExists(scanPath)) continue;

            foreach (var dir in _fileSystem.GetDirectories(scanPath))
            {
                var gitConfigPath = System.IO.Path.Combine(dir, ".git", "config");
                if (!_fileSystem.FileExists(gitConfigPath)) continue;

                try
                {
                    var content = _fileSystem.ReadAllText(gitConfigPath);
                    if (content.Contains(repoFullName, StringComparison.OrdinalIgnoreCase))
                    {
                        return dir;
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets items to display based on the selected section.
    /// </summary>
    public IEnumerable<object> CurrentSectionItems => SelectedSection switch
    {
        "Review" => ReviewRequests,
        "MyPRs" => MyPullRequests,
        "Issues" => MyIssues,
        "CIFailed" => FailedRuns,
        _ => ReviewRequests
    };

    /// <summary>
    /// Populates the recent repos list from open tabs and config.
    /// </summary>
    private void PopulateRecentRepos()
    {
        var repos = new List<RepositoryItem>();

        // Add from open tabs
        foreach (var tab in _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>())
        {
            var dir = tab.Pair.WorkingDirectory;
            var gitDir = System.IO.Path.Combine(dir, ".git");

            if (_fileSystem.DirectoryExists(gitDir))
            {
                repos.Add(new RepositoryItem
                {
                    FullName = System.IO.Path.GetFileName(dir),
                    LocalPath = dir,
                    IsOpen = true,
                    IsLocal = true
                });
            }
        }

        // Add from recent paths in config (if not already in list).
        // Cache the paths to avoid loading the full 145KB config on every refresh.
        _cachedRecentPaths ??= _configService.Load().Settings.Repositories.RecentPaths;
        foreach (var path in _cachedRecentPaths.Take(10))
        {
            if (repos.Any(r => r.LocalPath?.Equals(path, StringComparison.OrdinalIgnoreCase) == true))
                continue;

            if (_fileSystem.DirectoryExists(path))
            {
                repos.Add(new RepositoryItem
                {
                    FullName = System.IO.Path.GetFileName(path),
                    LocalPath = path,
                    IsOpen = false,
                    IsLocal = true
                });
            }
        }

        RecentRepos = new ObservableCollection<RepositoryItem>(repos.Take(10));
    }
}

/// <summary>
/// Event args for requesting PR Review Mode to open for a specific PR.
/// </summary>
public class PrReviewRequestedEventArgs : EventArgs
{
    public string WorkingDirectory { get; }
    public GitHubPullRequest PullRequest { get; }

    public PrReviewRequestedEventArgs(string workingDirectory, GitHubPullRequest pullRequest)
    {
        WorkingDirectory = workingDirectory;
        PullRequest = pullRequest;
    }
}
