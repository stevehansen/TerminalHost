using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TerminalHost.Core.Interfaces;
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
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Move Win32 focus from the terminal HwndHost to this Popup's HWND
            var source = PresentationSource.FromVisual(SwitcherSearchBox) as HwndSource;
            if (source != null)
                SetFocus(source.Handle);

            SwitcherSearchBox.Focus();
            Keyboard.Focus(SwitcherSearchBox);
        }), DispatcherPriority.Input);
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.IsTabSwitcherOpen = false;
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

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
}
