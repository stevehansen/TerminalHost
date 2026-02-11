using System.Windows;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Views;

/// <summary>
/// Generic window for hosting any IPanelableViewModel content.
/// Uses DataTemplates from PanelContentTemplates.xaml to render the appropriate view.
/// </summary>
public partial class PanelWindow : Window
{
    private bool _isDocking;

    public PanelWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    /// <summary>
    /// Event raised when the user requests to dock back to panel.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? DockRequested;

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IPanelableViewModel vm)
        {
            _isDocking = true;
            DockRequested?.Invoke(this, vm);
            Close();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IPanelableViewModel vm && !_isDocking)
        {
            vm.IsOpen = false;
        }
    }
}
