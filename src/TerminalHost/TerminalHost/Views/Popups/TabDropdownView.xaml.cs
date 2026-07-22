using System.Windows;
using System.Windows.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class TabDropdownView : UserControl
{
    public TabDropdownView()
    {
        InitializeComponent();
        Loaded += TabDropdownView_Loaded;
        IsVisibleChanged += TabDropdownView_IsVisibleChanged;
    }

    private void TabDropdownView_Loaded(object sender, RoutedEventArgs e)
    {
        FocusSearchBox();
    }

    private void TabDropdownView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            DropdownSearchBox.Focus();
            Keyboard.Focus(DropdownSearchBox);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TabDropdownViewModel vm) return;

        if (e.Key == Key.Down)
        {
            if (DropdownTabList.SelectedIndex < DropdownTabList.Items.Count - 1)
            {
                DropdownTabList.SelectedIndex++;
                DropdownTabList.ScrollIntoView(DropdownTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (DropdownTabList.SelectedIndex > 0)
            {
                DropdownTabList.SelectedIndex--;
                DropdownTabList.ScrollIntoView(DropdownTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectDropdownItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TabDropdownViewModel vm)
            vm.CloseCommand.Execute(null);
    }

    private void DropdownTabList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        SelectDropdownItem();
    }

    private void SelectDropdownItem()
    {
        if (DataContext is not TabDropdownViewModel vm) return;
        if (DropdownTabList.SelectedItem is ITabViewModel tab)
            vm.SelectTab(tab);
    }
}
