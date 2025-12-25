namespace TerminalHost.Domain;

/// <summary>
/// Represents a keyboard shortcut with its description.
/// </summary>
public record ShortcutItem(string Shortcut, string Description);

/// <summary>
/// Represents a group of related shortcuts.
/// </summary>
public record ShortcutSection(string Name, List<ShortcutItem> Items);

/// <summary>
/// Represents a command line usage example.
/// </summary>
public record CommandLineExample(string Command, string Description);

/// <summary>
/// Represents an important path entry.
/// </summary>
public record ImportantPath(string Label, string Path);
