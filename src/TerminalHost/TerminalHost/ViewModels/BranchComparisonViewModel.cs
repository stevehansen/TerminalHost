using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Branch Comparison panel.
/// Allows comparing two branches to see commits and files that differ.
/// </summary>
public partial class BranchComparisonViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly IConfigurationService _configurationService;
    private readonly MainViewModel _mainViewModel;
    private TerminalPairTabViewModel? _currentTerminalTab;

    #region IPanelableViewModel Implementation

    public override string PanelId => "branchComparison";
    public override string PanelTitle => Title;
    public override string PanelIcon => "⇄";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "↻",
            Tooltip = "Refresh comparison",
            Command = CompareCommand
        },
        new PanelHeaderCommand
        {
            Icon = "⇆",
            Tooltip = "Swap branches",
            Command = SwapBranchesCommand
        }
    ];

    #endregion

    #region Properties

    [ObservableProperty]
    private string _title = "Branch Comparison";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private ObservableCollection<GitBranch> _availableBranches = [];

    [ObservableProperty]
    private ObservableCollection<GitBranch> _keyBranches = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private GitBranch? _baseBranch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private GitBranch? _compareBranch;

    [ObservableProperty]
    private BranchComparisonResult? _comparisonResult;

    [ObservableProperty]
    private ObservableCollection<GitCommit> _commitsInBase = [];

    [ObservableProperty]
    private ObservableCollection<GitCommit> _commitsInCompare = [];

    [ObservableProperty]
    private GitCommit? _selectedCommit;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// View mode: "Commits" shows commit lists, "Files" shows file diff view.
    /// </summary>
    [ObservableProperty]
    private string _viewMode = "Commits";

    [ObservableProperty]
    private ObservableCollection<GitFileStatus> _changedFiles = [];

    [ObservableProperty]
    private GitFileStatus? _selectedFile;

    [ObservableProperty]
    private string _diffText = "";

    public bool IsCommitsView => ViewMode == "Commits";
    public bool IsFilesView => ViewMode == "Files";

    #endregion

    public BranchComparisonViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService,
        IConfigurationService configurationService,
        MainViewModel mainViewModel)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;
        _configurationService = configurationService;
        _mainViewModel = mainViewModel;

        // Default to Popup state
        DisplayState = PanelDisplayState.Popup;
        Width = 1100;
        Height = 700;
    }

    #region Commands

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        await LoadDataAsync(terminalTab);
        IsOpen = true;
    }

    /// <summary>
    /// Loads branch comparison data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        WorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Branch Comparison - {terminalTab.Title}";

        await LoadBranchesAsync();
    }

    public async Task OpenWithBranchesAsync(TerminalPairTabViewModel terminalTab, string baseBranchName, string compareBranchName)
    {
        _currentTerminalTab = terminalTab;
        WorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Branch Comparison - {terminalTab.Title}";

        await LoadBranchesAsync();

        // Select the specified branches
        BaseBranch = AvailableBranches.FirstOrDefault(b => b.ShortName == baseBranchName || b.Name == baseBranchName);
        CompareBranch = AvailableBranches.FirstOrDefault(b => b.ShortName == compareBranchName || b.Name == compareBranchName);

        if (BaseBranch != null && CompareBranch != null)
        {
            await CompareAsync();
        }

        IsOpen = true;
    }

    private async Task LoadBranchesAsync()
    {
        IsLoading = true;
        try
        {
            var allBranches = await _gitStatusService.GetBranchesAsync(WorkingDirectory);
            AvailableBranches = new ObservableCollection<GitBranch>(allBranches.Where(b => !b.IsRemote));

            // Get key branches from config
            var config = _configurationService.Load();
            var dirSettings = config.DirectorySettings.TryGetValue(WorkingDirectory.ToLowerInvariant(), out var ds) ? ds : null;
            var keyBranchPatterns = dirSettings?.KeyBranchOverrides ?? config.Settings.KeyBranches;

            var detectedKeyBranches = await _gitStatusService.GetKeyBranchesAsync(WorkingDirectory, keyBranchPatterns);
            KeyBranches = new ObservableCollection<GitBranch>(detectedKeyBranches);

            // Default selection: current branch as base
            BaseBranch = AvailableBranches.FirstOrDefault(b => b.IsCurrent);

            // Default compare to first key branch that isn't current
            CompareBranch = KeyBranches.FirstOrDefault(b => !b.IsCurrent) ?? AvailableBranches.FirstOrDefault(b => !b.IsCurrent);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (BaseBranch == null || CompareBranch == null || string.IsNullOrEmpty(WorkingDirectory))
            return;

        IsLoading = true;
        StatusMessage = "Comparing branches...";
        try
        {
            ComparisonResult = await _gitStatusService.CompareBranchesAsync(
                WorkingDirectory,
                BaseBranch.Name,
                CompareBranch.Name);

            CommitsInBase = new ObservableCollection<GitCommit>(ComparisonResult.CommitsOnlyInBase);
            CommitsInCompare = new ObservableCollection<GitCommit>(ComparisonResult.CommitsOnlyInCompare);

            // Fetch changed files for the Files view
            var changedFiles = await _gitStatusService.GetChangedFilesBetweenBranchesAsync(
                WorkingDirectory, BaseBranch.Name, CompareBranch.Name);
            ChangedFiles = new ObservableCollection<GitFileStatus>(changedFiles);

            StatusMessage = ComparisonResult.Summary;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _dialogService.ShowWarning($"Failed to compare branches: {ex.Message}", "Branch Comparison");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanCompare() => BaseBranch != null && CompareBranch != null && BaseBranch != CompareBranch && !IsLoading;

    [RelayCommand]
    private void SwapBranches()
    {
        (BaseBranch, CompareBranch) = (CompareBranch, BaseBranch);
    }

    [RelayCommand]
    private async Task FastForwardBaseToCompareAsync()
    {
        if (ComparisonResult == null || !ComparisonResult.CanFastForwardBaseToCompare)
            return;

        if (BaseBranch == null || CompareBranch == null)
            return;

        // First check if we need to checkout the base branch
        var currentBranch = AvailableBranches.FirstOrDefault(b => b.IsCurrent);
        var needsCheckout = currentBranch?.Name != BaseBranch.Name;

        var message = needsCheckout
            ? $"This will checkout '{BaseBranch.ShortName}' and fast-forward it to '{CompareBranch.ShortName}' ({ComparisonResult.BaseBehindCount} commits).\n\nContinue?"
            : $"Fast-forward '{BaseBranch.ShortName}' to '{CompareBranch.ShortName}' ({ComparisonResult.BaseBehindCount} commits)?\n\nThis will move the branch pointer forward.";

        if (!_dialogService.ShowConfirmation(message, "Fast-Forward Branch"))
            return;

        IsLoading = true;
        try
        {
            if (needsCheckout)
            {
                var checkoutResult = await _gitStatusService.CheckoutBranchAsync(WorkingDirectory, BaseBranch.Name);
                if (!checkoutResult.Success)
                {
                    _dialogService.ShowWarning($"Failed to checkout branch: {checkoutResult.Error}", "Fast-Forward");
                    return;
                }
            }

            var result = await _gitStatusService.FastForwardAsync(WorkingDirectory, CompareBranch.Name);
            if (result.Success)
            {
                _toastService.Show($"Fast-forwarded {BaseBranch.ShortName} to {CompareBranch.ShortName}", ToastType.Success);
                await CompareAsync(); // Refresh comparison
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Fast-forward failed: {result.Error}", "Fast-Forward");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ResetBaseToCompareAsync()
    {
        if (BaseBranch == null || CompareBranch == null)
            return;

        // First check if we need to checkout the base branch
        var currentBranch = AvailableBranches.FirstOrDefault(b => b.IsCurrent);
        var needsCheckout = currentBranch?.Name != BaseBranch.Name;

        var message = needsCheckout
            ? $"WARNING: This will checkout '{BaseBranch.ShortName}' and RESET it to match '{CompareBranch.ShortName}'.\n\nAny commits unique to '{BaseBranch.ShortName}' will become unreachable.\n\nThis is a destructive operation. Continue?"
            : $"WARNING: This will RESET '{BaseBranch.ShortName}' to match '{CompareBranch.ShortName}'.\n\nAny commits unique to '{BaseBranch.ShortName}' will become unreachable.\n\nThis is a destructive operation. Continue?";

        if (!_dialogService.ShowConfirmation(message, "Reset Branch"))
            return;

        IsLoading = true;
        try
        {
            if (needsCheckout)
            {
                var checkoutResult = await _gitStatusService.CheckoutBranchAsync(WorkingDirectory, BaseBranch.Name);
                if (!checkoutResult.Success)
                {
                    _dialogService.ShowWarning($"Failed to checkout branch: {checkoutResult.Error}", "Reset Branch");
                    return;
                }
            }

            var result = await _gitStatusService.ResetAsync(WorkingDirectory, CompareBranch.Name, ResetMode.Hard);
            if (result.Success)
            {
                _toastService.Show($"Reset {BaseBranch.ShortName} to {CompareBranch.ShortName}", ToastType.Success);
                await CompareAsync(); // Refresh comparison
                await RefreshTerminalGitStatusAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Reset failed: {result.Error}", "Reset Branch");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectKeyBranchPair(string pairKey)
    {
        // pairKey format: "base|compare" e.g. "development|production"
        var parts = pairKey.Split('|');
        if (parts.Length != 2) return;

        var baseName = parts[0];
        var compareName = parts[1];

        BaseBranch = AvailableBranches.FirstOrDefault(b => b.ShortName == baseName);
        CompareBranch = AvailableBranches.FirstOrDefault(b => b.ShortName == compareName);
    }

    #endregion

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCommitsView));
        OnPropertyChanged(nameof(IsFilesView));
    }

    partial void OnSelectedFileChanged(GitFileStatus? value)
    {
        LoadFileDiffAsync(value);
    }

    private async void LoadFileDiffAsync(GitFileStatus? file)
    {
        if (file == null || BaseBranch == null || CompareBranch == null || string.IsNullOrEmpty(WorkingDirectory))
        {
            DiffText = "";
            return;
        }

        var diff = await _gitStatusService.GetFileDiffBetweenBranchesAsync(
            WorkingDirectory, BaseBranch.Name, CompareBranch.Name, file.FilePath);

        DiffText = diff ?? "";
    }

    [RelayCommand]
    private void SetViewMode(string mode)
    {
        ViewMode = mode;
    }

    #region Helpers

    private async Task RefreshTerminalGitStatusAsync()
    {
        if (_currentTerminalTab == null) return;

        try
        {
            var status = await _gitStatusService.GetGitStatusAsync(_currentTerminalTab.Pair.WorkingDirectory);
            _currentTerminalTab.GitStatus = status;

            // Also refresh workspace sidebar to keep it in sync
            if (_mainViewModel.WorkspaceSidebar != null)
            {
                await _mainViewModel.WorkspaceSidebar.RefreshGitStatusAsync(_currentTerminalTab.Pair.WorkingDirectory);
            }
        }
        catch
        {
            // Silently ignore git status errors
        }
    }

    #endregion

    #region Overrides

    protected override void OnClose()
    {
        ComparisonResult = null;
        CommitsInBase.Clear();
        CommitsInCompare.Clear();
        ChangedFiles.Clear();
        SelectedCommit = null;
        SelectedFile = null;
        DiffText = "";
        ViewMode = "Commits";
        StatusMessage = "";
        _currentTerminalTab = null;
        base.OnClose();
    }

    #endregion
}
