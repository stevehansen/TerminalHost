using TerminalHost.Core.Domain;
using System.Text.RegularExpressions;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for detecting keyboard shortcut conflicts.
/// Single source of truth for all built-in keyboard shortcuts.
/// </summary>
public static class ShortcutConflictService
{
    /// <summary>
    /// All built-in keyboard shortcuts organized by section.
    /// This is the authoritative source - used by both Help view and conflict detection.
    /// </summary>
    public static readonly List<ShortcutSection> BuiltInShortcutSections =
    [
        new("Tab Navigation",
        [
            new("Ctrl+PageDown", "Next tab"),
            new("Ctrl+PageUp", "Previous tab"),
            new("Ctrl+1-9", "Jump to tab 1-9"),
            new("Ctrl+Shift+T", "Open tab switcher (search tabs)"),
            new("Ctrl+W", "Close current tab"),
            new("Middle-click tab", "Close tab"),
        ]),

        new("Terminal",
        [
            new("Ctrl+`", "Switch between Custom/Shell terminal"),
            new("Links button", "Click to view detected URLs and file paths"),
        ]),

        new("Layout",
        [
            new("Ctrl+L", "Toggle layout mode (Tabs/Sidebar)"),
        ]),

        new("File Operations",
        [
            new("Ctrl+N", "Open new project (folder picker)"),
            new("Ctrl+E", "Open current folder in Explorer"),
            new("Ctrl+O", "Open file preview dialog"),
            new("Ctrl+Shift+E", "Open file editor"),
            new("Ctrl+Shift+F", "Toggle file explorer panel"),
            new("Ctrl+F3", "Search across files"),
        ]),

        new("Application",
        [
            new("Ctrl+,", "Open settings editor"),
            new("Ctrl+P", "Open settings (Profiles)"),
            new("Ctrl+Shift+P", "Open command palette"),
            new("Ctrl+Shift+N", "Open scratch pad (notes)"),
            new("Ctrl+T", "Open task panel"),
            new("Ctrl+Shift+Q", "Quick add task"),
            new("Ctrl+Shift+M", "Quick add note"),
            new("Ctrl+G", "Open git changes panel"),
            new("Ctrl+H", "Open commit history"),
            new("Ctrl+B", "Open git branch switcher"),
            new("Ctrl+Shift+S", "Open git stash manager"),
            new("Ctrl+Shift+G", "Open git reflog"),
            new("Ctrl+Shift+B", "View file blame"),
            new("Ctrl+Shift+O", "Repository quick access"),
            new("Ctrl+Shift+H", "GitHub Dashboard"),
            new("Ctrl+Shift+R", "PR Review Mode"),
            new("Ctrl+Shift+I", "Timeline Mode"),
            new("F1", "Show this help window"),
            new("F6", "Run tests"),
            new("Ctrl+M", "Markdown preview"),
        ]),

        new("Project Runner",
        [
            new("F5", "Start/Stop project run"),
            new("Shift+F5", "Force stop project run"),
        ]),

        new("Timeline Mode",
        [
            new("↑/↓", "Navigate intents"),
            new("←/→", "Navigate sessions"),
            new("Enter", "Open session detail"),
            new("Escape", "Close session detail"),
            new("Ctrl+Alt+N", "New Intent"),
            new("Ctrl+Alt+S", "Start session"),
            new("Ctrl+Alt+F", "Fork from session"),
        ]),
    ];

    /// <summary>
    /// Flat dictionary of built-in shortcuts for quick conflict lookup.
    /// Derived from BuiltInShortcutSections.
    /// </summary>
    public static readonly Dictionary<string, string> BuiltInShortcuts = BuildShortcutDictionary();

