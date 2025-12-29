using System.Windows.Controls;
using System.Windows.Input;

namespace TerminalHost.Views.Popups;

public partial class BranchComparisonView : UserControl
{
    public BranchComparisonView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            BranchComparisonPopup.IsOpen = false;
            e.Handled = true;
        }
    }
}
