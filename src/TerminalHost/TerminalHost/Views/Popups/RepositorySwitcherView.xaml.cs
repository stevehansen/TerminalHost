using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

/// <summary>
/// Code-behind for RepositorySwitcherView.xaml
/// </summary>
public partial class RepositorySwitcherView : UserControl
{
    public RepositorySwitcherView()
    {
        InitializeComponent();

        // Focus search box when popup opens (like GitBranchView pattern)
        RepositorySwitcherPopup.Opened += (s, e) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                FocusSearchBox();
            });
        };
    }

    private void FocusSearchBox()
    {
        Keyboard.Focus(RepoSearchBox);
        RepoSearchBox.SelectAll();
    }

    private void PopupBorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle keyboard events for the popup content
        HandleKeyDown(e);
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Also handle events at UserControl level (for any edge cases)
        HandleKeyDown(e);
    }

    private void HandleKeyDown(KeyEventArgs e)
    {
        if (DataContext is not RepositorySwitcherViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                if (vm.SelectedRepository != null)
                {
                    vm.OpenRepositoryCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (RepoList.SelectedIndex < RepoList.Items.Count - 1)
                {
                    RepoList.SelectedIndex++;
                    RepoList.ScrollIntoView(RepoList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (RepoList.SelectedIndex > 0)
                {
                    RepoList.SelectedIndex--;
                    RepoList.ScrollIntoView(RepoList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.F:
                // Ctrl+F to toggle favorite
                if (Keyboard.Modifiers == ModifierKeys.Control && vm.SelectedRepository != null)
                {
                    vm.ToggleFavoriteCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    private void RepoList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RepositorySwitcherViewModel vm && vm.SelectedRepository != null)
        {
            vm.OpenRepositoryCommand.Execute(null);
        }
    }
}
