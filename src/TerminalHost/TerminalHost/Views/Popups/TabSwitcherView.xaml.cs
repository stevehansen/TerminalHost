using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class TabSwitcherView : UserControl
{
    public TabSwitcherView()
    {
        InitializeComponent();
        Loaded += TabSwitcherView_Loaded;
        IsVisibleChanged += TabSwitcherView_IsVisibleChanged;
    }

    private void TabSwitcherView_Loaded(object sender, RoutedEventArgs e)
    {
        FocusSearchBox();
    }

    private void TabSwitcherView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            SwitcherSearchBox.Focus();
            Keyboard.Focus(SwitcherSearchBox);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SwitcherSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (e.Key == Key.Down)
        {
            if (SwitcherTabList.SelectedIndex < SwitcherTabList.Items.Count - 1)
            {
                SwitcherTabList.SelectedIndex++;
                SwitcherTabList.ScrollIntoView(SwitcherTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (SwitcherTabList.SelectedIndex > 0)
            {
                SwitcherTabList.SelectedIndex--;
                SwitcherTabList.ScrollIntoView(SwitcherTabList.SelectedItem);
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
            mainViewModel.IsTabSwitcherOpen = false;
            e.Handled = true;
        }
    }

    private void SwitcherTabList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectSwitcherItem();
    }

    private void SelectSwitcherItem()
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (SwitcherTabList.SelectedItem is ITabViewModel tab)
        {
            mainViewModel.SelectedTab = tab;
            mainViewModel.IsTabSwitcherOpen = false;
        }
    }
}
