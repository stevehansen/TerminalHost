using System.Windows.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class ClaudeTasksPanelView : UserControl
{
    public ClaudeTasksPanelView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(System.Windows.DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Call OnOpened when panel is loaded with a DataContext
        if (e.Property == DataContextProperty && e.NewValue is ClaudeTasksPanelViewModel viewModel)
        {
            viewModel.OnOpened();
        }
    }
}
