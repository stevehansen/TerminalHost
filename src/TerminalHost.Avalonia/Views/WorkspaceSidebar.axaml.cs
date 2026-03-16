using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.Core.Domain;
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
        if (sender is not Control control || DataContext is not WorkspaceSidebarViewModel vm) return;

        if (control.DataContext is WorkspaceEntryViewModel workspace)
            vm.OpenWorkspaceCommand.Execute(workspace);
        else if (control.DataContext is RecentWorkspaceItem recent)
            vm.OpenWorkspaceCommand.Execute(recent);
    }

    private void Worktree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not WorkspaceSidebarViewModel vm) return;

        if (sender is Control control)
        {
            // Handle both WorktreeEntryViewModel and WorktreeInfo (from different templates)
            if (control.DataContext is WorktreeEntryViewModel worktreeVm)
                vm.OpenWorktreeCommand.Execute(worktreeVm);
            else if (control.DataContext is WorktreeInfo worktreeInfo)
                vm.OpenWorktreeCommand.Execute(worktreeInfo);
        }
    }
}
