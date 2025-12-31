using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitBranchView : UserControl
{
    public GitBranchView()
    {
        InitializeComponent();

        // Focus the search box when view becomes visible
        this.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(IsVisible) && IsVisible)
            {
                // Use Dispatcher to ensure focus happens after view is fully rendered
                Dispatcher.UIThread.Post(() =>
                {
                    GitBranchSearchBox.Focus();
                    GitBranchSearchBox.SelectAll();
                }, DispatcherPriority.Input);
            }
        };

        // Handle keyboard navigation
        this.KeyDown += UserControl_KeyDown;
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