using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitStashViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly IToastService _toastService;
    private readonly IAiExecutionService _aiExecutionService;

    [ObservableProperty]
    private string _currentWorkingDirectory = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitStashEntry> _stashes = [];

    [ObservableProperty]
    private GitStashEntry? _selectedStash;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _title = "Git Stash";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Create stash form
    [ObservableProperty]
    private string _stashMessage = string.Empty;

    [ObservableProperty]
    private bool _includeUntracked;

    // Branch from stash form
    [ObservableProperty]
    private string _newBranchName = string.Empty;

    [ObservableProperty]
    private bool _isCreatingBranch;

    // AI properties
    [ObservableProperty]
    private bool _isAiLoading;

    // View properties for popup sizing
    [ObservableProperty]
    private double _width = 550;

    [ObservableProperty]
    private double _height = 500;

    public GitStashViewModel(
        IGitStatusService gitStatusService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IConfigurationService configurationService,
        IProcessService processService,
        IToastService toastService,
        IAiExecutionService aiExecutionService)
    {
        _gitStatusService = gitStatusService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _configurationService = configurationService;
        _processService = processService;
        _toastService = toastService;
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

        await LoadDataAsync(terminalTab);

        IsOpen = true;
    }

    /// <summary>
    /// Loads stash data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        CurrentWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Git Stash - {terminalTab.Title}";
        StatusMessage = string.Empty;
        StashMessage = string.Empty;
        IncludeUntracked = false;
        NewBranchName = string.Empty;
        IsCreatingBranch = false;

        await RefreshStashListAsync();
    }

    [RelayCommand]
    private async Task RefreshStashListAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var stashList = await _gitStatusService.GetStashListAsync(CurrentWorkingDirectory);
            Stashes = new ObservableCollection<GitStashEntry>(stashList);

            StatusMessage = Stashes.Count == 0 ? "No stashes found." : string.Empty;
            SelectedStash = Stashes.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedStashChanged(GitStashEntry? value)
    {
        UpdateStashButtonsCanExecute();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        UpdateStashButtonsCanExecute();
    }

    private void UpdateStashButtonsCanExecute()
    {
        ApplyStashCommand.NotifyCanExecuteChanged();
        PopStashCommand.NotifyCanExecuteChanged();
        DropStashCommand.NotifyCanExecuteChanged();
        ShowBranchFromStashCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateStashAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateStashAsync(
                CurrentWorkingDirectory,
                string.IsNullOrWhiteSpace(StashMessage) ? null : StashMessage,
                IncludeUntracked);

            if (result.Success)
            {
                StashMessage = string.Empty;
                IncludeUntracked = false;
                await RefreshStashListAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Failed to create stash:\n{result.Error}", "Git Stash");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnStash))]
    private async Task ApplyStashAsync()
    {
        if (SelectedStash == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.ApplyStashAsync(
                CurrentWorkingDirectory,
                SelectedStash.Index);

            if (result.Success)
            {
                await RefreshTerminalGitStatusAsync();
                // Don't close - user might want to drop after confirming apply worked
            }
            else
            {
                _dialogService.ShowWarning($"Failed to apply stash:\n{result.Error}", "Git Stash");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnStash))]
    private async Task PopStashAsync()
    {
        if (SelectedStash == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.PopStashAsync(
                CurrentWorkingDirectory,
                SelectedStash.Index);

            if (result.Success)
            {
                await RefreshStashListAsync();
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Failed to pop stash:\n{result.Error}", "Git Stash");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnStash))]
    private async Task DropStashAsync()
    {
        if (SelectedStash == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        if (!_dialogService.ShowConfirmation(
            $"Are you sure you want to drop '{SelectedStash.DisplayTitle}'?\n\nThis action cannot be undone.",
            "Drop Stash"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.DropStashAsync(
                CurrentWorkingDirectory,
                SelectedStash.Index);

            if (result.Success)
            {
                await RefreshStashListAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Failed to drop stash:\n{result.Error}", "Git Stash");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnStash))]
    private void ShowBranchFromStash()
    {
        if (SelectedStash == null)
            return;

        NewBranchName = $"stash-{SelectedStash.Index}";
        IsCreatingBranch = true;
    }

    [RelayCommand]
    private void CancelBranchFromStash()
    {
        IsCreatingBranch = false;
        NewBranchName = string.Empty;
    }

    [RelayCommand]
    private async Task CreateBranchFromStashAsync()
    {
        if (SelectedStash == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        if (string.IsNullOrWhiteSpace(NewBranchName))
        {
            _dialogService.ShowWarning("Please enter a branch name.", "Git Stash");
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateBranchFromStashAsync(
                CurrentWorkingDirectory,
                NewBranchName.Trim(),
                SelectedStash.Index);

            if (result.Success)
            {
                IsCreatingBranch = false;
                NewBranchName = string.Empty;
                IsOpen = false;
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Failed to create branch from stash:\n{result.Error}", "Git Stash");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanOperateOnStash() => SelectedStash != null && !IsLoading;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        IsCreatingBranch = false;
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

    [RelayCommand]
    private async Task GenerateStashNameAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory)) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        IsAiLoading = true;
        try
        {
            // Get current diff to describe
            var diff = await _gitStatusService.GetStagedDiffAsync(CurrentWorkingDirectory);
            if (string.IsNullOrEmpty(diff))
            {
                // Fall back to unstaged diff
                var (exitCode, output, _) = await _processService.RunAsync(
                    "git", "diff", CurrentWorkingDirectory, timeout: TimeSpan.FromSeconds(10));
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    diff = output;
            }
            if (string.IsNullOrEmpty(diff))
            {
                _toastService.Show("No changes to describe", ToastType.Warning);
                return;
            }

            if (diff.Length > 20_000) diff = diff[..20_000] + "\n\n[... diff truncated ...]";

            var prompt = $"Generate a short descriptive name (3-6 words) for this stash. Output ONLY the name, nothing else.\n\n{diff}";
            var result = await _aiExecutionService.ExecuteAsync(prompt, CurrentWorkingDirectory, "Generating stash name");

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                StashMessage = result.Output.Trim().Trim('"', '\'');
        }
        finally { IsAiLoading = false; }
    }
}
