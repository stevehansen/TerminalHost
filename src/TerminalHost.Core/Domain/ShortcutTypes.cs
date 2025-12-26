namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a keyboard shortcut with its description.
/// </summary>
public record ShortcutItem(string Shortcut, string Description);

/// <summary>
/// Represents a group of related shortcuts.
/// </summary>
public record ShortcutSection(string Name, List<ShortcutItem> Items);
