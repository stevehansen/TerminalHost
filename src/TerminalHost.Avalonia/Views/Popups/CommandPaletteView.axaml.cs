using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.Domain; // For PaletteCommand
using TerminalHost.ViewModels; // For MainViewModel

namespace TerminalHost.Views.Popups;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        AttachedToVisualTree += CommandPaletteView_AttachedToVisualTree;
        this.GetObservable(IsVisibleProperty).Subscribe(OnIsVisibleChanged);
        KeyDown += CommandPaletteView_KeyDown;
    }

    private void CommandPaletteView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        FocusSearchBox();
    }

    private void OnIsVisibleChanged(bool isVisible)
    {
        if (isVisible)
            FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            PaletteSearchBox.Focus();
            PaletteSearchBox.SelectAll();
        }, DispatcherPriority.Input);
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
            mainViewModel.Palette.IsOpen = false;
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.Palette.IsOpen = false;
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
            mainViewModel.Palette.IsOpen = false;
            command.Execute();
        }
    }
}