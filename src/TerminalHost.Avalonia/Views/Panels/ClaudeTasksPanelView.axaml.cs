using Avalonia.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Panels;

public partial class ClaudeTasksPanelView : UserControl
{
    public ClaudeTasksPanelView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Call OnOpened when panel is loaded with a DataContext
        if (DataContext is ClaudeTasksPanelViewModel viewModel)
        {
            viewModel.OnOpened();
        }
    }
}
