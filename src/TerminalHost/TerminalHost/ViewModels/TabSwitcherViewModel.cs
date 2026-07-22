using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Tab Switcher popup (Ctrl+Shift+T).
/// Owns the search text and filtered tab list (previously smeared on <see cref="MainViewModel"/>),
/// and routes the user's selection back to the main view model's <c>SelectedTab</c>.
/// </summary>
public sealed partial class TabSwitcherViewModel : BasePanelViewModel
{
    public override string PanelId => "tabSwitcher";
    public override string PanelTitle => "Switch Tab";
    public override string PanelIcon => "\U0001F50D";
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    private readonly MainViewModel _mainViewModel;
    private readonly ObservableCollection<ITabViewModel> _filtered = [];

    public ReadOnlyObservableCollection<ITabViewModel> FilteredSwitcherTabs { get; }

    public ITabViewModel? SelectedTab => _mainViewModel.SelectedTab;

    [ObservableProperty]
    private string _switcherSearchText = "";

    public TabSwitcherViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        FilteredSwitcherTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filtered);
        Refilter();
    }

    public void SelectTab(ITabViewModel tab)
    {
        // OnWorkspaceSelectedTabChanged in MainViewModel closes this panel via the router.
        _mainViewModel.SelectedTab = tab;
    }

    partial void OnSwitcherSearchTextChanged(string value) => Refilter();

    protected override void OnPanelOpened()
    {
        SwitcherSearchText = "";
        Refilter();
        OnPropertyChanged(nameof(SelectedTab));
    }

    private void Refilter()
    {
        _filtered.Clear();
        if (string.IsNullOrEmpty(SwitcherSearchText))
        {
            foreach (var tab in _mainViewModel.Tabs)
                _filtered.Add(tab);
        }
        else
        {
            foreach (var tab in _mainViewModel.Tabs.Where(t =>
                t.Title.Contains(SwitcherSearchText, StringComparison.OrdinalIgnoreCase) ||
                t.WorkingDirectory.Contains(SwitcherSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                _filtered.Add(tab);
            }
        }
    }
}
