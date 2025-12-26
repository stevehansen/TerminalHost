// ShortcutItem and ShortcutSection are now in TerminalHost.Core.Domain.ShortcutTypes
// Re-export them for backward compatibility
global using TerminalHost.Core.Domain;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a command line usage example.
/// </summary>
public record CommandLineExample(string Command, string Description);

/// <summary>
/// Represents an important path entry.
/// </summary>
public record ImportantPath(string Label, string Path);
