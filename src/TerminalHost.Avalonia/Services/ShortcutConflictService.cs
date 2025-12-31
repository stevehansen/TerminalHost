using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for detecting keyboard shortcut conflicts between built-in shortcuts,
/// quick commands, and profiles.
/// </summary>
public static class ShortcutConflictService
{
    /// <summary>
    /// Dictionary of all built-in keyboard shortcuts and their descriptions.
    /// Uses macOS shortcuts (Cmd instead of Ctrl for primary modifier).
    /// </summary>
    public static readonly Dictionary<string, string> BuiltInShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navigation
        { "Cmd+1", "Jump to Tab 1" },
        { "Cmd+2", "Jump to Tab 2" },
        { "Cmd+3", "Jump to Tab 3" },
        { "Cmd+4", "Jump to Tab 4" },
        { "Cmd+5", "Jump to Tab 5" },
        { "Cmd+6", "Jump to Tab 6" },
        { "Cmd+7", "Jump to Tab 7" },
        { "Cmd+8", "Jump to Tab 8" },
        { "Cmd+9", "Jump to Tab 9" },
        { "Ctrl+Tab", "Next Tab" },
        { "Ctrl+Shift+Tab", "Previous Tab" },

        // Application
        { "Cmd+N", "Open New Project" },
        { "Cmd+W", "Close Tab" },
        { "Cmd+,", "Settings" },
        { "Cmd+Shift+P", "Command Palette" },
        { "Cmd+Shift+T", "Tab Switcher" },
        { "F1", "Help" },

        // Git
        { "Cmd+G", "Git Changes" },
        { "Cmd+B", "Git Branches" },
        { "Cmd+Shift+H", "Commit History" },
        { "Cmd+Shift+S", "Git Stash" },
        { "Cmd+Shift+G", "Git Reflog" },

        // Files
        { "Cmd+O", "File Preview" },
        { "Cmd+Shift+E", "File Edit" },
        { "Cmd+Shift+F", "Toggle File Explorer" },
        { "Cmd+F", "Search Across Files" },

        // Other features
        { "Cmd+Shift+N", "Scratch Pad" },
        { "Cmd+T", "Task Panel" },
        { "Cmd+Shift+L", "Toggle Layout Mode" },

        // Terminal
        { "Cmd+`", "Switch Terminal" },

        // Run
        { "F5", "Start/Stop Run" },
        { "Shift+F5", "Force Stop Run" },
    };

    /// <summary>
    /// Checks if a shortcut conflicts with built-in shortcuts, other quick commands, or profiles.
    /// </summary>
    /// <param name="shortcut">The shortcut to check (e.g., "Ctrl+Shift+C")</param>
    /// <param name="quickCommands">All quick commands to check against</param>
    /// <param name="profiles">All profiles to check against</param>
    /// <param name="excludeQuickCommandId">Optional ID of quick command to exclude from check (for self-editing)</param>
    /// <param name="excludeProfileName">Optional name of profile to exclude from check (for self-editing)</param>
    /// <returns>Conflict description or null if no conflict</returns>
    public static string? GetConflict(
        string shortcut,
        IEnumerable<QuickCommand>? quickCommands = null,
        IEnumerable<Profile>? profiles = null,
        string? excludeQuickCommandId = null,
        string? excludeProfileName = null)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        var normalizedShortcut = NormalizeShortcut(shortcut);
        if (normalizedShortcut == null)
            return null;

        // Check built-in shortcuts
        foreach (var builtin in BuiltInShortcuts)
        {
            if (NormalizeShortcut(builtin.Key) == normalizedShortcut)
            {
                return $"Conflicts with built-in shortcut: {builtin.Value} ({builtin.Key})";
            }
        }

        // Check other quick commands
        if (quickCommands != null)
        {
            foreach (var command in quickCommands)
            {
                if (!string.IsNullOrEmpty(excludeQuickCommandId) && command.Id == excludeQuickCommandId)
                    continue;

                if (!string.IsNullOrEmpty(command.Shortcut))
                {
                    var cmdNormalized = NormalizeShortcut(command.Shortcut);
                    if (cmdNormalized == normalizedShortcut)
                    {
                        return $"Conflicts with Quick Command: {command.Label}";
                    }
                }
            }
        }

        // Check profiles
        if (profiles != null)
        {
            foreach (var profile in profiles)
            {
                if (!string.IsNullOrEmpty(excludeProfileName) && profile.Name == excludeProfileName)
                    continue;

                if (!string.IsNullOrEmpty(profile.Shortcut))
                {
                    var profileNormalized = NormalizeShortcut(profile.Shortcut);
                    if (profileNormalized == normalizedShortcut)
                    {
                        return $"Conflicts with Profile: {profile.Name}";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes a shortcut string for comparison.
    /// Converts to uppercase and sorts modifiers consistently.
    /// </summary>
    private static string? NormalizeShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        var modifiers = new List<string>();
        string? key = null;

        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers.Add("CTRL");
                    break;
                case "CMD":
                case "META":
                case "COMMAND":
                    modifiers.Add("CMD");
                    break;
                case "ALT":
                case "OPT":
                case "OPTION":
                    modifiers.Add("ALT");
                    break;
                case "SHIFT":
                    modifiers.Add("SHIFT");
                    break;
                default:
                    // This is the key
                    key = upper;
                    break;
            }
        }

        if (key == null)
            return null;

        // Sort modifiers for consistent comparison
        modifiers.Sort();
        modifiers.Add(key);

        return string.Join("+", modifiers);
    }

    /// <summary>
    /// Gets all conflicts for a configuration (for validation on save).
    /// </summary>
    /// <param name="quickCommands">Quick commands to check</param>
    /// <param name="profiles">Profiles to check</param>
    /// <returns>List of warning messages for conflicts</returns>
    public static List<string> GetAllConflicts(
        IEnumerable<QuickCommand>? quickCommands,
        IEnumerable<Profile>? profiles)
    {
        var warnings = new List<string>();
        var quickCommandsList = quickCommands?.ToList() ?? [];
        var profilesList = profiles?.ToList() ?? [];

        // Check each quick command for conflicts
        foreach (var command in quickCommandsList)
        {
            if (string.IsNullOrEmpty(command.Shortcut))
                continue;

            var conflict = GetConflict(
                command.Shortcut,
                quickCommandsList,
                profilesList,
                excludeQuickCommandId: command.Id);

            if (conflict != null)
            {
                warnings.Add($"Quick Command '{command.Label}': {conflict}");
            }
        }

        // Check each profile for conflicts
        foreach (var profile in profilesList)
        {
            if (string.IsNullOrEmpty(profile.Shortcut))
                continue;

            var conflict = GetConflict(
                profile.Shortcut,
                quickCommandsList,
                profilesList,
                excludeProfileName: profile.Name);

            if (conflict != null)
            {
                warnings.Add($"Profile '{profile.Name}': {conflict}");
            }
        }

        return warnings;
    }
}
