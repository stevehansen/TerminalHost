using System.Windows.Controls;

namespace TerminalHost.Views;

/// <summary>
/// Content view for Scratch Pad panel.
/// This view displays the content without any popup/window chrome,
/// making it suitable for use in the panel system (docked, popup, or window).
/// </summary>
public partial class ScratchPadContentView : UserControl
{
    public ScratchPadContentView()
    {
        InitializeComponent();
    }
}
