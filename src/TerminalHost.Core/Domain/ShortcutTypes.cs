using System.Linq;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Specifies which platform(s) a shortcut is available on.
/// </summary>
public enum ShortcutPlatform
{
    /// <summary>Available on all platforms (Windows and macOS).</summary>
    All,
    /// <summary>Available only on Windows.</summary>
    Windows,
    /// <summary>Available only on macOS.</summary>
    MacOS
}

/// <summary>
/// Represents a keyboard shortcut with its description and platform availability.
/// </summary>
/// <param name="Shortcut">The keyboard shortcut (use Ctrl+ prefix, will be converted to Cmd+ for macOS display).</param>
/// <param name="Description">Description of what the shortcut does.</param>
/// <param name="Platform">Which platform(s) this shortcut is available on.</param>
public record ShortcutItem(string Shortcut, string Description, ShortcutPlatform Platform = ShortcutPlatform.All)
{
    /// <summary>
    /// Gets the display-friendly shortcut for a specific platform.
    /// Converts Ctrl+ to ⌘ (Cmd) for macOS, and formats modifiers with symbols.
    /// </summary>
    public string GetDisplayShortcut(bool isMacOS)
    {
        if (isMacOS)
        {
            // Convert to macOS symbols
            return Shortcut
                .Replace("Ctrl+", "⌘")
                .Replace("Alt+", "⌥")
                .Replace("Shift+", "⇧")
                .Replace("+", " ");
        }
        return Shortcut;
    }
}

/// <summary>
/// Represents a group of related shortcuts.
/// </summary>
public record ShortcutSection(string Name, List<ShortcutItem> Items)
{
    /// <summary>
    /// Gets only the shortcuts available on the specified platform.
    /// </summary>
    public List<ShortcutItem> GetItemsForPlatform(bool isMacOS)
    {
        var targetPlatform = isMacOS ? ShortcutPlatform.MacOS : ShortcutPlatform.Windows;
        return Items
            .Where(item => item.Platform == ShortcutPlatform.All || item.Platform == targetPlatform)
            .ToList();
    }
}
