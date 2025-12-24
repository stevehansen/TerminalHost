using System.Windows.Input;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitStashView : UserControl
{
    public GitStashView()
    {
        InitializeComponent();

        // Focus the message box when popup opens
        GitStashPopup.Opened += (s, e) =>
        {
            // Use Dispatcher to ensure focus happens after popup is fully rendered
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                StashMessageBox.Focus();
                Keyboard.Focus(StashMessageBox);
            });
        };
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = DataContext as GitStashViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Down)
        {
            // Move selection down in the list
            if (StashList.Items.Count > 0)
            {
                if (StashList.SelectedIndex < StashList.Items.Count - 1)
                {
                    StashList.SelectedIndex++;
                }
                else if (StashList.SelectedIndex == -1)
                {
                    StashList.SelectedIndex = 0;
                }
                StashList.ScrollIntoView(StashList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            // Move selection up in the list
            if (StashList.Items.Count > 0)
            {
                if (StashList.SelectedIndex > 0)
                {
                    StashList.SelectedIndex--;
                }
                else if (StashList.SelectedIndex == -1)
                {
                    StashList.SelectedIndex = StashList.Items.Count - 1;
                }
                StashList.ScrollIntoView(StashList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // If focus is on message box and there's text, create stash
            // Otherwise pop selected stash
            if (StashMessageBox.IsFocused && !string.IsNullOrWhiteSpace(viewModel.StashMessage))
            {
                viewModel.CreateStashCommand.Execute(null);
            }
            else if (viewModel.SelectedStash != null)
            {
                viewModel.PopStashCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void StashList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var viewModel = DataContext as GitStashViewModel;
        if (viewModel == null) return;

        if (StashList.SelectedItem is GitStashEntry)
        {
            viewModel.PopStashCommand.Execute(null);
        }
    }
}
