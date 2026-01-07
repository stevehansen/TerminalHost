namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a Claude Code custom slash command from a .md file.
/// </summary>
public class ClaudeCommand
{
    /// <summary>
    /// Unique identifier for the command (source + name).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Command name (filename without .md extension).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full path to the .md file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Description from frontmatter or first line of the file.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// User-assigned keyboard shortcut (e.g., "Ctrl+Alt+R").
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// Whether this is a global, project-specific, or plugin command.
    /// </summary>
    public ClaudeCommandSource Source { get; init; }

    /// <summary>
    /// For plugin commands, the name of the plugin (e.g., "dcg").
    /// Null for global and project commands.
    /// </summary>
    public string? PluginName { get; init; }

    /// <summary>
    /// Gets the full command name including plugin prefix if applicable.
    /// E.g., "create_for_issue" becomes "dcg:create_for_issue" for plugin commands.
    /// </summary>
    public string FullName => PluginName != null ? $"{PluginName}:{Name}" : Name;
}

/// <summary>
/// Indicates where a Claude command was loaded from.
/// </summary>
public enum ClaudeCommandSource
{
    /// <summary>
    /// Global command from ~/.claude/commands/
    /// </summary>
    Global,

    /// <summary>
    /// Project-specific command from .claude/commands/
    /// </summary>
    Project,

    /// <summary>
    /// Plugin command from ~/.claude/plugins/
    /// </summary>
    Plugin
}
