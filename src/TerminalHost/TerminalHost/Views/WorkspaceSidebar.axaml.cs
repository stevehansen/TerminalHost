using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class WorkspaceSidebar : UserControl
{
    public WorkspaceSidebar()
    {
        InitializeComponent();
    }

    private void Workspace_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is Workspace workspace)
        {
            if (DataContext is WorkspaceSidebarViewModel vm)
            {
                vm.OpenWorkspaceCommand.Execute(workspace);
            }
        }
    }

    private void Worktree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is WorktreeInfo worktree)
        {
            if (DataContext is WorkspaceSidebarViewModel vm)
            {
                vm.OpenWorktreeCommand.Execute(worktree);
            }
        }
    }
}
