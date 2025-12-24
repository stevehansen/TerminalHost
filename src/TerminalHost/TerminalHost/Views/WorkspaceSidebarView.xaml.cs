using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        // Double-click to open worktree
        if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is WorktreeEntryViewModel worktree)
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

    private WorkspaceEntryViewModel? FindParentWorkspace(FrameworkElement element)
    {
        var current = element;
        while (current != null)
        {
            if (current.DataContext is WorkspaceEntryViewModel workspace)
            {
                return workspace;
            }
            current = current.Parent as FrameworkElement;
        }
        return null;
    }

    private async void AddWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a project folder to add to workspaces",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var path = dialog.SelectedPath;
            if (ViewModel != null && !string.IsNullOrEmpty(path))
            {
                await ViewModel.AddWorkspaceAsync(path);
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
}
