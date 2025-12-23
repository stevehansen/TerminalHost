using System.ComponentModel;
using System.Windows.Input;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Represents a custom command button that can appear in panel headers.
/// </summary>
public class PanelHeaderCommand
{
    /// <summary>
    /// Icon to display (emoji or symbol).
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>
    /// Tooltip text for the button.
    /// </summary>
    public required string Tooltip { get; init; }

    /// <summary>
    /// Command to execute when clicked.
    /// </summary>
    public required ICommand Command { get; init; }
}

/// <summary>
/// Defines the display state of a panelable content view.
/// </summary>
public enum PanelDisplayState
{
    /// <summary>
    /// Docked as a panel tab in the main window.
    /// </summary>
    Panel,

    /// <summary>
    /// Floating as a popup within the application.
    /// </summary>
    Popup,

    /// <summary>
    /// Detached as an independent window.
    /// </summary>
    Window
}

/// <summary>
/// Defines which side of the main content area a panel can dock to.
/// </summary>
public enum PanelSide
{
    Left,
    Right
}

/// <summary>
/// Interface for ViewModels that support transitioning between Panel, Popup, and Window states.
/// Implementing this interface allows content to be docked as a tab, shown as a floating popup,
/// or detached to a separate window.
/// </summary>
public interface IPanelableViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Unique identifier for this panel type (e.g., "fileExplorer", "markdownPreview", "gitChanges").
    /// Used for configuration persistence and panel registration.
    /// </summary>
    string PanelId { get; }

    /// <summary>
    /// Display title shown in the panel tab and header.
    /// </summary>
    string PanelTitle { get; }

    /// <summary>
    /// Icon (emoji or symbol) shown in the panel tab.
    /// </summary>
    string PanelIcon { get; }

    /// <summary>
    /// Current display state of the panel.
    /// </summary>
    PanelDisplayState DisplayState { get; set; }

    /// <summary>
    /// Preferred docking side when in Panel state.
    /// </summary>
    PanelSide PreferredSide { get; set; }

    /// <summary>
    /// Whether the panel is currently open/visible.
    /// </summary>
    bool IsOpen { get; set; }

    /// <summary>
    /// Width of the panel content (used for popup and panel sizing).
    /// </summary>
    double Width { get; set; }

    /// <summary>
    /// Height of the panel content (used for popup sizing).
    /// </summary>
    double Height { get; set; }

    /// <summary>
    /// Command to dock the panel (from Popup or Window state to Panel state).
    /// </summary>
    ICommand DockCommand { get; }

    /// <summary>
    /// Command to undock the panel (from Panel state to Popup state).
    /// </summary>
    ICommand UndockCommand { get; }

    /// <summary>
    /// Command to detach the panel to a window (from Panel or Popup state to Window state).
    /// </summary>
    ICommand DetachCommand { get; }

    /// <summary>
    /// Command to close the panel.
    /// </summary>
    ICommand CloseCommand { get; }

    /// <summary>
    /// Optional collection of custom header commands specific to this panel type.
    /// These buttons appear before the standard Undock/PopOut/Close buttons.
    /// </summary>
    IEnumerable<PanelHeaderCommand>? HeaderCommands { get; }

    /// <summary>
    /// Optional status text displayed in the footer (popup/window modes only).
    /// </summary>
    string? StatusText { get; }

    /// <summary>
    /// Raised when the panel requests a state change.
    /// The handler should perform the actual state transition (e.g., creating a window).
    /// </summary>
    event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;
}

/// <summary>
/// Event arguments for panel state change requests.
/// </summary>
public class PanelStateChangeRequestedEventArgs : EventArgs
{
    /// <summary>
    /// The requested new state for the panel.
    /// </summary>
    public PanelDisplayState RequestedState { get; }

    /// <summary>
    /// The side to dock to (only relevant when RequestedState is Panel).
    /// </summary>
    public PanelSide? DockSide { get; }

    public PanelStateChangeRequestedEventArgs(PanelDisplayState requestedState, PanelSide? dockSide = null)
    {
        RequestedState = requestedState;
        DockSide = dockSide;
    }
}
