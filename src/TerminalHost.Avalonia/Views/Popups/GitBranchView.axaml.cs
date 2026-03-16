using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitBranchView : UserControl
{
    public GitBranchView()
    {
        InitializeComponent();

        // Focus the search box when view becomes visible
        this.AttachedToVisualTree += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                GitBranchSearchBox.Focus();
                GitBranchSearchBox.SelectAll();
            }, DispatcherPriority.Input);
        };

        // Handle keyboard navigation
        this.KeyDown += UserControl_KeyDown;

        // Handle tree view selection to sync SelectedBranch
        GitBranchTreeView.SelectionChanged += (s, e) =>
        {
            if (DataContext is GitBranchViewModel vm &&
                GitBranchTreeView.SelectedItem is BranchTreeNode node &&
                node.Branch != null)
            {
                vm.SelectedBranch = node.Branch;
            }
        };
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        var viewModel = DataContext as GitBranchViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Down)
        {
            // Move selection down in the list
            if (GitBranchList.ItemCount > 0)
            {
                if (GitBranchList.SelectedIndex < GitBranchList.ItemCount - 1)
                {
                    GitBranchList.SelectedIndex++;
                }
                else if (GitBranchList.SelectedIndex == -1)
                {
                    GitBranchList.SelectedIndex = 0;
                }
                GitBranchList.ScrollIntoView(GitBranchList.SelectedIndex);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            // Move selection up in the list
            if (GitBranchList.ItemCount > 0)
            {
                if (GitBranchList.SelectedIndex > 0)
                {
                    GitBranchList.SelectedIndex--;
                }
                else if (GitBranchList.SelectedIndex == -1)
                {
                    GitBranchList.SelectedIndex = GitBranchList.ItemCount - 1;
                }
                GitBranchList.ScrollIntoView(GitBranchList.SelectedIndex);
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
        else if (e.Key == Key.Delete)
        {
            // Delete selected branch
            viewModel.DeleteBranchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None)
        {
            // Fetch all remotes (only if not typing in search box with text)
            if (string.IsNullOrEmpty(viewModel.SearchText))
            {
                viewModel.FetchAllCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.None)
        {
            // Pull current branch (only if not typing in search box with text)
            if (string.IsNullOrEmpty(viewModel.SearchText))
            {
                viewModel.PullCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.None)
        {
            // Compare with current branch (only if not typing in search box with text)
            if (string.IsNullOrEmpty(viewModel.SearchText))
            {
                viewModel.CompareWithSelectedBranchCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.T && e.KeyModifiers == KeyModifiers.None)
        {
            // Toggle tree view (only if not typing in search box with text)
            if (string.IsNullOrEmpty(viewModel.SearchText))
            {
                viewModel.ToggleTreeViewCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Handle double-click on list items
        if (e.ClickCount == 2 && e.Source is Control control)
        {
            // Check if the click was on a ListBoxItem
            var listBoxItem = control.FindAncestorOfType<ListBoxItem>();
            if (listBoxItem != null && DataContext is GitBranchViewModel viewModel)
            {
                if (GitBranchList.SelectedItem is GitBranch)
                {
                    viewModel.CheckoutBranchCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}

// Helper extension for finding ancestor controls
public static class ControlExtensions
{
    public static T? FindAncestorOfType<T>(this Control control) where T : Control
    {
        var parent = control.Parent;
        while (parent != null)
        {
            if (parent is T typedParent)
                return typedParent;
            parent = parent.Parent;
        }
        return null;
    }
}