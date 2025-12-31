using System.Collections.Generic;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for screen/monitor information.
/// Replaces platform-specific screen APIs.
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
