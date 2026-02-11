using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// Base class for all panelable ViewModels.
/// Provides common implementation of IPanelableViewModel including state management,
/// commands for dock/undock/detach/close, and events for state changes.
/// </summary>
public abstract partial class BasePanelViewModel : ObservableObject, IPanelableViewModel
{
    #region Abstract Properties (must be implemented by subclasses)

    /// <summary>
    /// Unique identifier for this panel type (e.g., "gitChanges", "scratchPad").
    /// </summary>
    public abstract string PanelId { get; }

    /// <summary>
    /// Display title shown in the panel tab and header.
    /// </summary>
    public abstract string PanelTitle { get; }

    /// <summary>
    /// Icon (emoji or symbol) shown in the panel tab.
    /// </summary>
    public abstract string PanelIcon { get; }

    /// <summary>
    /// Size preset for window mode.
    /// </summary>
    public abstract PanelSizePreset SizePreset { get; }

    #endregion

    #region Virtual Properties (can be overridden)

    /// <summary>
    /// Optional collection of custom header commands specific to this panel type.
    /// </summary>
    public virtual IEnumerable<PanelHeaderCommand>? HeaderCommands => null;

    /// <summary>
    /// Optional status text displayed in the footer (window mode only).
    /// </summary>
    public virtual string? StatusText => null;

    #endregion

    #region State Properties

    [ObservableProperty]
    private PanelDisplayState _displayState = PanelDisplayState.Panel;

    [ObservableProperty]
    private PanelSide _preferredSide = PanelSide.Right;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private double _width = 600;

    [ObservableProperty]
    private double _height = 500;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    #endregion

    #region Commands

    public ICommand DockCommand { get; }
    public ICommand DetachCommand { get; }
    public ICommand CloseCommand { get; }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the panel requests a state change (dock, detach).
    /// The handler should perform the actual state transition.
    /// </summary>
    public event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;

    /// <summary>
    /// Raised when the panel wants to be shown in its current DisplayState.
    /// </summary>
    public event EventHandler? ShowRequested;

    #endregion

    protected BasePanelViewModel()
    {
        DockCommand = new RelayCommand<PanelSide?>(OnDock);
        DetachCommand = new RelayCommand(OnDetach);
        CloseCommand = new RelayCommand(OnClose);
    }

    #region Command Handlers

    /// <summary>
    /// Handles dock command - requests transition to Panel state.
    /// </summary>
    protected virtual void OnDock(PanelSide? side)
    {
        var dockSide = side ?? PreferredSide;
        PreferredSide = dockSide;
        DisplayState = PanelDisplayState.Panel;
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Panel, dockSide));
        // After state change, request to show in docked position
        RequestShow();
    }

    /// <summary>
    /// Handles detach command - requests transition to Window state.
    /// </summary>
    protected virtual void OnDetach()
    {
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Window));
        // After state change removes from docked panels, request to show as window
        RequestShow();
    }

    /// <summary>
    /// Handles close command.
    /// Override this if you need custom close behavior (e.g., saving state).
    /// </summary>
    protected virtual void OnClose()
    {
        IsOpen = false;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates the display state without raising events.
    /// Called by the panel host when state changes are applied.
    /// </summary>
    public void SetDisplayState(PanelDisplayState state, PanelSide? side = null)
    {
        DisplayState = state;
        if (side.HasValue)
        {
            PreferredSide = side.Value;
        }
    }

    /// <summary>
    /// Requests the panel to be shown in its current DisplayState.
    /// </summary>
    protected void RequestShow()
    {
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
