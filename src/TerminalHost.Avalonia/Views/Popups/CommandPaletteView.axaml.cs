using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.Domain; // For PaletteCommand
using TerminalHost.ViewModels; // For MainViewModel

namespace TerminalHost.Views.Popups;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        AttachedToVisualTree += CommandPaletteView_AttachedToVisualTree;
        KeyDown += CommandPaletteView_KeyDown;
    }

    private void CommandPaletteView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Set keyboard focus to the search box when the palette becomes visible
        PaletteSearchBox.Focus();
        PaletteSearchBox.SelectAll();
    }

    private void CommandPaletteView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (e.Key == Key.Down)
        {
            if (PaletteCommandList.SelectedIndex < PaletteCommandList.ItemCount - 1)
            {
                PaletteCommandList.SelectedIndex++;
                PaletteCommandList.ScrollIntoView(PaletteCommandList.SelectedIndex);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteCommandList.SelectedIndex > 0)
            {
                PaletteCommandList.SelectedIndex--;
                PaletteCommandList.ScrollIntoView(PaletteCommandList.SelectedIndex);
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

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.IsCommandPaletteOpen = false;
        }
    }

    private void PaletteCommandList_DoubleTapped(object? sender, TappedEventArgs e)
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