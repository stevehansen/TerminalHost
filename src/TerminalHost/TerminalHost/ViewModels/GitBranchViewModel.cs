using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Data;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitBranchViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IConfigurationService _configurationService;
    private readonly MainViewModel _mainViewModel; // To get selected tab and refresh its git status
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;

    private List<GitBranch> _allBranches = [];

    /// <summary>
    /// Event raised when user requests to compare branches.
    /// MainWindow handles this to open BranchComparisonViewModel.
    /// </summary>
    public event EventHandler<CompareBranchesRequestedEventArgs>? CompareBranchesRequested;

    [ObservableProperty]
    private string _currentBranchWorkingDirectory = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitBranch> _branches = [];

    [ObservableProperty]
    private GitBranch? _selectedBranch;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _title = "Git Branches";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Key branch comparison properties
    [ObservableProperty]
    private GitBranch? _currentBranch;

    [ObservableProperty]
    private ObservableCollection<KeyBranchStatus> _keyBranchStatuses = [];

    [ObservableProperty]
    private bool _hasKeyBranches;

    // View properties for positioning/sizing the popup
    [ObservableProperty]
    private double _width = 1100;
    [ObservableProperty]
    private double _height = 700;
    [ObservableProperty]
    private double _horizontalOffset;
    [ObservableProperty]
    private double _verticalOffset;

    public GitBranchViewModel(
        IGitStatusService gitStatusService,
        IConfigurationService configurationService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _configurationService = configurationService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _toastService = toastService;

        // GitBranch is opened via MainWindow.xaml.cs directly (Ctrl+B shortcut)
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            StatusMessage = "Please select a project tab first.";
            IsOpen = true; // Still open to show message
            return;
        }

        await LoadDataAsync(terminalTab);
        IsOpen = true;
    }

    /// <summary>
    /// Loads branch data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        CurrentBranchWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Git Branches - {terminalTab.Title}";
        StatusMessage = string.Empty; // Clear previous messages

        await RefreshGitBranchesAsync();

        // Reset search text and focus should be handled by the view
        SearchText = string.Empty;
    }

    [RelayCommand]
    private async Task RefreshGitBranchesAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            _allBranches = await _gitStatusService.GetBranchesAsync(CurrentBranchWorkingDirectory);
            ApplyBranchFilter();

            // Set current branch
            CurrentBranch = _allBranches.FirstOrDefault(b => b.IsCurrent);

            // Load key branch comparisons
            await LoadKeyBranchStatusesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadKeyBranchStatusesAsync()
    {
        if (CurrentBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
        {
            KeyBranchStatuses.Clear();
            HasKeyBranches = false;
            return;
        }

        try
        {
            // Get key branch patterns from config
            var config = _configurationService.Load();
            var dirSettings = config.DirectorySettings.TryGetValue(CurrentBranchWorkingDirectory.ToLowerInvariant(), out var ds) ? ds : null;
            var keyBranchPatterns = dirSettings?.KeyBranchOverrides ?? config.Settings.KeyBranches;

            // Get key branches that exist in this repo
            var keyBranches = await _gitStatusService.GetKeyBranchesAsync(CurrentBranchWorkingDirectory, keyBranchPatterns);

            // Filter out the current branch from key branches display
            keyBranches = keyBranches.Where(b => !b.IsCurrent).ToList();

            var statuses = new List<KeyBranchStatus>();

            foreach (var keyBranch in keyBranches)
            {
                // Get ahead/behind counts
                var (ahead, behind) = await _gitStatusService.GetAheadBehindAsync(
                    CurrentBranchWorkingDirectory,
                    CurrentBranch.Name,
                    keyBranch.Name);

                statuses.Add(new KeyBranchStatus
                {
                    Branch = keyBranch,
                    AheadCount = ahead,
                    BehindCount = behind
                });
            }

            KeyBranchStatuses = new ObservableCollection<KeyBranchStatus>(statuses);
            HasKeyBranches = statuses.Count > 0;
        }
        catch
        {
            // If anything fails, just clear the key branches section
            KeyBranchStatuses.Clear();
            HasKeyBranches = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyBranchFilter();
    }

    private void ApplyBranchFilter()
    {
        var searchTextLower = SearchText.ToLower();

        IEnumerable<GitBranch> filtered = _allBranches;
        if (!string.IsNullOrEmpty(searchTextLower))
        {
            filtered = _allBranches.Where(b =>
                b.Name.ToLower().Contains(searchTextLower) ||
                b.ShortName.ToLower().Contains(searchTextLower) ||
                (b.IssueNumber?.ToLower().Contains(searchTextLower) ?? false));
        }

        // Group branches by type
        var view = new CollectionViewSource { Source = filtered.ToList() }.View;
        view.GroupDescriptions.Add(new PropertyGroupDescription("TypeGroup"));

        Branches = new ObservableCollection<GitBranch>(view.Cast<GitBranch>());

        // Show/hide empty state
        StatusMessage = Branches.Any() ? string.Empty : "No branches found matching your search.";

        // Select first item if any (or current branch if present)
        SelectedBranch = Branches.FirstOrDefault(b => b.IsCurrent) ?? Branches.FirstOrDefault();

        UpdateBranchButtonsCanExecute();
    }

    partial void OnSelectedBranchChanged(GitBranch? value)
    {
        UpdateBranchButtonsCanExecute();
    }

    private void UpdateBranchButtonsCanExecute()
    {
        CheckoutBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        ResetToSelectedBranchCommand.NotifyCanExecuteChanged();
        FastForwardToSelectedBranchCommand.NotifyCanExecuteChanged();
        RebaseOntoSelectedBranchCommand.NotifyCanExecuteChanged();
        CompareWithSelectedBranchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCheckoutBranch))]
    private async Task CheckoutBranchAsync()
    {
        if (SelectedBranch == null || SelectedBranch.IsCurrent || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        var branchToCheckout = SelectedBranch;
        using var toast = _toastService.ShowProgress($"Switching to {branchToCheckout.ShortName}...");

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CheckoutBranchAsync(
                CurrentBranchWorkingDirectory,
                branchToCheckout.Name,
                branchToCheckout.IsRemote);

            if (result.Success)
            {
                toast.Complete($"Switched to {branchToCheckout.ShortName}");
                IsOpen = false;
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                toast.Fail("Checkout failed");
                _dialogService.ShowWarning(string.Format("Failed to switch branch:\n{0}", result.Error), "Git Checkout");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanCheckoutBranch() => SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        // var branchName = ShowInputDialog("Create New Branch", "Enter branch name:", ""); // Commented out
        // For now, this will just call to the service with a placeholder.
        // A proper solution would be to make an IInputBoxService and inject it.
        var branchName = "new-branch-from-test"; // Placeholder

        if (string.IsNullOrWhiteSpace(branchName))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateBranchAsync(CurrentBranchWorkingDirectory, branchName);

            if (result.Success)
            {
                IsOpen = false;
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning(string.Format("Failed to create branch:\n{0}", result.Error), "Git Branch");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteBranch))]
    private async Task DeleteBranchAsync()
    {
        if (SelectedBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        if (SelectedBranch.IsCurrent)
        {
            _dialogService.ShowWarning(
                "Cannot delete the current branch. Please switch to another branch first.",
                "Git Branch");
            return;
        }

        var branchToDelete = SelectedBranch;

        IsLoading = true;
        try
        {
            if (branchToDelete.IsRemote)
            {
                if (!_dialogService.ShowConfirmation(
                    string.Format("Are you sure you want to delete the remote branch '{0}'?\n\nThis action CANNOT be undone and will affect other developers.", branchToDelete.Name),
                    "Delete Remote Branch"))
                    return;

                using var toast = _toastService.ShowProgress($"Deleting {branchToDelete.ShortName}...");
                var remoteName = branchToDelete.RemoteName ?? "origin";
                var result = await _gitStatusService.DeleteRemoteBranchAsync(
                    CurrentBranchWorkingDirectory,
                    remoteName,
                    branchToDelete.ShortName);

                if (result.Success)
                {
                    toast.Complete($"Deleted {branchToDelete.ShortName}");
                    await RefreshGitBranchesAsync();
                    await RefreshTerminalGitStatusAsync();
                }
                else
                {
                    toast.Fail("Delete failed");
                    _dialogService.ShowWarning(string.Format("Failed to delete remote branch:\n{0}", result.Error), "Git Branch");
                }
            }
            else
            {
                if (!_dialogService.ShowConfirmation(
                    string.Format("Delete local branch '{0}'?", branchToDelete.Name),
                    "Delete Branch"))
                    return;

                using var toast = _toastService.ShowProgress($"Deleting {branchToDelete.ShortName}...");
                var result = await _gitStatusService.DeleteBranchAsync(CurrentBranchWorkingDirectory, branchToDelete.Name);

                if (!result.Success && result.Error?.Contains("not fully merged") == true)
                {
                    toast.Close();
                    if (_dialogService.ShowConfirmation(
                        string.Format("Branch '{0}' is not fully merged.\n\nDo you want to force delete it? This may result in lost commits.", branchToDelete.Name),
                        "Force Delete?"))
                    {
                        result = await _gitStatusService.DeleteBranchAsync(CurrentBranchWorkingDirectory, branchToDelete.Name, force: true);
                        if (result.Success)
                        {
                            _toastService.Show($"Deleted {branchToDelete.ShortName} (force)", ToastType.Success);
                        }
                    }
                }

                if (result.Success)
                {
                    await RefreshGitBranchesAsync();
                    await RefreshTerminalGitStatusAsync();
                }
                else if (result.Error?.Contains("not fully merged") != true)
                {
                    _toastService.Show($"Delete failed: {result.Error}", ToastType.Error);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteBranch() => SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    [RelayCommand]
    private async Task FetchAllAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        using var toast = _toastService.ShowProgress("Fetching all remotes...");

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.FetchAllAsync(CurrentBranchWorkingDirectory);

            if (result.Success)
            {
                toast.Complete("Fetch complete");
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                toast.Fail($"Fetch failed: {result.Error}");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        using var toast = _toastService.ShowProgress("Pulling changes...");

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.PullAsync(CurrentBranchWorkingDirectory);

            if (result.Success)
            {
                toast.Complete("Pull complete");
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                toast.Fail($"Pull failed: {result.Error}");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    #region Branch Operations (Reset, Fast-Forward, Rebase, Compare)

    /// <summary>
    /// Reset current branch to the selected branch (hard reset).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResetToSelectedBranch))]
    private async Task ResetToSelectedBranchAsync()
    {
        if (SelectedBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        var currentBranch = _allBranches.FirstOrDefault(b => b.IsCurrent);
        if (currentBranch == null) return;

        var message = $"WARNING: This will HARD RESET '{currentBranch.ShortName}' to match '{SelectedBranch.ShortName}'.\n\n" +
                      "Any uncommitted changes will be LOST.\n" +
                      "Any commits unique to the current branch will become unreachable.\n\n" +
                      "This is a destructive operation. Continue?";

        if (!_dialogService.ShowConfirmation(message, "Reset Branch"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.ResetAsync(
                CurrentBranchWorkingDirectory,
                SelectedBranch.Name,
                ResetMode.Hard);

            if (result.Success)
            {
                _toastService.Show($"Reset {currentBranch.ShortName} to {SelectedBranch.ShortName}", ToastType.Success);
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Reset failed: {result.Error}", "Git Reset");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanResetToSelectedBranch() =>
        SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    /// <summary>
    /// Fast-forward current branch to the selected branch.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFastForwardToSelectedBranch))]
    private async Task FastForwardToSelectedBranchAsync()
    {
        if (SelectedBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        var currentBranch = _allBranches.FirstOrDefault(b => b.IsCurrent);
        if (currentBranch == null) return;

        // Check if fast-forward is possible
        var (canFF, commitCount, error) = await _gitStatusService.CheckFastForwardAsync(
            CurrentBranchWorkingDirectory,
            SelectedBranch.Name);

        if (!canFF)
        {
            _dialogService.ShowWarning(
                $"Cannot fast-forward '{currentBranch.ShortName}' to '{SelectedBranch.ShortName}':\n\n{error}",
                "Fast-Forward Not Possible");
            return;
        }

        var message = $"Fast-forward '{currentBranch.ShortName}' to '{SelectedBranch.ShortName}'?\n\n" +
                      $"This will move the branch pointer forward by {commitCount} commit(s).";

        if (!_dialogService.ShowConfirmation(message, "Fast-Forward Branch"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.FastForwardAsync(
                CurrentBranchWorkingDirectory,
                SelectedBranch.Name);

            if (result.Success)
            {
                _toastService.Show($"Fast-forwarded {currentBranch.ShortName} ({commitCount} commits)", ToastType.Success);
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Fast-forward failed: {result.Error}", "Git Fast-Forward");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanFastForwardToSelectedBranch() =>
        SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    /// <summary>
    /// Rebase current branch onto the selected branch.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRebaseOntoSelectedBranch))]
    private async Task RebaseOntoSelectedBranchAsync()
    {
        if (SelectedBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        var currentBranch = _allBranches.FirstOrDefault(b => b.IsCurrent);
        if (currentBranch == null) return;

        var message = $"Rebase '{currentBranch.ShortName}' onto '{SelectedBranch.ShortName}'?\n\n" +
                      "This will replay your commits on top of the selected branch.\n" +
                      "If there are conflicts, you'll need to resolve them.";

        if (!_dialogService.ShowConfirmation(message, "Rebase Branch"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.RebaseAsync(
                CurrentBranchWorkingDirectory,
                SelectedBranch.Name);

            if (result.Success)
            {
                _toastService.Show($"Rebased {currentBranch.ShortName} onto {SelectedBranch.ShortName}", ToastType.Success);
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                // Check if rebase is in progress (conflicts)
                var isRebaseInProgress = await _gitStatusService.IsRebaseInProgressAsync(CurrentBranchWorkingDirectory);
                if (isRebaseInProgress)
                {
                    _dialogService.ShowWarning(
                        "Rebase has conflicts that need to be resolved.\n\n" +
                        "Resolve conflicts in the terminal, then use:\n" +
                        "  git rebase --continue (after resolving)\n" +
                        "  git rebase --abort (to cancel)",
                        "Rebase Conflicts");
                }
                else
                {
                    _dialogService.ShowWarning($"Rebase failed: {result.Error}", "Git Rebase");
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRebaseOntoSelectedBranch() =>
        SelectedBranch != null && !SelectedBranch.IsCurrent && !IsLoading;

    /// <summary>
    /// Open branch comparison view for current vs selected branch.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompareWithSelectedBranch))]
    private void CompareWithSelectedBranch()
    {
        if (SelectedBranch == null) return;

        var currentBranch = _allBranches.FirstOrDefault(b => b.IsCurrent);
        if (currentBranch == null) return;

        // Raise event for MainWindow to handle
        CompareBranchesRequested?.Invoke(this, new CompareBranchesRequestedEventArgs(
            CurrentBranchWorkingDirectory,
            currentBranch.ShortName,
            SelectedBranch.ShortName));

        IsOpen = false; // Close branch switcher
    }

    private bool CanCompareWithSelectedBranch() =>
        SelectedBranch != null && !SelectedBranch.IsCurrent;

    #endregion

    #region Key Branch Operations

    /// <summary>
    /// Fast-forward current branch to the specified key branch.
    /// </summary>
    [RelayCommand]
    private async Task FastForwardToKeyBranchAsync(KeyBranchStatus keyBranchStatus)
    {
        if (keyBranchStatus?.Branch == null || CurrentBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        if (!keyBranchStatus.CanFastForward)
        {
            _dialogService.ShowWarning(
                $"Cannot fast-forward: {CurrentBranch.ShortName} has commits not in {keyBranchStatus.Branch.ShortName}.\n\n" +
                "Use Reset if you want to discard those commits, or Rebase to replay them.",
                "Fast-Forward Not Possible");
            return;
        }

        var message = $"Fast-forward '{CurrentBranch.ShortName}' to '{keyBranchStatus.Branch.ShortName}'?\n\n" +
                      $"This will move {CurrentBranch.ShortName} forward by {keyBranchStatus.BehindCount} commit(s).";

        if (!_dialogService.ShowConfirmation(message, "Fast-Forward"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.FastForwardAsync(
                CurrentBranchWorkingDirectory,
                keyBranchStatus.Branch.Name);

            if (result.Success)
            {
                _toastService.Show($"Fast-forwarded to {keyBranchStatus.Branch.ShortName}", ToastType.Success);
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Fast-forward failed: {result.Error}", "Git Fast-Forward");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Reset current branch to the specified key branch.
    /// </summary>
    [RelayCommand]
    private async Task ResetToKeyBranchAsync(KeyBranchStatus keyBranchStatus)
    {
        if (keyBranchStatus?.Branch == null || CurrentBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        var warningText = keyBranchStatus.AheadCount > 0
            ? $"\n\nWARNING: You will lose {keyBranchStatus.AheadCount} commit(s) that are only in {CurrentBranch.ShortName}!"
            : "";

        var message = $"Reset '{CurrentBranch.ShortName}' to '{keyBranchStatus.Branch.ShortName}'?{warningText}\n\n" +
                      "This is a hard reset - uncommitted changes will be lost.";

        if (!_dialogService.ShowConfirmation(message, "Reset Branch"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.ResetAsync(
                CurrentBranchWorkingDirectory,
                keyBranchStatus.Branch.Name,
                ResetMode.Hard);

            if (result.Success)
            {
                _toastService.Show($"Reset {CurrentBranch.ShortName} to {keyBranchStatus.Branch.ShortName}", ToastType.Success);
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Reset failed: {result.Error}", "Git Reset");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Compare current branch with the specified key branch.
    /// </summary>
    [RelayCommand]
    private void CompareWithKeyBranch(KeyBranchStatus keyBranchStatus)
    {
        if (keyBranchStatus?.Branch == null || CurrentBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        CompareBranchesRequested?.Invoke(this, new CompareBranchesRequestedEventArgs(
            CurrentBranchWorkingDirectory,
            CurrentBranch.ShortName,
            keyBranchStatus.Branch.ShortName));
    }

    /// <summary>
    /// Fast-forward the key branch to match current branch (push key branch forward).
    /// This is the inverse of FastForwardToKeyBranch - it updates the key branch to include commits from current.
    /// </summary>
    [RelayCommand]
    private async Task FastForwardKeyBranchToCurrentAsync(KeyBranchStatus keyBranchStatus)
    {
        if (keyBranchStatus?.Branch == null || CurrentBranch == null || string.IsNullOrEmpty(CurrentBranchWorkingDirectory))
            return;

        if (!keyBranchStatus.CanFastForwardKeyToCurrent)
        {
            _dialogService.ShowWarning(
                $"Cannot fast-forward {keyBranchStatus.Branch.ShortName} to {CurrentBranch.ShortName}:\n\n" +
                $"{keyBranchStatus.Branch.ShortName} has {keyBranchStatus.BehindCount} commit(s) not in {CurrentBranch.ShortName}.\n\n" +
                "The branches have diverged. Use Reset to forcefully update the branch.",
                "Fast-Forward Not Possible");
            return;
        }

        var message = $"Fast-forward '{keyBranchStatus.Branch.ShortName}' to '{CurrentBranch.ShortName}'?\n\n" +
                      $"This will move {keyBranchStatus.Branch.ShortName} forward by {keyBranchStatus.AheadCount} commit(s) to match {CurrentBranch.ShortName}.";

        if (!_dialogService.ShowConfirmation(message, "Fast-Forward Branch"))
            return;

        using var toast = _toastService.ShowProgress($"Updating {keyBranchStatus.Branch.ShortName}...");

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.UpdateBranchPointerAsync(
                CurrentBranchWorkingDirectory,
                keyBranchStatus.Branch.Name,
                CurrentBranch.Name);

            if (result.Success)
            {
                toast.Complete($"{keyBranchStatus.Branch.ShortName} updated to {CurrentBranch.ShortName}");
                await RefreshGitBranchesAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                toast.Fail("Update failed");
                _dialogService.ShowWarning($"Failed to update {keyBranchStatus.Branch.ShortName}: {result.Error}", "Git Branch Update");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    private async Task RefreshTerminalGitStatusAsync()
    {
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            try
            {
                var status = await _gitStatusService.GetGitStatusAsync(terminalTab.Pair.WorkingDirectory);
                terminalTab.GitStatus = status;
            }
            catch
            {
                // Silently ignore git status errors
            }
        }
    }

    // Commented out to resolve compilation errors, requires a proper injectable solution
    // private static string? ShowInputDialog(string title, string prompt, string defaultValue)
    // {
    //     // This method should ideally be replaced by a proper dialog service
    //     // For now, mirroring the existing implementation from MainWindow
    //     var dialog = new Window
    //     {
    //         Title = title,
    //         Width = 400,
    //         Height = 150,
    //         WindowStartupLocation = WindowStartupLocation.CenterScreen,
    //         WindowStyle = WindowStyle.ToolWindow,
    //         ResizeMode = ResizeMode.NoResize,
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25))
    //     };

    //     var panel = new StackPanel { Margin = new Thickness(16) };

    //     var label = new TextBlock
    //     {
    //         Text = prompt,
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
    //         Margin = new Thickness(0, 0, 0, 8)
    //     };
    //     panel.Children.Add(label);

    //     var textBox = new TextBox
    //     {
    //         Text = defaultValue,
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
    //         BorderBrush = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
    //         Padding = new Thickness(8, 6, 8, 6),
    //         Margin = new Thickness(0, 0, 0, 16)
    //     };
    //     panel.Children.Add(textBox);

    //     var buttonPanel = new StackPanel
    //     {
    //         Orientation = Orientation.Horizontal,
    //         HorizontalAlignment = HorizontalAlignment.Right
    //     };

    //     var okButton = new Button
    //     {
    //         Content = "Create",
    //         Width = 80,
    //         Padding = new Thickness(8, 4, 8, 4),
    //         Margin = new Thickness(0, 0, 8, 0),
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
    //         BorderThickness = new Thickness(0),
    //         IsDefault = true
    //     };
    //     okButton.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
    //     buttonPanel.Children.Add(okButton);

    //     var cancelButton = new Button
    //     {
    //         Content = "Cancel",
    //         Width = 80,
    //         Padding = new Thickness(8, 4, 8, 4),
    //         Background = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x3C)),
    //         Foreground = new System.Windows.Media.SolidColorBrush(
    //             System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
    //         BorderThickness = new Thickness(0),
    //         IsCancel = true
    //     };
    //     cancelButton.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
    //     buttonPanel.Children.Add(cancelButton);

    //     panel.Children.Add(buttonPanel);

    //     dialog.Content = panel;

    //     textBox.Focus();
    //     textBox.SelectAll();

    //     return dialog.ShowDialog() == true ? textBox.Text : null;
    // }
}

/// <summary>
/// Event arguments for branch comparison request.
/// </summary>
public class CompareBranchesRequestedEventArgs : EventArgs
{
    public string WorkingDirectory { get; }
    public string BaseBranch { get; }
    public string CompareBranch { get; }

    public CompareBranchesRequestedEventArgs(string workingDirectory, string baseBranch, string compareBranch)
    {
        WorkingDirectory = workingDirectory;
        BaseBranch = baseBranch;
        CompareBranch = compareBranch;
    }
}
