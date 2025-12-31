using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class TaskPanelView : UserControl
{
    public TaskPanelView()
    {
        InitializeComponent();
        AttachedToVisualTree += TaskPanelView_AttachedToVisualTree;
    }

    private void TaskPanelView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Focus the new task textbox if adding task
        if (DataContext is TaskPanelViewModel vm && vm.IsAddingTask)
        {
            NewTaskTextBox.Focus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

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
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
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

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskPanelViewModel viewModel)
        {
            viewModel.CloseCommand.Execute(null);
        }
    }

    private void TaskItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control element && element.DataContext is FocusTask task)
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