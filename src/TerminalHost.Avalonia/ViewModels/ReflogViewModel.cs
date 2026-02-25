using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class ReflogViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;
    private readonly IConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly MainViewModel _mainViewModel;
    private readonly IAiExecutionService _aiExecutionService;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private GitReflogEntry? _selectedEntry;

    [ObservableProperty]
    private string? _aiExplanation;

    [ObservableProperty]
    private bool _isAiLoading;

    public ObservableCollection<GitReflogEntry> Entries { get; } = [];

    public ReflogViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IToastService toastService,
        IConfigurationService configurationService,
        IProcessService processService,
        MainViewModel mainViewModel,
        IAiExecutionService aiExecutionService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _toastService = toastService;
        _configurationService = configurationService;
        _processService = processService;
        _mainViewModel = mainViewModel;
        _aiExecutionService = aiExecutionService;
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        // Get working directory from current tab
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            WorkingDirectory = terminalTab.WorkingDirectory;
        }
        else
        {
            _toastService.Show("No project tab selected", ToastType.Warning);
            return;
        }

        Entries.Clear();
        SelectedEntry = null;

        IsOpen = true;
        await LoadReflogAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private async Task LoadReflogAsync()
    {
        if (IsLoading || string.IsNullOrEmpty(WorkingDirectory))
            return;

        IsLoading = true;

        try
        {
            var entries = await _gitStatusService.GetReflogAsync(WorkingDirectory, 50);

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to load reflog: {ex.Message}", "Error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (SelectedEntry == null) return;

        var confirm = _dialogService.ShowConfirmation(
            $"Checkout to {SelectedEntry.ShortHash}?\n\nThis will reset your HEAD to this commit.",
            "Checkout");

        if (!confirm) return;

        try
        {
            var result = await _gitStatusService.CheckoutBranchAsync(WorkingDirectory, SelectedEntry.Hash);

            if (result.Success)
            {
                _toastService.Show($"Checked out to {SelectedEntry.ShortHash}", ToastType.Success);
                Close();
            }
            else
            {
                _dialogService.ShowError(result.Error ?? "Failed to checkout", "Error");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to checkout: {ex.Message}", "Error");
        }
    }

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        if (SelectedEntry == null) return;

        var branchName = _dialogService.ShowInput(
            $"Create a new branch from {SelectedEntry.ShortHash}:",
            "Create Branch",
            "new-branch");

        if (string.IsNullOrWhiteSpace(branchName)) return;

        try
        {
            var result = await _gitStatusService.CreateBranchFromRefAsync(
                WorkingDirectory, SelectedEntry.Hash, branchName.Trim());

            if (result.Success)
            {
                _toastService.Show($"Created and checked out branch '{branchName}'", ToastType.Success);
                Close();
            }
            else
            {
                _dialogService.ShowError(result.Error ?? "Failed to create branch", "Error");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to create branch: {ex.Message}", "Error");
        }
    }

    [RelayCommand]
    private async Task CopyHashAsync()
    {
        if (SelectedEntry == null) return;

        try
        {
            await _clipboardService.SetTextAsync(SelectedEntry.Hash);
            _toastService.Show("Hash copied to clipboard", ToastType.Success);
        }
        catch
        {
            // Clipboard access can fail silently
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadReflogAsync();
    }

    [RelayCommand]
    private void DismissAiExplanation()
    {
        AiExplanation = null;
    }

    [RelayCommand]
    private async Task ExplainReflogAsync()
    {
        if (Entries.Count == 0) return;
        if (!_aiExecutionService.IsAiAvailable()) return;

        IsAiLoading = true;
        try
        {
            var entries = string.Join("\n", Entries.Take(20).Select(e => $"- {e.ShortHash}: {e.Action} {e.Description}"));
            var prompt = $"You are a git expert. Explain in plain language.\n\nExplain what happened in these recent git operations:\n{entries}";
            var result = await _aiExecutionService.ExecuteAsync(prompt, WorkingDirectory, "Explaining reflog", timeout: TimeSpan.FromSeconds(60));

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                AiExplanation = result.Output.Trim();
        }
        finally { IsAiLoading = false; }
    }

    [RelayCommand]
    private async Task CopyAiExplanationAsync()
    {
        if (!string.IsNullOrEmpty(AiExplanation))
        {
            await _clipboardService.SetTextAsync(AiExplanation);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }
}
