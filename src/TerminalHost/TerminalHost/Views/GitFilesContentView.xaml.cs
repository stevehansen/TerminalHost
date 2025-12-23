using System.Windows.Controls;

namespace TerminalHost.Views;

/// <summary>
/// Content view for Git Changes panel.
/// This view displays the content without any popup/window chrome,
/// making it suitable for use in the panel system (docked, popup, or window).
/// </summary>
public partial class GitFilesContentView : UserControl
{
    public GitFilesContentView()
    {
        InitializeComponent();
    }
}
