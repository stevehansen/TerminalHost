using Avalonia.Controls;
using Avalonia.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class ManageWorktreesView : UserControl
{
    public ManageWorktreesView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && DataContext is ManageWorktreesViewModel vm && vm.IsOpen)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }
}