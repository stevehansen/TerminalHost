using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        Loaded += CommandPaletteView_Loaded;
        IsVisibleChanged += CommandPaletteView_IsVisibleChanged;
    }

    private void CommandPaletteView_Loaded(object sender, RoutedEventArgs e)
    {
        FocusSearchBox();
    }

    private void CommandPaletteView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var source = PresentationSource.FromVisual(PaletteSearchBox) as HwndSource;
            if (source != null)
                SetFocus(source.Handle);

            PaletteSearchBox.Focus();
            Keyboard.Focus(PaletteSearchBox);
            PaletteSearchBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;

        if (e.Key == Key.Down)
        {
            if (PaletteCommandList.SelectedIndex < PaletteCommandList.Items.Count - 1)
            {
                PaletteCommandList.SelectedIndex++;
                PaletteCommandList.ScrollIntoView(PaletteCommandList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteCommandList.SelectedIndex > 0)
            {
                PaletteCommandList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedPaletteCommand();
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
        if (DataContext is CommandPaletteViewModel vm)
            vm.CloseCommand.Execute(null);
    }

    private void PaletteCommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedPaletteCommand();
    }

    private void ExecuteSelectedPaletteCommand()
    {
        if (DataContext is not CommandPaletteViewModel vm) return;
        if (PaletteCommandList.SelectedItem is PaletteCommand command)
        {
            vm.CloseCommand.Execute(null);
            command.Execute();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
}
