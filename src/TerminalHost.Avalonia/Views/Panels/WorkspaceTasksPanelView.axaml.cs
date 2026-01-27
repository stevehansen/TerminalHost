using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalHost.Views.Panels;

public partial class WorkspaceTasksPanelView : UserControl
{
    public WorkspaceTasksPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
