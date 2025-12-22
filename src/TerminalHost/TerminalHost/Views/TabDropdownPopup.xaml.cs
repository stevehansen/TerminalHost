using System.Collections;
using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class TabDropdownPopup : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(TabDropdownPopup));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TabDropdownPopup),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public event EventHandler<ITabViewModel?>? TabSelected;

    public TabDropdownPopup()
    {
        InitializeComponent();
    }

    public void Initialize(IEnumerable tabs, ITabViewModel? selectedTab)
    {
        TabList.ItemsSource = tabs;
        TabList.SelectedItem = selectedTab;
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            TabList.ItemsSource = ItemsSource;
        }
        else if (ItemsSource is IEnumerable<ITabViewModel> tabs)
        {
            var filtered = tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            TabList.ItemsSource = filtered;
        }
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedItem is ITabViewModel tab)
        {
            SelectedItem = tab;
            TabSelected?.Invoke(this, tab);
        }
    }
}
