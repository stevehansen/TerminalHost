using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class ReflogView : UserControl
{
    public ReflogView()
    {
        InitializeComponent();

        ReflogPopup.Opened += (s, e) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                ReflogList.Focus();
                if (ReflogList.Items.Count > 0 && ReflogList.SelectedIndex == -1)
                {
                    ReflogList.SelectedIndex = 0;
                }
            });
        };
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = DataContext as ReflogViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Down)
        {
            if (ReflogList.Items.Count > 0)
            {
                if (ReflogList.SelectedIndex < ReflogList.Items.Count - 1)
                {
                    ReflogList.SelectedIndex++;
                }
                else if (ReflogList.SelectedIndex == -1)
                {
                    ReflogList.SelectedIndex = 0;
                }
                ReflogList.ScrollIntoView(ReflogList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (ReflogList.Items.Count > 0)
            {
                if (ReflogList.SelectedIndex > 0)
                {
                    ReflogList.SelectedIndex--;
                }
                else if (ReflogList.SelectedIndex == -1)
                {
                    ReflogList.SelectedIndex = ReflogList.Items.Count - 1;
                }
                ReflogList.ScrollIntoView(ReflogList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (viewModel.SelectedEntry != null)
            {
                viewModel.CheckoutCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (viewModel.SelectedEntry != null)
            {
                viewModel.CopyHashCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}
