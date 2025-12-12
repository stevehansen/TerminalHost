using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace TerminalHost.Views;

public partial class TabSwitcherPopup : UserControl
{
    public event EventHandler<ITabViewModel?>? TabSelected;
    public event EventHandler? CloseRequested;

    private IEnumerable<ITabViewModel>? _allTabs;

    public TabSwitcherPopup()
    {
        InitializeComponent();
    }

    public void Initialize(IEnumerable<ITabViewModel> tabs, ITabViewModel? selectedTab)
    {
        _allTabs = tabs;
        TabList.ItemsSource = tabs;
        TabList.SelectedItem = selectedTab;
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_allTabs == null) return;

        var searchText = SearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            TabList.ItemsSource = _allTabs;
        }
        else
        {
            var filtered = _allTabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            TabList.ItemsSource = filtered;
        }

        // Select first item if any
        if (TabList.Items.Count > 0)
        {
            TabList.SelectedIndex = 0;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (TabList.SelectedIndex < TabList.Items.Count - 1)
            {
                TabList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (TabList.SelectedIndex > 0)
            {
                TabList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectCurrentItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void TabList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCurrentItem();
    }

    private void SelectCurrentItem()
    {
        if (TabList.SelectedItem is ITabViewModel tab)
        {
            TabSelected?.Invoke(this, tab);
        }
    }
}
