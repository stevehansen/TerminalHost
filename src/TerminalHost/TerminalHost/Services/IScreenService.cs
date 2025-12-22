namespace TerminalHost.Services;

/// <summary>
/// Abstraction for screen/monitor information.
/// Replaces System.Windows.Forms.Screen and SystemParameters.
/// </summary>
public interface IScreenService
{
    /// <summary>
    /// Gets the primary screen bounds.
    /// </summary>
    ScreenBounds GetPrimaryScreenBounds();

    /// <summary>
    /// Gets the screen containing the specified point.
    /// </summary>
    ScreenBounds GetScreenFromPoint(double x, double y);

    /// <summary>
    /// Gets the working area (excluding taskbar/dock) of the primary screen.
    /// </summary>
    ScreenBounds GetPrimaryWorkingArea();

    /// <summary>
    /// Gets all available screens.
    /// </summary>
    IReadOnlyList<ScreenBounds> GetAllScreens();
}

public record ScreenBounds(double X, double Y, double Width, double Height);
