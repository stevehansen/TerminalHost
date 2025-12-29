using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class UnifiedGitPanelView : UserControl
{
    public UnifiedGitPanelView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = DataContext as UnifiedGitPanelViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
        // Tab switching with Ctrl+1-5
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1:
                    viewModel.SwitchToTabCommand.Execute(GitPanelTab.Branches);
                    e.Handled = true;
                    break;
                case Key.D2:
                    viewModel.SwitchToTabCommand.Execute(GitPanelTab.Changes);
                    e.Handled = true;
                    break;
                case Key.D3:
                    viewModel.SwitchToTabCommand.Execute(GitPanelTab.History);
                    e.Handled = true;
                    break;
                case Key.D4:
                    viewModel.SwitchToTabCommand.Execute(GitPanelTab.Stash);
                    e.Handled = true;
                    break;
                case Key.D5:
                    viewModel.SwitchToTabCommand.Execute(GitPanelTab.Comparison);
                    e.Handled = true;
                    break;
            }
        }
    }
}
