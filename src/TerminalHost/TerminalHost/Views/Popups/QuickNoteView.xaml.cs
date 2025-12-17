using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class QuickNoteView : UserControl
{
    public QuickNoteView()
    {
        InitializeComponent();
        Loaded += QuickNoteView_Loaded;
    }

    private void QuickNoteView_Loaded(object sender, RoutedEventArgs e)
    {
        Keyboard.Focus(NoteTextBox);
        NoteTextBox.SelectAll();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsQuickNoteOpen = false;
            viewModel.QuickNoteText = string.Empty;
        }
    }
}
