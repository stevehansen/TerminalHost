using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class QuickTaskView : UserControl
{
    public QuickTaskView()
    {
        InitializeComponent();
        Loaded += QuickTaskView_Loaded;
        KeyDown += QuickTaskView_KeyDown;
    }

    private void QuickTaskView_Loaded(object? sender, RoutedEventArgs e)
    {
        TaskTitleBox.Focus();
        TaskTitleBox.SelectAll();
    }

    private void QuickTaskView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        if (e.Key == Key.Escape)
        {
            viewModel.IsQuickTaskOpen = false;
            viewModel.QuickTaskTitle = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (string.IsNullOrWhiteSpace(viewModel.QuickTaskTitle))
            {
                viewModel.IsQuickTaskOpen = false;
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                // Shift+Enter: Create and start task
                viewModel.CreateAndStartQuickTaskCommand.Execute(null);
            }
            else
            {
                // Enter: Create task (add to backlog)
                viewModel.CreateQuickTaskCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsQuickTaskOpen = false;
            viewModel.QuickTaskTitle = string.Empty;
        }
    }
}
