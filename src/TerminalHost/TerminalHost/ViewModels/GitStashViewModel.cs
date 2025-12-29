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
    private readonly IToastService _toastService;

    private TerminalPairTabViewModel? _currentTerminalTab;

    [ObservableProperty]
    private string _currentWorkingDirectory = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitStashEntry> _stashEntries = [];

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

    // Create stash options
    [ObservableProperty]
    private string _stashMessage = string.Empty;

    [ObservableProperty]
    private bool _includeUntracked;

    // View properties for positioning/sizing the popup
    [ObservableProperty]
    private double _width = 600;

    [ObservableProperty]
    private double _height = 500;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    public GitStashViewModel(
        IGitStatusService gitStatusService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _toastService = toastService;
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            _dialogService.ShowInfo("Please select a project tab first.", "Git Stash");
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
        _currentTerminalTab = terminalTab;
        CurrentWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Git Stash - {terminalTab.Title}";
        StatusMessage = string.Empty;
        StashMessage = string.Empty;
        IncludeUntracked = false;

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
            var stashes = await _gitStatusService.GetStashListAsync(CurrentWorkingDirectory);
            StashEntries = new ObservableCollection<GitStashEntry>(stashes);

            StatusMessage = StashEntries.Count == 0 ? "No stashes found." : string.Empty;
            SelectedStash = StashEntries.FirstOrDefault();

            UpdateCommandsCanExecute();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedStashChanged(GitStashEntry? value)
    {
        UpdateCommandsCanExecute();
    }

    private void UpdateCommandsCanExecute()
    {
        ApplyStashCommand.NotifyCanExecuteChanged();
        PopStashCommand.NotifyCanExecuteChanged();
        DropStashCommand.NotifyCanExecuteChanged();
        CreateBranchFromStashCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateStashAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var message = string.IsNullOrWhiteSpace(StashMessage) ? null : StashMessage.Trim();
            var result = await _gitStatusService.CreateStashAsync(CurrentWorkingDirectory, message, IncludeUntracked);

            if (result.Success)
            {
                _toastService.Show("Changes stashed", ToastType.Success);
                StashMessage = string.Empty;
                await RefreshStashListAsync();
                await RefreshTerminalGitStatusAsync();
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
                    _dialogService.ShowWarning($"Failed to create stash:\n{error}", "Git Stash");
                }
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
            var result = await _gitStatusService.ApplyStashAsync(CurrentWorkingDirectory, SelectedStash.Index);

            if (result.Success)
            {
                _toastService.Show($"Applied {SelectedStash.StashRef}", ToastType.Success);
                await RefreshTerminalGitStatusAsync();
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
            var result = await _gitStatusService.PopStashAsync(CurrentWorkingDirectory, SelectedStash.Index);

            if (result.Success)
            {
                _toastService.Show($"Popped {SelectedStash.StashRef}", ToastType.Success);
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
            $"Are you sure you want to drop {SelectedStash.StashRef}?\n\nThis action cannot be undone.",
            "Drop Stash"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.DropStashAsync(CurrentWorkingDirectory, SelectedStash.Index);

            if (result.Success)
            {
                _toastService.Show($"Dropped {SelectedStash.StashRef}", ToastType.Success);
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
    private async Task CreateBranchFromStashAsync()
    {
        if (SelectedStash == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        // For now, use a simple input approach - could be enhanced with IInputDialogService
        var defaultBranchName = $"stash-{SelectedStash.Index}";

        // Show a simple confirmation with the default branch name
        if (!_dialogService.ShowConfirmation(
            $"Create a new branch from {SelectedStash.StashRef}?\n\nBranch name: {defaultBranchName}\n\nThis will apply the stash and delete it.",
            "Create Branch from Stash"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.CreateBranchFromStashAsync(CurrentWorkingDirectory, defaultBranchName, SelectedStash.Index);

            if (result.Success)
            {
                _toastService.Show($"Created branch '{defaultBranchName}'", ToastType.Success);
                await RefreshStashListAsync();
                await RefreshTerminalGitStatusAsync();
                IsOpen = false;
            }
            else
            {
                _dialogService.ShowWarning($"Failed to create branch:\n{result.Error}", "Git Stash");
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
    }

    private async Task RefreshTerminalGitStatusAsync()
    {
        if (_currentTerminalTab != null)
        {
            try
            {
                var status = await _gitStatusService.GetGitStatusAsync(_currentTerminalTab.Pair.WorkingDirectory);
                _currentTerminalTab.GitStatus = status;
            }
            catch
            {
                // Silently ignore git status errors
            }
        }
    }
}
