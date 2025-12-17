using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

/// <summary>
/// Code-behind for PrReviewView.xaml - PR Review Mode popup.
/// </summary>
public partial class PrReviewView : Popup
{
    public PrReviewView()
    {
        InitializeComponent();

        // Focus the popup content when opened so keyboard events work
        Opened += (s, e) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                PopupBorder.Focus();
                Keyboard.Focus(PopupBorder);
            });
        };
    }

    private void PopupBorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is PrReviewViewModel vm)
            {
                vm.CloseCommand.Execute(null);
            }
            else
            {
                IsOpen = false;
            }
            e.Handled = true;
        }
    }
}
