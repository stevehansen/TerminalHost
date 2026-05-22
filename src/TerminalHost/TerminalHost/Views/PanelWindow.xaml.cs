using System.ComponentModel;
using System.Windows;
using TerminalHost.Core.Interfaces;
using TerminalHost.Windows.Platform;

namespace TerminalHost.Views;

/// <summary>
/// Generic window for hosting any IPanelableViewModel content.
/// Uses DataTemplates from PanelContentTemplates.xaml to render the appropriate view.
/// </summary>
public partial class PanelWindow : Window
{
    private bool _suppressCloseChrome;

    public PanelWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkModeHelper.EnableDarkMode(this);
    }

    /// <summary>
    /// Event raised when the user requests to dock back to panel.
    /// The hosting surface is responsible for closing this window after handling the event.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? DockRequested;

    /// <summary>
    /// Marks an upcoming close as surface-initiated (programmatic Unmount or dock-back).
    /// Suppresses the <see cref="IPanelCloseGuard"/> prompt and the <c>vm.IsOpen = false</c>
    /// side-effect — both belong to the router/surface, not the user-initiated close gesture.
    /// </summary>
    public void BeginProgrammaticClose()
    {
        _suppressCloseChrome = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_suppressCloseChrome && DataContext is IPanelCloseGuard guard && !guard.CanClose())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IPanelableViewModel vm)
        {
            DockRequested?.Invoke(this, vm);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IPanelableViewModel vm && !_suppressCloseChrome)
        {
            vm.IsOpen = false;
        }
    }
}
