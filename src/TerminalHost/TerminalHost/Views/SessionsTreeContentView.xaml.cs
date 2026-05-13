using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class SessionsTreeContentView : UserControl
{
    public SessionsTreeContentView()
    {
        InitializeComponent();
    }

    private void OnTreeViewItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: SessionTreeNode node }
            && DataContext is SessionsTreePanelViewModel vm
            && vm.OpenWorkspaceCommand.CanExecute(node))
        {
            vm.OpenWorkspaceCommand.Execute(node);
            e.Handled = true;
        }
    }
}
