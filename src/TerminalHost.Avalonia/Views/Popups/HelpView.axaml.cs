using Avalonia.Controls;
using Avalonia.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();

        // Attach KeyDown event handler
        KeyDown += HelpView_KeyDown;
    }

    private void HelpView_KeyDown(object? sender, KeyEventArgs e)
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