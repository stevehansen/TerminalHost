using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class FileExplorerView : UserControl
{
    public FileExplorerView()
    {
        InitializeComponent();
    }

    private FileExplorerViewModel? ViewModel => DataContext as FileExplorerViewModel;

    private void FileTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && FileTree.SelectedItem is FileSystemNode node)
        {
            ViewModel.SelectedNode = node;
        }
    }

    private async void FileTree_DoubleTapped(object? sender, TappedEventArgs e)
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
            if (ViewModel.OpenInViewerCommand.CanExecute(null))
            {
                ViewModel.OpenInViewerCommand.Execute(null);
            }
        }

        e.Handled = true;
    }

    private async void FileTree_KeyDown(object? sender, KeyEventArgs e)
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
                    if (ViewModel.OpenInViewerCommand.CanExecute(null))
                    {
                        ViewModel.OpenInViewerCommand.Execute(null);
                    }
                }
                e.Handled = true;
                break;

            case Key.F2:
                if (ViewModel.RenameCommand.CanExecute(null))
                {
                    ViewModel.RenameCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Delete:
                if (ViewModel.DeleteCommand.CanExecute(null))
                {
                    ViewModel.DeleteCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.F4:
                if (ViewModel.EditInViewerCommand.CanExecute(null))
                {
                    ViewModel.EditInViewerCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.C when e.KeyModifiers == KeyModifiers.Control:
                if (ViewModel.CopyPathCommand.CanExecute(null))
                {
                    ViewModel.CopyPathCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }
}
