using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the PR Review Mode popup (Ctrl+Shift+R).
/// </summary>
public partial class PrReviewViewModel : ObservableObject
{
    private readonly IGitHubService _gitHubService;
    private readonly IDialogService _dialogService;
    private readonly ITestRunnerService _testRunnerService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IMarkdownService _markdownService;
    private readonly IToastService _toastService;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private GitHubPullRequest? _pullRequest;

    [ObservableProperty]
    private ObservableCollection<GitHubPrFile> _changedFiles = [];

    [ObservableProperty]
    private GitHubPrFile? _selectedFile;

    [ObservableProperty]
    private string _diffContent = "";

    [ObservableProperty]
    private ObservableCollection<PrReviewComment> _pendingComments = [];

    [ObservableProperty]
    private string _reviewComment = "";

    [ObservableProperty]
    private bool _isRunningTests;

    [ObservableProperty]
    private TestRunSummary? _testSummary;

    [ObservableProperty]
    private string _bodyHtml = "";

    [ObservableProperty]
    private bool _isBodyExpanded;

    [ObservableProperty]
    private PrComments? _prComments;

    [ObservableProperty]
    private ObservableCollection<PrReviewThread> _currentFileThreads = [];

    [ObservableProperty]
    private ObservableCollection<PrReviewThread> _allThreads = [];

    [ObservableProperty]
    private bool _isCommentsExpanded = true;

    [ObservableProperty]
    private bool _showAllComments = true;

    public bool HasPullRequest => PullRequest != null;

    public bool HasBody => !string.IsNullOrWhiteSpace(PullRequest?.Body);

    public bool HasComments => PrComments != null && PrComments.TotalThreadCount > 0;

    public int CurrentFileCommentCount => CurrentFileThreads.Count;

    public int TotalCommentCount => AllThreads.Count;

    public ObservableCollection<PrReviewThread> DisplayedThreads => ShowAllComments ? AllThreads : CurrentFileThreads;

    public PrReviewViewModel(
        IGitHubService gitHubService,
        IDialogService dialogService,
        ITestRunnerService testRunnerService,
        IProjectDetectionService projectDetectionService,
        IFileSystem fileSystem,
        IProcessService processService,
        IMarkdownService markdownService,
        IToastService toastService)
    {
        _gitHubService = gitHubService;
        _dialogService = dialogService;
        _testRunnerService = testRunnerService;
        _projectDetectionService = projectDetectionService;
        _fileSystem = fileSystem;
        _processService = processService;
        _markdownService = markdownService;
        _toastService = toastService;
    }

