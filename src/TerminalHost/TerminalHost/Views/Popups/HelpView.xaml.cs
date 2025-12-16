using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is MainViewModel mainViewModel)
            {
                if (mainViewModel.CloseHelpCommand.CanExecute(null))
                {
                    mainViewModel.CloseHelpCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
