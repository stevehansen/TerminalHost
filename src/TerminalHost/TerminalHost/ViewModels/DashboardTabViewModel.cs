using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using TerminalHost.Domain;
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
    private readonly DispatcherTimer _refreshTimer;

    #region ITabViewModel Implementation

    public string Title => "Dashboard";
    public string TabIcon => "D";  // Home icon
    public string WorkingDirectory => "";
    public bool IsCloseable => true;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsSelected { get; set; }
    public bool IsVisibleInFocusMode => true;  // Dashboard always visible
    public string DisplayTitle => Title;

    public event EventHandler? CloseRequested;

    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { }
    public void ClearUnreadActivity() { }

    #endregion

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private DateTime _lastRefreshed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionItems))]
    private string _selectedSection = "Review";

    [ObservableProperty]
    private ObservableCollection<GitHubPullRequest> _reviewRequests = [];

    [ObservableProperty]
    private ObservableCollection<GitHubPullRequest> _myPullRequests = [];

    [ObservableProperty]
    private ObservableCollection<GitHubIssue> _myIssues = [];

    [ObservableProperty]
    private ObservableCollection<GitHubWorkflowRun> _failedRuns = [];

    [ObservableProperty]
    private ObservableCollection<RepositoryItem> _recentRepos = [];

    [ObservableProperty]
    private object? _selectedItem;

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
        IProcessService processService)
    {
        _gitHubService = gitHubService;
        _configService = configService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _processService = processService;

        // Setup auto-refresh timer
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
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

        try
        {
            // Fetch all data in parallel
            var reviewTask = _gitHubService.GetReviewRequestsAsync();
            var myPrsTask = _gitHubService.GetMyPullRequestsAsync();
            var issuesTask = _gitHubService.GetMyIssuesAsync();

            await Task.WhenAll(reviewTask, myPrsTask, issuesTask);

            ReviewRequests = new ObservableCollection<GitHubPullRequest>(await reviewTask);
            MyPullRequests = new ObservableCollection<GitHubPullRequest>(await myPrsTask);
            MyIssues = new ObservableCollection<GitHubIssue>(await issuesTask);

            // Notify count changes
            OnPropertyChanged(nameof(ReviewRequestsCount));
            OnPropertyChanged(nameof(MyPullRequestsCount));
            OnPropertyChanged(nameof(MyIssuesCount));
            OnPropertyChanged(nameof(FailedRunsCount));

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

        // Check if we have a local clone
        var localPath = FindLocalRepository(pr.Repository);

        if (string.IsNullOrEmpty(localPath))
        {
            // Ask to clone
            var config = _configService.Load();
            var cloneDir = config.Settings.Repositories.CloneDirectory;

            if (string.IsNullOrEmpty(cloneDir))
            {
                cloneDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "repos");
            }

            StatusMessage = $"Cloning {pr.Repository}...";
            localPath = await _gitHubService.CloneRepositoryAsync(pr.Repository, cloneDir);

            if (string.IsNullOrEmpty(localPath))
            {
                _dialogService.ShowWarning($"Failed to clone {pr.Repository}", "Checkout Failed");
                return;
            }
        }

        // Checkout the PR branch
        StatusMessage = $"Checking out PR #{pr.Number}...";
        var success = await _gitHubService.CheckoutPullRequestAsync(localPath, pr.Number);

        if (success)
        {
            _mainViewModel.OpenProjectTab(localPath);
            StatusMessage = $"Checked out PR #{pr.Number}";
        }
        else
        {
            _dialogService.ShowWarning($"Failed to checkout PR #{pr.Number}", "Checkout Failed");
            StatusMessage = "Checkout failed";
        }
    }

    [RelayCommand]
    private void OpenRepository(RepositoryItem repo)
    {
        if (repo == null || string.IsNullOrEmpty(repo.LocalPath)) return;

        _mainViewModel.OpenProjectTab(repo.LocalPath);
    }

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
}