    /// <summary>
    /// Opens the PR Review panel for the specified working directory.
    /// Automatically detects if the current branch is a PR branch.
    /// </summary>
    public async Task OpenAsync(string workingDirectory)
    {
        WorkingDirectory = workingDirectory;
        IsOpen = true;
        IsLoading = true;
        StatusMessage = "Detecting PR...";
        PullRequest = null;
        ChangedFiles.Clear();
        DiffContent = "";
        PendingComments.Clear();
        BodyHtml = "";
        IsBodyExpanded = false;

        try
        {
            // Try to get the current PR for this branch
            var pr = await _gitHubService.GetCurrentPullRequestAsync(workingDirectory);

            if (pr == null)
            {
                StatusMessage = "No PR found for current branch";
                IsLoading = false;
                return;
            }

            PullRequest = pr;
            OnPropertyChanged(nameof(HasPullRequest));
            OnPropertyChanged(nameof(HasBody));
            StatusMessage = $"PR #{pr.Number}: {pr.Title}";

            // Convert body markdown to HTML
            if (!string.IsNullOrWhiteSpace(pr.Body))
            {
                BodyHtml = _markdownService.ConvertToHtml(pr.Body);
            }

            // Load the files changed in this PR
            await LoadChangedFilesAsync();

            // Load PR comments
            await LoadCommentsAsync();
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

    /// <summary>
    /// Opens the PR Review panel for a specific PR (e.g., from Dashboard).
    /// </summary>
    public async Task OpenForPrAsync(string workingDirectory, GitHubPullRequest pr)
    {
        WorkingDirectory = workingDirectory;
        IsOpen = true;
        IsLoading = true;
        PullRequest = pr;
        OnPropertyChanged(nameof(HasPullRequest));
        OnPropertyChanged(nameof(HasBody));
        StatusMessage = $"Loading PR #{pr.Number}...";
        ChangedFiles.Clear();
        DiffContent = "";
        PendingComments.Clear();
        BodyHtml = "";
        IsBodyExpanded = false;

        try
        {
            // If body is not populated (from list view), fetch full PR details
            if (pr.Body == null)
            {
                var fullPr = await _gitHubService.GetCurrentPullRequestAsync(workingDirectory);
                if (fullPr != null)
                {
                    pr.Body = fullPr.Body;
                }
            }

            // Convert body markdown to HTML
            if (!string.IsNullOrWhiteSpace(pr.Body))
            {
                BodyHtml = _markdownService.ConvertToHtml(pr.Body);
                OnPropertyChanged(nameof(HasBody));
            }

            await LoadChangedFilesAsync();

            // Load PR comments
            await LoadCommentsAsync();

            StatusMessage = $"PR #{pr.Number}: {pr.Title}";
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

    private async Task LoadChangedFilesAsync()
    {
        if (PullRequest == null) return;

        var files = await _gitHubService.GetPullRequestFilesAsync(PullRequest.Repository, PullRequest.Number);
        ChangedFiles = new ObservableCollection<GitHubPrFile>(files);

        if (ChangedFiles.Count > 0)
        {
            SelectedFile = ChangedFiles[0];
        }
    }

    private async Task LoadCommentsAsync()
    {
        if (PullRequest == null || string.IsNullOrEmpty(PullRequest.Repository)) return;

        try
        {
            PrComments = await _gitHubService.GetPullRequestCommentsAsync(PullRequest.Repository, PullRequest.Number);
            OnPropertyChanged(nameof(HasComments));

            // Populate all threads
            if (PrComments != null)
            {
                AllThreads = new ObservableCollection<PrReviewThread>(PrComments.ReviewThreads);
            }
            else
            {
                AllThreads.Clear();
            }
            OnPropertyChanged(nameof(TotalCommentCount));
            OnPropertyChanged(nameof(DisplayedThreads));

            UpdateCurrentFileThreads();
        }
        catch
        {
            PrComments = null;
            AllThreads.Clear();
            OnPropertyChanged(nameof(HasComments));
            OnPropertyChanged(nameof(TotalCommentCount));
            OnPropertyChanged(nameof(DisplayedThreads));
        }
    }

    private void UpdateCurrentFileThreads()
    {
        if (PrComments == null || SelectedFile == null)
        {
            CurrentFileThreads.Clear();
            OnPropertyChanged(nameof(CurrentFileCommentCount));
            return;
        }

        var threads = PrComments.GetThreadsForFile(SelectedFile.Path);
        CurrentFileThreads = new ObservableCollection<PrReviewThread>(threads);
        OnPropertyChanged(nameof(CurrentFileCommentCount));
    }

    partial void OnSelectedFileChanged(GitHubPrFile? value)
    {
        if (value == null)
        {
            DiffContent = "";
            CurrentFileThreads.Clear();
            OnPropertyChanged(nameof(CurrentFileCommentCount));
            return;
        }

        _ = LoadFileDiffAsync(value);
        UpdateCurrentFileThreads();
    }

    private async Task LoadFileDiffAsync(GitHubPrFile file)
    {
        if (PullRequest == null || string.IsNullOrEmpty(WorkingDirectory)) return;

        try
        {
            DiffContent = await _gitHubService.GetFileDiffAsync(WorkingDirectory, file.Path);
        }
        catch (Exception ex)
        {
            DiffContent = $"Error loading diff: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        PullRequest = null;
        ChangedFiles.Clear();
        DiffContent = "";
        PendingComments.Clear();
        BodyHtml = "";
        IsBodyExpanded = false;
        PrComments = null;
        CurrentFileThreads.Clear();
        AllThreads.Clear();
        IsCommentsExpanded = true;
        ShowAllComments = true;
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(CurrentFileCommentCount));
        OnPropertyChanged(nameof(TotalCommentCount));
        OnPropertyChanged(nameof(DisplayedThreads));
    }

    [RelayCommand]
    private void ToggleBody()
    {
        IsBodyExpanded = !IsBodyExpanded;
    }

    [RelayCommand]
    private void ToggleComments()
    {
        IsCommentsExpanded = !IsCommentsExpanded;
    }

    [RelayCommand]
    private void ToggleCommentFilter()
    {
        ShowAllComments = !ShowAllComments;
        OnPropertyChanged(nameof(DisplayedThreads));
    }

    partial void OnShowAllCommentsChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedThreads));
    }

    [RelayCommand]
    private async Task ApproveAsync()
    {
        if (PullRequest == null || string.IsNullOrEmpty(WorkingDirectory)) return;

        using var toast = _toastService.ShowProgress($"Approving PR #{PullRequest.Number}...");
        var comment = string.IsNullOrWhiteSpace(ReviewComment) ? null : ReviewComment.Trim();
        var success = await _gitHubService.ApprovePullRequestAsync(WorkingDirectory, PullRequest.Number, comment);

        if (success)
        {
            StatusMessage = "PR approved";
            ReviewComment = "";
            toast.Complete($"PR #{PullRequest.Number} approved");
        }
        else
        {
            toast.Fail("Failed to approve PR");
        }
    }

    [RelayCommand]
    private async Task RequestChangesAsync()
    {
        if (PullRequest == null || string.IsNullOrEmpty(WorkingDirectory)) return;

        if (string.IsNullOrWhiteSpace(ReviewComment))
        {
            _dialogService.ShowWarning("Please provide a comment when requesting changes", "Comment Required");
            return;
        }

        using var toast = _toastService.ShowProgress($"Requesting changes on PR #{PullRequest.Number}...");
        var success = await _gitHubService.RequestChangesAsync(WorkingDirectory, PullRequest.Number, ReviewComment.Trim());

        if (success)
        {
            StatusMessage = "Changes requested";
            ReviewComment = "";
            toast.Complete($"Changes requested on PR #{PullRequest.Number}");
        }
        else
        {
            toast.Fail("Failed to request changes");
        }
    }

    [RelayCommand]
    private async Task CommentAsync()
    {
        if (PullRequest == null || string.IsNullOrEmpty(WorkingDirectory)) return;

        if (string.IsNullOrWhiteSpace(ReviewComment))
        {
            _dialogService.ShowWarning("Please enter a comment", "Comment Required");
            return;
        }

        using var toast = _toastService.ShowProgress("Adding comment...");
        var success = await _gitHubService.CommentOnPullRequestAsync(WorkingDirectory, PullRequest.Number, ReviewComment.Trim());

        if (success)
        {
            StatusMessage = "Comment added";
            ReviewComment = "";
            toast.Complete("Comment added");
        }
        else
        {
            toast.Fail("Failed to add comment");
        }
    }

    [RelayCommand]
    private async Task MergeAsync()
    {
        if (PullRequest == null || string.IsNullOrEmpty(WorkingDirectory)) return;

        // Build the expected squash commit message
        var squashCommitMessage = $"{PullRequest.Title} (#{PullRequest.Number})";

        // Confirm merge with squash commit preview
        var confirmed = _dialogService.ShowConfirmation(
            $"Squash and merge PR #{PullRequest.Number}?\n\nCommit message:\n{squashCommitMessage}",
            "Confirm Squash & Merge");

        if (!confirmed) return;

        using var toast = _toastService.ShowProgress($"Merging PR #{PullRequest.Number}...");
        var success = await _gitHubService.MergePullRequestAsync(WorkingDirectory, PullRequest.Number, "squash", squashCommitMessage);

        if (success)
        {
            StatusMessage = "PR merged successfully";
            toast.Complete($"PR #{PullRequest.Number} merged");
            Close();
        }
        else
        {
            toast.Fail("Failed to merge PR");
        }
    }

    [RelayCommand]
    private async Task RunTestsAsync()
    {
        if (string.IsNullOrEmpty(WorkingDirectory)) return;

        IsRunningTests = true;
        TestSummary = null;
        StatusMessage = "Running tests...";

        try
        {
            var projectType = _projectDetectionService.DetectProjectType(WorkingDirectory);
            var results = await _testRunnerService.RunAllTestsAsync(WorkingDirectory, projectType);

            TestSummary = new TestRunSummary
            {
                Total = results.Count,
                Passed = results.Count(r => r.Status == TestStatus.Passed),
                Failed = results.Count(r => r.Status == TestStatus.Failed),
                Skipped = results.Count(r => r.Status == TestStatus.Skipped)
            };

            if (TestSummary.Failed > 0)
            {
                StatusMessage = $"Tests: {TestSummary.Passed} passed, {TestSummary.Failed} failed";
            }
            else
            {
                StatusMessage = $"Tests: {TestSummary.Total} passed";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test error: {ex.Message}";
        }
        finally
        {
            IsRunningTests = false;
        }
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (PullRequest == null) return;

        var url = $"https://github.com/{PullRequest.Repository}/pull/{PullRequest.Number}";
        _processService.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFileInEditor(GitHubPrFile? file)
    {
        if (file == null || string.IsNullOrEmpty(WorkingDirectory)) return;

        var fullPath = System.IO.Path.Combine(WorkingDirectory, file.Path);
        if (!_fileSystem.FileExists(fullPath)) return;

        // This will be handled by the MainWindow via an event
        FileOpenRequested?.Invoke(this, fullPath);
    }

    /// <summary>
    /// Event raised when a file should be opened in the editor.
    /// </summary>
    public event EventHandler<string>? FileOpenRequested;
}
