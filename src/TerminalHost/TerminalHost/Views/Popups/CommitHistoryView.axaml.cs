using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalHost.Views.Popups;

public partial class CommitHistoryView : UserControl
{
    public CommitHistoryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
