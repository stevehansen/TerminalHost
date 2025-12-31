namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents screen/display bounds.
/// </summary>
/// <param name="X">Left position of the screen.</param>
/// <param name="Y">Top position of the screen.</param>
/// <param name="Width">Width of the screen.</param>
/// <param name="Height">Height of the screen.</param>
public record ScreenBounds(double X, double Y, double Width, double Height);
