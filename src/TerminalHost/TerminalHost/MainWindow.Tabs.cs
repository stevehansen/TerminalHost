using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost;

/// <summary>
/// Tab drag-drop, overflow, switcher, and cycling functionality.
/// </summary>
public partial class MainWindow
{
    #region Tab Overflow and Switcher

    private void TabDropdown_Click(object sender, RoutedEventArgs e)
    {
        // Populate and show dropdown
        DropdownTabList.ItemsSource = _viewModel.Tabs;
        DropdownTabList.SelectedItem = _viewModel.SelectedTab;
        DropdownSearchBox.Text = "";
        TabDropdownPopup.IsOpen = true;
        DropdownSearchBox.Focus();
    }

    private void DropdownSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = DropdownSearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            DropdownTabList.ItemsSource = _viewModel.Tabs;
        }
        else
        {
            var filtered = _viewModel.Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            DropdownTabList.ItemsSource = filtered;
        }
    }

    private void DropdownTabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DropdownTabList.SelectedItem is ITabViewModel tab)
        {
            _viewModel.SelectedTab = tab;
            TabDropdownPopup.IsOpen = false;
        }
    }

    #endregion

    #region Tab Switcher (Ctrl+Shift+T)

    private void ShowTabSwitcher()
    {
        // Populate and show switcher
        SwitcherTabList.ItemsSource = _viewModel.Tabs;
        SwitcherTabList.SelectedItem = _viewModel.SelectedTab;
        SwitcherSearchBox.Text = "";
        TabSwitcherPopup.IsOpen = true;
        SwitcherSearchBox.Focus();
    }

    private void SwitcherSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SwitcherSearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            SwitcherTabList.ItemsSource = _viewModel.Tabs;
        }
        else
        {
            var filtered = _viewModel.Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            SwitcherTabList.ItemsSource = filtered;
        }

        // Select first item if any
        if (SwitcherTabList.Items.Count > 0)
        {
            SwitcherTabList.SelectedIndex = 0;
        }
    }

    private void SwitcherSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (SwitcherTabList.SelectedIndex < SwitcherTabList.Items.Count - 1)
            {
                SwitcherTabList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (SwitcherTabList.SelectedIndex > 0)
            {
                SwitcherTabList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectSwitcherItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TabSwitcherPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void SwitcherTabList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectSwitcherItem();
    }

    private void SelectSwitcherItem()
    {
        if (SwitcherTabList.SelectedItem is ITabViewModel tab)
        {
            _viewModel.SelectedTab = tab;
            TabSwitcherPopup.IsOpen = false;
        }
    }

    #endregion

    #region Tab Cycling

    private void CycleTab(bool forward)
    {
        if (_viewModel.Tabs.Count <= 1) return;

        var currentIndex = _viewModel.SelectedTab != null
            ? _viewModel.Tabs.IndexOf(_viewModel.SelectedTab)
            : 0;

        int newIndex;
        if (forward)
        {
            newIndex = (currentIndex + 1) % _viewModel.Tabs.Count;
        }
        else
        {
            newIndex = (currentIndex - 1 + _viewModel.Tabs.Count) % _viewModel.Tabs.Count;
        }

        _viewModel.SelectedTab = _viewModel.Tabs[newIndex];
    }

    #endregion
}
