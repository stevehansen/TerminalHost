using System.Windows;
using System.Windows.Input;
using TerminalHost.Domain; // For PaletteCommand
using TerminalHost.ViewModels; // For MainViewModel

namespace TerminalHost.Views.Popups;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        Loaded += CommandPaletteView_Loaded;
    }

    private void CommandPaletteView_Loaded(object sender, RoutedEventArgs e)
    {
        // Set focus to the search box when the palette becomes visible
        PaletteSearchBox.Focus();
    }

    private void PaletteSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel) return;

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
            mainViewModel.IsCommandPaletteOpen = false;
            e.Handled = true;
        }
    }

    private void PaletteCommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedPaletteCommand();
    }

    private void ExecuteSelectedPaletteCommand()
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (PaletteCommandList.SelectedItem is PaletteCommand command)
        {
            mainViewModel.IsCommandPaletteOpen = false;
            command.Execute();
        }
    }
}
