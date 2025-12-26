using System.Windows.Controls;
using System.Windows.Input;

namespace TerminalHost.Views.Popups;

/// <summary>
/// Interaction logic for ManageWorktreesView.xaml
/// </summary>
public partial class ManageWorktreesView : UserControl
{
    public ManageWorktreesView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is ViewModels.ManageWorktreesViewModel vm)
            {
                vm.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
