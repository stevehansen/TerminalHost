using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Tab Dropdown popup (tab overflow).
/// Owns the search text and filtered tab list (previously smeared on <see cref="MainViewModel"/>),
/// and routes the user's selection back to the main view model's <c>SelectedTab</c>.
/// </summary>
public sealed partial class TabDropdownViewModel : BasePanelViewModel
{
    public override string PanelId => "tabDropdown";
    public override string PanelTitle => "Tabs";
    public override string PanelIcon => "\U0001F4C2";
    public override PanelSizePreset SizePreset => PanelSizePreset.Compact;

    private readonly MainViewModel _mainViewModel;
    private readonly ObservableCollection<ITabViewModel> _filtered = [];

    public ReadOnlyObservableCollection<ITabViewModel> FilteredDropdownTabs { get; }

    public ITabViewModel? SelectedTab => _mainViewModel.SelectedTab;

    [ObservableProperty]
    private string _dropdownSearchText = "";

    public TabDropdownViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        FilteredDropdownTabs = new ReadOnlyObservableCollection<ITabViewModel>(_filtered);
        Refilter();
    }

    public void SelectTab(ITabViewModel tab)
    {
        // OnWorkspaceSelectedTabChanged in MainViewModel closes this panel via the router.
        _mainViewModel.SelectedTab = tab;
    }

    partial void OnDropdownSearchTextChanged(string value) => Refilter();

    protected override void OnPanelOpened()
    {
        DropdownSearchText = "";
        Refilter();
        OnPropertyChanged(nameof(SelectedTab));
    }

    private void Refilter()
    {
        _filtered.Clear();
        if (string.IsNullOrEmpty(DropdownSearchText))
        {
            foreach (var tab in _mainViewModel.Tabs)
                _filtered.Add(tab);
        }
        else
        {
            foreach (var tab in _mainViewModel.Tabs.Where(t =>
                t.Title.Contains(DropdownSearchText, StringComparison.OrdinalIgnoreCase) ||
                t.WorkingDirectory.Contains(DropdownSearchText, StringComparison.OrdinalIgnoreCase)))
            {
                _filtered.Add(tab);
            }
        }
    }
}
