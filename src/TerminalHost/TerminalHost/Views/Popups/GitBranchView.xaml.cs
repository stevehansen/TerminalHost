using System.Windows.Input;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitBranchView : UserControl
{
    public GitBranchView()
    {
        InitializeComponent();

        // Focus the search box when popup opens
        GitBranchPopup.Opened += (s, e) =>
        {
            // Use Dispatcher to ensure focus happens after popup is fully rendered
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                GitBranchSearchBox.Focus();
                Keyboard.Focus(GitBranchSearchBox);
                GitBranchSearchBox.SelectAll();
            });
        };
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = DataContext as GitBranchViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Down)
        {
            // Move selection down in the list
            if (GitBranchList.Items.Count > 0)
            {
                if (GitBranchList.SelectedIndex < GitBranchList.Items.Count - 1)
                {
                    GitBranchList.SelectedIndex++;
                }
                else if (GitBranchList.SelectedIndex == -1)
                {
                    GitBranchList.SelectedIndex = 0;
                }
                GitBranchList.ScrollIntoView(GitBranchList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            // Move selection up in the list
            if (GitBranchList.Items.Count > 0)
            {
                if (GitBranchList.SelectedIndex > 0)
                {
                    GitBranchList.SelectedIndex--;
                }
                else if (GitBranchList.SelectedIndex == -1)
                {
                    GitBranchList.SelectedIndex = GitBranchList.Items.Count - 1;
                }
                GitBranchList.ScrollIntoView(GitBranchList.SelectedItem);
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

        if (GitBranchList.SelectedItem is GitBranch)
        {
            viewModel.CheckoutBranchCommand.Execute(null);
        }
    }
}
