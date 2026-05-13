using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Panels;

public partial class SessionsTreePanelView : UserControl
{
    public SessionsTreePanelView()
    {
        InitializeComponent();
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SessionTreeNode node }
            && DataContext is SessionsTreePanelViewModel vm
            && vm.OpenWorkspaceCommand.CanExecute(node))
        {
            vm.OpenWorkspaceCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnOpenWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: SessionTreeNode node }
            && DataContext is SessionsTreePanelViewModel vm
            && vm.OpenWorkspaceCommand.CanExecute(node))
        {
            vm.OpenWorkspaceCommand.Execute(node);
        }
    }
}
