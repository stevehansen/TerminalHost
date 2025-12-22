using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

/// <summary>
/// Code-behind for RepositorySwitcherView.axaml
/// </summary>
public partial class RepositorySwitcherView : UserControl
{
    public RepositorySwitcherView()
    {
        InitializeComponent();

        // Focus search box when popup becomes visible
        this.GetObservable(IsVisibleProperty).Subscribe(isVisible =>
        {
            if (isVisible)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    FocusSearchBox();
                }, DispatcherPriority.Input);
            }
        });
    }

    private void FocusSearchBox()
    {
        RepoSearchBox?.Focus();
        RepoSearchBox?.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
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
                if (RepoList.SelectedIndex < RepoList.ItemCount - 1)
                {
                    RepoList.SelectedIndex++;
                    RepoList.ScrollIntoView(RepoList.SelectedIndex);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (RepoList.SelectedIndex > 0)
                {
                    RepoList.SelectedIndex--;
                    RepoList.ScrollIntoView(RepoList.SelectedIndex);
                }
                e.Handled = true;
                break;

            case Key.F:
                // Ctrl+F to toggle favorite
                if (e.KeyModifiers == KeyModifiers.Control && vm.SelectedRepository != null)
                {
                    vm.ToggleFavoriteCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    private void RepoList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RepositorySwitcherViewModel vm && vm.SelectedRepository != null)
        {
            vm.OpenRepositoryCommand.Execute(null);
        }
    }
}
