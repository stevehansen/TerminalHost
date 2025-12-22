using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class TaskPanelView : UserControl
{
    public TaskPanelView()
    {
        InitializeComponent();
        Loaded += TaskPanelView_Loaded;
    }

    private void TaskPanelView_Loaded(object sender, RoutedEventArgs e)
    {
        // Focus the new task textbox if adding task
        if (DataContext is TaskPanelViewModel vm && vm.IsAddingTask)
        {
            Keyboard.Focus(NewTaskTextBox);
        }
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TaskPanelViewModel viewModel) return;

        if (e.Key == Key.Escape)
        {
            if (viewModel.IsAddingTask)
            {
                viewModel.CancelAddTaskCommand.Execute(null);
            }
            else
            {
                viewModel.CloseCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.IsAddingTask)
        {
            // If Shift+Enter, create and start; otherwise just create
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                viewModel.CreateAndStartTaskCommand.Execute(null);
            }
            else
            {
                viewModel.CreateTaskCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TaskPanelViewModel viewModel)
        {
            viewModel.CloseCommand.Execute(null);
        }
    }

    private void TaskItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FocusTask task)
        {
            if (DataContext is TaskPanelViewModel viewModel)
            {
                if (e.ClickCount == 2)
                {
                    // Double-click: start the task
                    viewModel.StartTaskCommand.Execute(task);
                }
                else
                {
                    // Single-click: select for editing
                    viewModel.SelectTaskForEditCommand.Execute(task);
                }
            }
        }
    }
}
