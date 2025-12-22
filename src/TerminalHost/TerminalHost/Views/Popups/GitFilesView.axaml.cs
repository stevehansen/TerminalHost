using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalHost.Views.Popups;

public partial class GitFilesView : UserControl
{
    public GitFilesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
