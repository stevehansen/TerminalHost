using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for detecting and managing Claude Code custom slash commands from .md files.
/// </summary>
public interface IClaudeCommandService
{
    /// <summary>
    /// Gets global commands from ~/.claude/commands/
    /// </summary>
    IReadOnlyList<ClaudeCommand> GlobalCommands { get; }

    /// <summary>
    /// Gets project-specific commands from .claude/commands/ in the specified directory.
    /// </summary>
    IReadOnlyList<ClaudeCommand> GetProjectCommands(string workingDirectory);

    /// <summary>
    /// Gets all commands (global + project) for a working directory.
    /// Project commands override global commands with the same name.
    /// </summary>
    IReadOnlyList<ClaudeCommand> GetAllCommands(string? workingDirectory);

    /// <summary>
    /// Refreshes the global commands list by rescanning ~/.claude/commands/
    /// </summary>
    void RefreshGlobalCommands();

    /// <summary>
    /// Refreshes project commands for a specific working directory.
    /// </summary>
    void RefreshProjectCommands(string workingDirectory);

    /// <summary>
    /// Raised when commands change (files added, removed, or modified).
    /// </summary>
    event EventHandler? CommandsChanged;
}
