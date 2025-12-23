using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Controls;

/// <summary>
/// Generic popup for hosting any IPanelableViewModel content.
/// Uses DraggablePopup for floating behavior with drag/resize support.
/// Uses DataTemplates from PanelContentTemplates.xaml to render the appropriate view.
/// </summary>
public partial class PanelPopup : UserControl
{
    public PanelPopup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Event raised when the user requests to dock back to panel.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? DockRequested;

    /// <summary>
    /// Event raised when the user requests to pop out to a window.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? PopOutRequested;

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IPanelableViewModel vm)
        {
            vm.IsOpen = false;
            DockRequested?.Invoke(this, vm);
        }
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IPanelableViewModel vm)
        {
            vm.IsOpen = false;
            PopOutRequested?.Invoke(this, vm);
        }
    }
}
