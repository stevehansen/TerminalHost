namespace TerminalHost.Core.Services;

/// <summary>
/// Service for detecting keyboard shortcut conflicts.
/// </summary>
public static class ShortcutConflictService
{
    /// <summary>
    /// All built-in keyboard shortcuts that cannot be overridden by user shortcuts.
    /// </summary>
    public static readonly Dictionary<string, string> BuiltInShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tab Navigation
        ["Ctrl+PageDown"] = "Next tab",
        ["Ctrl+PageUp"] = "Previous tab",
        ["Ctrl+1"] = "Jump to tab 1",
        ["Ctrl+2"] = "Jump to tab 2",
        ["Ctrl+3"] = "Jump to tab 3",
        ["Ctrl+4"] = "Jump to tab 4",
        ["Ctrl+5"] = "Jump to tab 5",
        ["Ctrl+6"] = "Jump to tab 6",
        ["Ctrl+7"] = "Jump to tab 7",
        ["Ctrl+8"] = "Jump to tab 8",
        ["Ctrl+9"] = "Jump to tab 9",
        ["Ctrl+Shift+T"] = "Open tab switcher",
        ["Ctrl+W"] = "Close current tab",

        // Terminal
        ["Ctrl+`"] = "Switch Custom/Shell terminal",

        // Layout
        ["Ctrl+L"] = "Toggle layout mode",

        // File Operations
        ["Ctrl+N"] = "Open new project",
        ["Ctrl+E"] = "Open in Explorer",
        ["Ctrl+O"] = "Open file preview",
        ["Ctrl+Shift+E"] = "Open file editor",
        ["Ctrl+Shift+F"] = "Toggle file explorer",
        ["Ctrl+F3"] = "Search across files",

        // Application
        ["Ctrl+,"] = "Open settings",
        ["Ctrl+P"] = "Open profiles",
        ["Ctrl+Shift+P"] = "Command palette",
        ["Ctrl+Shift+N"] = "Open scratch pad",
        ["Ctrl+T"] = "Open task panel",
        ["Ctrl+Shift+Q"] = "Quick add task",
        ["Ctrl+Shift+M"] = "Quick add note",
        ["Ctrl+G"] = "Git changes panel",
        ["Ctrl+H"] = "Commit history",
        ["Ctrl+B"] = "Branch switcher",
        ["Ctrl+Shift+S"] = "Stash manager",
        ["Ctrl+Shift+G"] = "Git reflog",
        ["Ctrl+Shift+B"] = "File blame",
        ["Ctrl+Shift+O"] = "Repository quick access",
        ["Ctrl+Shift+H"] = "GitHub Dashboard",
        ["Ctrl+Shift+R"] = "PR Review Mode",
        ["F1"] = "Help window",
        ["F5"] = "Start/Stop project",
        ["Shift+F5"] = "Force stop project",
        ["F6"] = "Run tests",
        ["Ctrl+M"] = "Markdown preview",
        ["Escape"] = "Close popups",
    };

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