    private static Dictionary<string, string> BuildShortcutDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in BuiltInShortcutSections)
        {
            foreach (var item in section.Items)
            {
                // Skip non-keyboard shortcuts (like "Middle-click tab", "Links button")
                if (!item.Shortcut.Contains("Ctrl") && !item.Shortcut.Contains("Shift") &&
                    !item.Shortcut.Contains("Alt") && !item.Shortcut.StartsWith("F") &&
                    !item.Shortcut.Contains("Escape"))
                    continue;

                // Handle range shortcuts like "Ctrl+1-9"
                if (item.Shortcut.Contains("-") && item.Shortcut.Contains("Ctrl+"))
                {
                    var match = Regex.Match(item.Shortcut, @"Ctrl\+(\d)-(\d)");
                    if (match.Success)
                    {
                        var start = int.Parse(match.Groups[1].Value);
                        var end = int.Parse(match.Groups[2].Value);
                        for (int i = start; i <= end; i++)
                        {
                            dict[$"Ctrl+{i}"] = item.Description;
                        }
                        continue;
                    }
                }

                dict[item.Shortcut] = item.Description;
            }
        }

        // Add Escape which isn't in sections but is a built-in
        dict["Escape"] = "Close popups";

        return dict;
    }

    /// <summary>
    /// Check if a shortcut conflicts with a built-in shortcut.
    /// </summary>
    /// <param name="shortcut">The shortcut to check (e.g., "Ctrl+Shift+C")</param>
    /// <returns>The description of the built-in feature if conflict found, null otherwise</returns>
    public static string? GetBuiltInConflict(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        var normalized = NormalizeShortcut(shortcut);
        return BuiltInShortcuts.TryGetValue(normalized, out var description) ? description : null;
    }

    /// <summary>
    /// Check if a shortcut conflicts with any of the provided user shortcuts.
    /// </summary>
    /// <param name="shortcut">The shortcut to check</param>
    /// <param name="existingShortcuts">Dictionary of existing shortcuts (shortcut -> label)</param>
    /// <param name="excludeLabel">Label to exclude from comparison (for self-check)</param>
    /// <returns>The label of the conflicting shortcut if found, null otherwise</returns>
    public static string? GetUserShortcutConflict(string? shortcut, IEnumerable<(string? Shortcut, string Label)> existingShortcuts, string? excludeLabel = null)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        var normalized = NormalizeShortcut(shortcut);

        foreach (var (existingShortcut, label) in existingShortcuts)
        {
            if (string.IsNullOrWhiteSpace(existingShortcut))
                continue;

            if (excludeLabel != null && label == excludeLabel)
                continue;

            if (string.Equals(NormalizeShortcut(existingShortcut), normalized, StringComparison.OrdinalIgnoreCase))
                return label;
        }

        return null;
    }

    /// <summary>
    /// Normalize a shortcut string for comparison (handles variations like "Ctrl+Shift+C" vs "CTRL+SHIFT+C")
    /// </summary>
    public static string NormalizeShortcut(string shortcut)
    {
        // Normalize to consistent format: capitalize modifiers, preserve key case
        var parts = shortcut.Split('+');
        var normalized = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var lower = trimmed.ToLowerInvariant();

            // Normalize modifier names
            if (lower == "ctrl" || lower == "control")
                normalized.Add("Ctrl");
            else if (lower == "shift")
                normalized.Add("Shift");
            else if (lower == "alt")
                normalized.Add("Alt");
            else
                normalized.Add(trimmed);
        }

        return string.Join("+", normalized);
    }

    /// <summary>
    /// Validate a shortcut format.
    /// </summary>
    /// <returns>Error message if invalid, null if valid</returns>
    public static string? ValidateShortcutFormat(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null; // Empty is valid (no shortcut assigned)

        var parts = shortcut.Split('+');
        if (parts.Length == 0)
            return "Invalid shortcut format";

        var hasModifier = false;
        var hasKey = false;

        foreach (var part in parts)
        {
            var trimmed = part.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(trimmed))
                return "Invalid shortcut format";

            if (trimmed == "ctrl" || trimmed == "control" || trimmed == "shift" || trimmed == "alt")
            {
                hasModifier = true;
            }
            else
            {
                hasKey = true;
            }
        }

        if (!hasKey)
            return "Shortcut must include a key (e.g., 'Ctrl+C')";

        if (!hasModifier)
            return "Shortcut should include a modifier (Ctrl, Shift, or Alt)";

        return null;
    }
}
