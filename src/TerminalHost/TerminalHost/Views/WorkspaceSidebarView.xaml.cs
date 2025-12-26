using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Interaction logic for WorkspaceSidebarView.xaml
/// </summary>
public partial class WorkspaceSidebarView : UserControl
{
    public WorkspaceSidebarView()
    {
        InitializeComponent();
    }

    private WorkspaceSidebarViewModel? ViewModel => DataContext as WorkspaceSidebarViewModel;

    private void Workspace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Handle double-click to open
        if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is WorkspaceEntryViewModel workspace)
        {
            workspace.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Workspace_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Single click selects the workspace
        if (sender is FrameworkElement element && element.DataContext is WorkspaceEntryViewModel workspace)
        {
            if (ViewModel != null)
            {
                ViewModel.SelectedWorkspace = workspace;
            }
        }
    }

    private void Worktree_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Single-click to open worktree
        if (sender is FrameworkElement element && element.DataContext is WorktreeEntryViewModel worktree)
        {
            // Find parent workspace and trigger open
            var parent = FindParentWorkspace(element);
            if (parent != null)
            {
                parent.OpenWorktreeCommand.Execute(worktree);
            }
            e.Handled = true;
        }
    }

    private WorkspaceEntryViewModel? FindParentWorkspace(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.DataContext is WorkspaceEntryViewModel workspace)
            {
                return workspace;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async void AddWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select project folders to add to workspaces",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FolderNames.Length > 0)
        {
            if (ViewModel == null) return;

            if (dialog.FolderNames.Length == 1)
            {
                // Single folder - use existing method
                await ViewModel.AddWorkspaceAsync(dialog.FolderNames[0]);
            }
            else
            {
                // Multiple folders - use batch method
                await ViewModel.AddWorkspacesAsync(dialog.FolderNames);
            }
        }
    }

    private MainViewModel? GetMainViewModel()
    {
        return Window.GetWindow(this)?.DataContext as MainViewModel;
    }

    private void OpenTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Click to select the tab
        if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
        {
            var mainVm = GetMainViewModel();
            if (mainVm != null)
            {
                mainVm.SelectedTab = tab;
            }
            e.Handled = true;
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ITabViewModel tab)
        {
            var mainVm = GetMainViewModel();
            if (mainVm != null && tab.IsCloseable)
            {
                mainVm.CloseTabCommand.Execute(tab);
            }
        }
    }

    private void OpenWorktree_Click(object sender, RoutedEventArgs e)
    {
        // Get the worktree from the context menu's data context
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target &&
            target.DataContext is WorktreeEntryViewModel worktree)
        {
            // Find the parent workspace to trigger open
            var workspace = FindParentWorkspaceFromContextMenu(contextMenu);
            if (workspace != null)
            {
                workspace.OpenWorktreeCommand.Execute(worktree);
            }
        }
    }

    private WorkspaceEntryViewModel? FindParentWorkspaceFromContextMenu(ContextMenu contextMenu)
    {
        // Walk up the visual tree from the context menu's placement target
        if (contextMenu.PlacementTarget is DependencyObject target)
        {
            return FindParentWorkspace(target);
        }
        return null;
    }

    private void OpenWorktreeInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target &&
            target.DataContext is WorktreeEntryViewModel worktree)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", worktree.Path);
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    private void ManageWorktreesFromWorktree_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target &&
            target.DataContext is WorktreeEntryViewModel worktree)
        {
            // Find the parent workspace and trigger manage worktrees
            var workspace = FindParentWorkspaceFromContextMenu(contextMenu);
            if (workspace != null && ViewModel != null)
            {
                ViewModel.ManageWorktreesCommand.Execute(workspace);
            }
        }
    }

    private void WorktreeGitFetch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target &&
            target.DataContext is WorktreeEntryViewModel worktree)
        {
            // Find the parent workspace and trigger git fetch via sidebar ViewModel
            var workspace = FindParentWorkspaceFromContextMenu(contextMenu);
            if (workspace != null && ViewModel != null)
            {
                ViewModel.GitFetchCommand.Execute(workspace);
            }
        }
    }

    private void WorktreeGitPull_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target &&
            target.DataContext is WorktreeEntryViewModel worktree)
        {
            // Find the parent workspace and trigger git pull via sidebar ViewModel
            var workspace = FindParentWorkspaceFromContextMenu(contextMenu);
            if (workspace != null && ViewModel != null)
            {
                ViewModel.GitPullRebaseCommand.Execute(workspace);
            }
        }
    }

    private void WorktreeGitPush_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement target &&
            target.DataContext is WorktreeEntryViewModel worktree)
        {
            // Find the parent workspace and trigger git push via sidebar ViewModel
            var workspace = FindParentWorkspaceFromContextMenu(contextMenu);
            if (workspace != null && ViewModel != null)
            {
                ViewModel.GitPushCommand.Execute(workspace);
            }
        }
    }
}
