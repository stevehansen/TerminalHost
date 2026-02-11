using System.Linq;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for detecting keyboard shortcut conflicts.
/// Single source of truth for all built-in keyboard shortcuts across all platforms.
///
/// IMPORTANT: When adding shortcuts, consider platform availability:
/// - Use ShortcutPlatform.All for shortcuts that work on both Windows and macOS
/// - Use ShortcutPlatform.Windows for Windows-only shortcuts
/// - Use ShortcutPlatform.MacOS for macOS-only shortcuts
/// - If a shortcut is reserved on one platform, it should be reserved on both
///   (to maintain consistency and avoid conflicts when porting features)
/// </summary>
public static class ShortcutConflictService
{
    /// <summary>
    /// All built-in keyboard shortcuts organized by section.
    /// This is the authoritative source - used by both Help view and conflict detection.
    /// Shortcuts use Ctrl+ prefix (converted to Cmd/⌘ for macOS display).
    /// </summary>
    public static readonly List<ShortcutSection> BuiltInShortcutSections =
    [
        new("Tab Navigation",
        [
            // Windows uses PageDown/PageUp, macOS uses Cmd+Alt+Arrow
            new("Ctrl+PageDown", "Next tab", ShortcutPlatform.Windows),
            new("Ctrl+PageUp", "Previous tab", ShortcutPlatform.Windows),
            new("Ctrl+Alt+Right", "Next tab", ShortcutPlatform.MacOS),
            new("Ctrl+Alt+Left", "Previous tab", ShortcutPlatform.MacOS),
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
            new("Ctrl+O", "File viewer (center panel)"),
            new("Ctrl+Shift+E", "File editor (center panel)"),
            new("Ctrl+Shift+F", "Toggle file explorer (right sidebar)"),
            new("Ctrl+F3", "Search across files (center panel)"),
        ]),

        new("Application",
        [
            new("Ctrl+,", "Open settings editor"),
            new("Ctrl+P", "Open settings (Profiles)"),
            new("Ctrl+Shift+P", "Open command palette"),
            new("Ctrl+Shift+N", "Open scratch pad (notes)"),
            new("Ctrl+Shift+O", "Repository quick access"),
            new("Ctrl+Shift+I", "Timeline Mode"),
            new("Ctrl+Shift+K", "Claude Tasks (right sidebar)"),
            new("F1", "Show this help window"),
            new("Ctrl+F1", "What's New / Recent Features"),
            new("Ctrl+M", "Markdown preview (center panel)"),
            new("F4", "Toggle voice commands"),
        ]),

        new("Git & GitHub",
        [
            new("Alt+G", "Git panel - Changes tab (center panel)"),
            new("Ctrl+H", "Git panel - History tab (center panel)"),
            new("Ctrl+B", "Git panel - Branches tab (center panel)"),
            new("Ctrl+Shift+D", "Git Pull (stash, pull --rebase, pop)"),
            new("Ctrl+Shift+U", "Git Push"),
            new("Ctrl+Shift+S", "Git panel - Stash tab (center panel)"),
            new("Ctrl+Shift+G", "Reflog"),
            new("Ctrl+Alt+B", "Git panel - Comparison tab (center panel)"),
            new("Ctrl+Shift+H", "GitHub Dashboard"),
            new("Ctrl+Shift+R", "PR Review (center panel)"),
            new("Ctrl+Enter", "Commit staged changes (in Git Changes panel)"),
        ]),

        new("Build & Test",
        [
            new("Ctrl+Shift+B", "Build"),
            new("F6", "Run tests (center panel)"),
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
    /// Gets shortcut sections filtered for the specified platform.
    /// </summary>
    /// <param name="isMacOS">True for macOS, false for Windows.</param>
    /// <returns>Sections with only platform-applicable shortcuts.</returns>
    public static List<ShortcutSection> GetSectionsForPlatform(bool isMacOS)
    {
        return BuiltInShortcutSections
            .Select(section => new ShortcutSection(section.Name, section.GetItemsForPlatform(isMacOS)))
            .Where(section => section.Items.Count > 0)
            .ToList();
    }

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
    /// Normalize a shortcut string for comparison.
    /// Handles variations like "Ctrl+Shift+C" vs "CTRL+SHIFT+C" and Cmd vs Ctrl.
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

            // Normalize modifier names (Cmd/Command maps to Ctrl for cross-platform comparison)
            if (lower == "ctrl" || lower == "control" || lower == "cmd" || lower == "command")
                normalized.Add("Ctrl");
            else if (lower == "shift")
                normalized.Add("Shift");
            else if (lower == "alt" || lower == "opt" || lower == "option")
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
        string? shortcut,
        IEnumerable<QuickCommand>? quickCommands = null,
        IEnumerable<Profile>? profiles = null,
        string? excludeQuickCommandId = null,
        string? excludeProfileName = null)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        var normalized = NormalizeShortcut(shortcut);

        // Check built-in shortcuts
        if (BuiltInShortcuts.TryGetValue(normalized, out var builtInDescription))
        {
            return $"Conflicts with built-in shortcut: {builtInDescription}";
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
                    if (string.Equals(cmdNormalized, normalized, StringComparison.OrdinalIgnoreCase))
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
                    if (string.Equals(profileNormalized, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Conflicts with Profile: {profile.Name}";
                    }
                }
            }
        }

        return null;
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
