namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for controlling Windows taskbar progress/glow states.
/// Used to notify users of terminal activity when app is not focused.
/// </summary>
public interface ITaskbarProgressService
{
    /// <summary>
    /// Shows an amber/yellow glow on the taskbar (paused/warning state).
    /// Used when Claude is waiting for user input.
    /// </summary>
    void ShowAmberGlow();

    /// <summary>
    /// Shows a green glow on the taskbar (normal/completed state).
    /// Used when terminal activity has completed.
    /// </summary>
    void ShowGreenGlow();

    /// <summary>
    /// Clears any taskbar glow/progress state.
    /// Called when app gains focus or alerts are dismissed.
    /// </summary>
    void ClearGlow();

    /// <summary>
    /// Whether the service is enabled (based on platform support and user preferences).
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Whether audio notifications are enabled for state transitions.
    /// </summary>
    bool IsAudioEnabled { get; set; }
}
