using System.Windows.Input;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Views.Popups;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is IPanelableViewModel panel)
        {
            if (panel.CloseCommand.CanExecute(null))
            {
                panel.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
