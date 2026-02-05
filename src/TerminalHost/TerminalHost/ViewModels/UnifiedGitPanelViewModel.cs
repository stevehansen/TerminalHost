using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// Defines the tabs available in the unified Git panel.
/// </summary>
public enum GitPanelTab
{
    Branches,
    Changes,
    History,
    Stash,
    Tags,
    Comparison
}

/// <summary>
/// Unified Git Panel ViewModel.
/// Consolidates all Git functionality into a single tabbed panel.
/// Shortcuts like Ctrl+B, Alt+G, Ctrl+H open this panel on the appropriate tab.
/// </summary>
public partial class UnifiedGitPanelViewModel : BasePanelViewModel
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;

    // Sub-ViewModels for each tab (injected)
    public GitBranchViewModel BranchesVM { get; }
    public GitFilesViewModel ChangesVM { get; }
    public CommitHistoryViewModel HistoryVM { get; }
    public GitStashViewModel StashVM { get; }
    public GitTagsViewModel TagsVM { get; }
    public BranchComparisonViewModel ComparisonVM { get; }

    private TerminalPairTabViewModel? _currentTerminalTab;

    #region IPanelableViewModel Implementation

    public override string PanelId => "unifiedGit";
    public override string PanelTitle => Title;
    public override string PanelIcon => "🔀";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "↻",
            Tooltip = "Refresh current view",
            Command = RefreshCurrentTabCommand
        }
    ];

    #endregion

    #region Properties

    [ObservableProperty]
    private string _title = "Git";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private GitPanelTab _activeTab = GitPanelTab.Changes;

    [ObservableProperty]
    private bool _isLoading;

    // Tab visibility helpers for XAML binding
    public bool IsBranchesTabActive => ActiveTab == GitPanelTab.Branches;
    public bool IsChangesTabActive => ActiveTab == GitPanelTab.Changes;
    public bool IsHistoryTabActive => ActiveTab == GitPanelTab.History;
    public bool IsStashTabActive => ActiveTab == GitPanelTab.Stash;
    public bool IsTagsTabActive => ActiveTab == GitPanelTab.Tags;
    public bool IsComparisonTabActive => ActiveTab == GitPanelTab.Comparison;

    #endregion

    public UnifiedGitPanelViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IToastService toastService,
        GitBranchViewModel branchesVM,
        GitFilesViewModel changesVM,
        CommitHistoryViewModel historyVM,
        GitStashViewModel stashVM,
        GitTagsViewModel tagsVM,
        BranchComparisonViewModel comparisonVM)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _toastService = toastService;

        BranchesVM = branchesVM;
        ChangesVM = changesVM;
        HistoryVM = historyVM;
        StashVM = stashVM;
        TagsVM = tagsVM;
        ComparisonVM = comparisonVM;

        // Default to Popup state with large size
        DisplayState = PanelDisplayState.Popup;
        Width = 1200;
        Height = 800;
    }

    partial void OnActiveTabChanged(GitPanelTab value)
    {
        OnPropertyChanged(nameof(IsBranchesTabActive));
        OnPropertyChanged(nameof(IsChangesTabActive));
        OnPropertyChanged(nameof(IsHistoryTabActive));
        OnPropertyChanged(nameof(IsStashTabActive));
        OnPropertyChanged(nameof(IsTagsTabActive));
        OnPropertyChanged(nameof(IsComparisonTabActive));
    }

    #region Commands

    /// <summary>
    /// Opens the unified Git panel on the specified tab.
    /// </summary>
    public async Task OpenOnTabAsync(TerminalPairTabViewModel terminalTab, GitPanelTab tab)
    {
        _currentTerminalTab = terminalTab;
        WorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Git - {terminalTab.Title}";
        ActiveTab = tab;

        // Initialize the active tab's data
        await LoadTabDataAsync(tab);

        IsOpen = true;
    }

    /// <summary>
    /// Opens with default tab (Changes).
    /// </summary>
    [RelayCommand]
    public Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        return OpenOnTabAsync(terminalTab, GitPanelTab.Changes);
    }

    [RelayCommand]
    private void SwitchToTab(GitPanelTab tab)
    {
        if (ActiveTab != tab)
        {
            ActiveTab = tab;
            // Load data for the newly selected tab
            _ = LoadTabDataAsync(tab);
        }
    }

    [RelayCommand]
    private async Task RefreshCurrentTabAsync()
    {
        await LoadTabDataAsync(ActiveTab);
    }

    #endregion

    #region Tab Data Loading

    private async Task LoadTabDataAsync(GitPanelTab tab)
    {
        if (_currentTerminalTab == null) return;

        IsLoading = true;
        try
        {
            switch (tab)
            {
                case GitPanelTab.Branches:
                    await BranchesVM.LoadDataAsync(_currentTerminalTab);
                    break;

                case GitPanelTab.Changes:
                    await ChangesVM.LoadDataAsync(_currentTerminalTab);
                    break;

                case GitPanelTab.History:
                    await HistoryVM.LoadDataAsync(_currentTerminalTab);
                    break;

                case GitPanelTab.Stash:
                    await StashVM.LoadDataAsync(_currentTerminalTab);
                    break;

                case GitPanelTab.Tags:
                    await TagsVM.LoadDataAsync(_currentTerminalTab);
                    break;

                case GitPanelTab.Comparison:
                    await ComparisonVM.LoadDataAsync(_currentTerminalTab);
                    break;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Overrides

    protected override void OnClose()
    {
        // Close all sub-panels
        BranchesVM.IsOpen = false;
        ChangesVM.IsOpen = false;
        HistoryVM.IsOpen = false;
        StashVM.IsOpen = false;
        TagsVM.IsOpen = false;
        ComparisonVM.IsOpen = false;

        _currentTerminalTab = null;
        base.OnClose();
    }

    #endregion
}
