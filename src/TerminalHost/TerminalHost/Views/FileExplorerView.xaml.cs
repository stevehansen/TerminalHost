using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class FileExplorerView : UserControl
{
    public FileExplorerView()
    {
        InitializeComponent();
    }

    private FileExplorerViewModel? ViewModel => DataContext as FileExplorerViewModel;

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel != null && e.NewValue is FileSystemNode node)
        {
            ViewModel.SelectedNode = node;
        }
    }

    private async void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.SelectedNode == null) return;

        var node = ViewModel.SelectedNode;

        if (node.IsDirectory)
        {
            // Toggle expansion
            if (!node.ChildrenLoaded)
            {
                await ViewModel.LoadChildrenAsync(node);
            }
            node.IsExpanded = !node.IsExpanded;
        }
        else
        {
            // Open file in viewer
            ViewModel.OpenInViewerCommand.Execute(null);
        }

        e.Handled = true;
    }

    private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem item && item.DataContext is FileSystemNode node && ViewModel != null)
        {
            if (!node.ChildrenLoaded)
            {
                await ViewModel.LoadChildrenAsync(node);
            }
        }
    }

    private async void FileTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel?.SelectedNode == null) return;

        switch (e.Key)
        {
            case Key.Enter:
                if (ViewModel.SelectedNode.IsDirectory)
                {
                    // Load children first, then toggle expansion
                    if (!ViewModel.SelectedNode.ChildrenLoaded)
                    {
                        await ViewModel.LoadChildrenAsync(ViewModel.SelectedNode);
                    }
                    ViewModel.SelectedNode.IsExpanded = !ViewModel.SelectedNode.IsExpanded;
                }
                else
                {
                    ViewModel.OpenInViewerCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.F2:
                ViewModel.RenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                ViewModel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F4:
                ViewModel.EditInViewerCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                ViewModel.CopyPathCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void FileTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Select the item under the mouse before showing context menu
        if (e.OriginalSource is DependencyObject source)
        {
            var treeViewItem = FindAncestor<TreeViewItem>(source);
            if (treeViewItem != null)
            {
                // Force keyboard focus to the WPF control to release terminal's focus grip
                // This fixes the context menu immediately closing when terminal has focus (airspace issue)
                Keyboard.Focus(treeViewItem);
                treeViewItem.IsSelected = true;
                // Don't set e.Handled = true, let the context menu show
            }
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        // Set the context menu's DataContext to the ViewModel
        // This is needed because context menus are not in the visual tree
        if (sender is ContextMenu contextMenu)
        {
            contextMenu.DataContext = ViewModel;
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T target)
                return target;

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
