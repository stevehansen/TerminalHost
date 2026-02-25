using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalHost.Views.Popups;

public partial class BranchComparisonView : UserControl
{
    public BranchComparisonView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
