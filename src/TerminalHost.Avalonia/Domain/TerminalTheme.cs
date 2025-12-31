using Avalonia.Media;

namespace TerminalHost.Domain;

/// <summary>
/// Terminal color theme definition.
/// </summary>
public class TerminalTheme
{
    public string Name { get; set; } = "Default";
    public Color Background { get; set; }
    public Color Foreground { get; set; }
    public Color SelectionBackground { get; set; }
    public Color CursorColor { get; set; }
    public Color[] AnsiColors { get; set; } = new Color[16];

    /// <summary>
    /// Campbell theme (Windows Terminal default).
    /// </summary>
    public static TerminalTheme Campbell => new()
    {
        Name = "Campbell",
        Background = Color.FromRgb(0x0C, 0x0C, 0x0C),
        Foreground = Color.FromRgb(0xCC, 0xCC, 0xCC),
        SelectionBackground = Color.FromRgb(0x26, 0x4F, 0x78),
        CursorColor = Color.FromRgb(0xCC, 0xCC, 0xCC),
        AnsiColors =
        [
            Color.FromRgb(0x0C, 0x0C, 0x0C), // Black
            Color.FromRgb(0xC5, 0x0F, 0x1F), // Red
            Color.FromRgb(0x13, 0xA1, 0x0E), // Green
            Color.FromRgb(0xC1, 0x9C, 0x00), // Yellow
            Color.FromRgb(0x00, 0x37, 0xDA), // Blue
            Color.FromRgb(0x88, 0x17, 0x98), // Magenta
            Color.FromRgb(0x3A, 0x96, 0xDD), // Cyan
            Color.FromRgb(0xCC, 0xCC, 0xCC), // White
            Color.FromRgb(0x76, 0x76, 0x76), // Bright Black
            Color.FromRgb(0xE7, 0x48, 0x56), // Bright Red
            Color.FromRgb(0x16, 0xC6, 0x0C), // Bright Green
            Color.FromRgb(0xF9, 0xF1, 0xA5), // Bright Yellow
            Color.FromRgb(0x3B, 0x78, 0xFF), // Bright Blue
            Color.FromRgb(0xB4, 0x00, 0x9E), // Bright Magenta
            Color.FromRgb(0x61, 0xD6, 0xD6), // Bright Cyan
            Color.FromRgb(0xF2, 0xF2, 0xF2), // Bright White
        ]
    };

    /// <summary>
    /// One Dark theme.
    /// </summary>
    public static TerminalTheme OneDark => new()
    {
        Name = "One Dark",
        Background = Color.FromRgb(0x28, 0x2C, 0x34),
        Foreground = Color.FromRgb(0xAB, 0xB2, 0xBF),
        SelectionBackground = Color.FromRgb(0x3E, 0x44, 0x51),
        CursorColor = Color.FromRgb(0x52, 0x8B, 0xFF),
        AnsiColors =
        [
            Color.FromRgb(0x28, 0x2C, 0x34), // Black
            Color.FromRgb(0xE0, 0x6C, 0x75), // Red
            Color.FromRgb(0x98, 0xC3, 0x79), // Green
            Color.FromRgb(0xE5, 0xC0, 0x7B), // Yellow
            Color.FromRgb(0x61, 0xAF, 0xEF), // Blue
            Color.FromRgb(0xC6, 0x78, 0xDD), // Magenta
            Color.FromRgb(0x56, 0xB6, 0xC2), // Cyan
            Color.FromRgb(0xAB, 0xB2, 0xBF), // White
            Color.FromRgb(0x5C, 0x63, 0x70), // Bright Black
            Color.FromRgb(0xE0, 0x6C, 0x75), // Bright Red
            Color.FromRgb(0x98, 0xC3, 0x79), // Bright Green
            Color.FromRgb(0xE5, 0xC0, 0x7B), // Bright Yellow
            Color.FromRgb(0x61, 0xAF, 0xEF), // Bright Blue
            Color.FromRgb(0xC6, 0x78, 0xDD), // Bright Magenta
            Color.FromRgb(0x56, 0xB6, 0xC2), // Bright Cyan
            Color.FromRgb(0xFF, 0xFF, 0xFF), // Bright White
        ]
    };

    /// <summary>
    /// Solarized Dark theme.
    /// </summary>
    public static TerminalTheme SolarizedDark => new()
    {
        Name = "Solarized Dark",
        Background = Color.FromRgb(0x00, 0x2B, 0x36),
        Foreground = Color.FromRgb(0x83, 0x94, 0x96),
        SelectionBackground = Color.FromRgb(0x07, 0x36, 0x42),
        CursorColor = Color.FromRgb(0x83, 0x94, 0x96),
        AnsiColors =
        [
            Color.FromRgb(0x07, 0x36, 0x42), // Black
            Color.FromRgb(0xDC, 0x32, 0x2F), // Red
            Color.FromRgb(0x85, 0x99, 0x00), // Green
            Color.FromRgb(0xB5, 0x89, 0x00), // Yellow
            Color.FromRgb(0x26, 0x8B, 0xD2), // Blue
            Color.FromRgb(0xD3, 0x36, 0x82), // Magenta
            Color.FromRgb(0x2A, 0xA1, 0x98), // Cyan
            Color.FromRgb(0xEE, 0xE8, 0xD5), // White
            Color.FromRgb(0x00, 0x2B, 0x36), // Bright Black
            Color.FromRgb(0xCB, 0x4B, 0x16), // Bright Red (Orange)
            Color.FromRgb(0x58, 0x6E, 0x75), // Bright Green
            Color.FromRgb(0x65, 0x7B, 0x83), // Bright Yellow
            Color.FromRgb(0x83, 0x94, 0x96), // Bright Blue
            Color.FromRgb(0x6C, 0x71, 0xC4), // Bright Magenta (Violet)
            Color.FromRgb(0x93, 0xA1, 0xA1), // Bright Cyan
            Color.FromRgb(0xFD, 0xF6, 0xE3), // Bright White
        ]
    };
}
