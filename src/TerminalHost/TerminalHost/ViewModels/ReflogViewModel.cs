using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Git Reflog panel (Ctrl+Shift+G).
/// Allows viewing and recovering from reflog entries.
/// </summary>
public partial class ReflogViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private TerminalPairTabViewModel? _currentTerminalTab;
    private const int DefaultCount = 50;

    #region IPanelableViewModel Implementation

    public override string PanelId => "reflog";
    public override string PanelTitle => "Git Reflog";
    public override string PanelIcon => "R";
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    #endregion

    #region Properties

    [ObservableProperty]
    private ObservableCollection<GitReflogEntry> _entries = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBranchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyHashCommand))]
    private GitReflogEntry? _selectedEntry;

    [ObservableProperty]
    private string _title = "Git Reflog";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _newBranchName = "";

    public bool HasSelectedEntry => SelectedEntry != null;
    public bool HasEntries => Entries.Count > 0;

    #endregion

    public ReflogViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;

        DisplayState = PanelDisplayState.Popup;
        Width = 700;
        Height = 500;
    }

    #region Overrides

    protected override void OnClose()
    {
        SelectedEntry = null;
        NewBranchName = "";
        _currentTerminalTab = null;
        base.OnClose();
    }

    #endregion

    #region Public Methods

    public void SetTerminalTab(TerminalPairTabViewModel? tab)
    {
        _currentTerminalTab = tab;
        Title = tab != null ? $"Reflog - {tab.Title}" : "Git Reflog";
    }

    public async Task LoadAsync()
    {
        if (_currentTerminalTab == null) return;

        IsLoading = true;
        try
        {
            var workDir = _currentTerminalTab.Pair.WorkingDirectory;
            var entries = await _gitStatusService.GetReflogAsync(workDir, DefaultCount);

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            Info = $"{Entries.Count} entries";
            OnPropertyChanged(nameof(HasEntries));
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private async Task CheckoutAsync()
    {
        if (SelectedEntry == null || _currentTerminalTab == null) return;

        var confirm = _dialogService.ShowConfirmation(
            $"Checkout {SelectedEntry.Selector}?\n\nThis will checkout commit {SelectedEntry.ShortHash}.\nYou will be in a detached HEAD state.",
            "Checkout Commit");

        if (!confirm) return;

        var workDir = _currentTerminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.CheckoutBranchAsync(workDir, SelectedEntry.Hash);

        if (result.Success)
        {
            _toastService.Show($"Checked out {SelectedEntry.ShortHash}", ToastType.Success);
            IsOpen = false;
        }
        else
        {
            _toastService.Show($"Checkout failed: {result.Error}", ToastType.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private async Task CreateBranchAsync()
    {
        if (SelectedEntry == null || _currentTerminalTab == null) return;

        if (string.IsNullOrWhiteSpace(NewBranchName))
        {
            _toastService.Show("Enter a branch name", ToastType.Warning);
            return;
        }

        var workDir = _currentTerminalTab.Pair.WorkingDirectory;
        var result = await _gitStatusService.CreateBranchFromRefAsync(workDir, NewBranchName.Trim(), SelectedEntry.Selector);

        if (result.Success)
        {
            _toastService.Show($"Created branch '{NewBranchName}'", ToastType.Success);
            NewBranchName = "";
        }
        else
        {
            _toastService.Show($"Failed to create branch: {result.Error}", ToastType.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void CopyHash()
    {
        if (SelectedEntry == null) return;

        try
        {
            System.Windows.Clipboard.SetText(SelectedEntry.Hash);
            _toastService.Show("Hash copied", ToastType.Success);
        }
        catch
        {
            _toastService.Show("Failed to copy", ToastType.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    #endregion
}
