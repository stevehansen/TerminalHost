using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class GitBranchesContentView : UserControl
{
    public GitBranchesContentView()
    {
        InitializeComponent();
    }

    private void BranchTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is BranchTreeNode node && !node.IsFolder && node.Branch != null)
        {
            if (DataContext is GitBranchViewModel vm)
                vm.SelectedBranch = node.Branch;
        }
    }
}
