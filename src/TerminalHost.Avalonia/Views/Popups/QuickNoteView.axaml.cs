using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class QuickNoteView : UserControl
{
    public QuickNoteView()
    {
        InitializeComponent();
        Loaded += QuickNoteView_Loaded;
        KeyDown += UserControl_KeyDown;
    }

    private void QuickNoteView_Loaded(object? sender, RoutedEventArgs e)
    {
        NoteTextBox.Focus();
        NoteTextBox.SelectAll();
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        if (e.Key == Key.Escape)
        {
            viewModel.IsQuickNoteOpen = false;
            viewModel.QuickNoteText = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (string.IsNullOrWhiteSpace(viewModel.QuickNoteText))
            {
                viewModel.IsQuickNoteOpen = false;
                e.Handled = true;
                return;
            }

            viewModel.CreateQuickNoteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsQuickNoteOpen = false;
            viewModel.QuickNoteText = string.Empty;
        }
    }
}