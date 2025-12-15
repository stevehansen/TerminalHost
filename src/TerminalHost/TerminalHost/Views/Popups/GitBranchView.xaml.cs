using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitBranchView : UserControl
{
    public GitBranchView()
    {
        InitializeComponent();
    }

    private void GitBranchSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = DataContext as GitBranchViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Down)
        {
            if (GitBranchList.SelectedIndex < GitBranchList.Items.Count - 1)
            {
                GitBranchList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (GitBranchList.SelectedIndex > 0)
            {
                GitBranchList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            viewModel.CheckoutBranchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void GitBranchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var viewModel = DataContext as GitBranchViewModel;
        if (viewModel == null) return;

        if (GitBranchList.SelectedItem is Domain.GitBranch)
        {
            viewModel.CheckoutBranchCommand.Execute(null);
        }
    }
}
