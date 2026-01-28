using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for reading Claude Code session index files from ~/.claude/projects/.
/// Provides rich metadata about sessions including summaries, branches, and timestamps.
/// </summary>
public interface IClaudeSessionIndexService
{
    /// <summary>
    /// Gets all session entries for a specific project path.
    /// </summary>
    /// <param name="projectPath">The project path to look up.</param>
    /// <returns>List of session entries for the project, ordered by modified date descending.</returns>
    IReadOnlyList<ClaudeSessionIndexEntry> GetSessionsForProject(string projectPath);

    /// <summary>
    /// Gets a specific session entry by its session ID.
    /// </summary>
    /// <param name="sessionId">The session ID (GUID) to look up.</param>
    /// <returns>The session entry if found, null otherwise.</returns>
    ClaudeSessionIndexEntry? GetSessionById(string sessionId);

    /// <summary>
    /// Gets the project path for a given session ID.
    /// Useful for mapping tasks to workspaces.
    /// </summary>
    /// <param name="sessionId">The session ID to look up.</param>
    /// <returns>The project path if found, null otherwise.</returns>
    string? GetProjectPathForSession(string sessionId);

    /// <summary>
    /// Gets all known session entries across all projects.
    /// </summary>
    /// <returns>All session entries, ordered by modified date descending.</returns>
    IReadOnlyList<ClaudeSessionIndexEntry> GetAllSessions();

    /// <summary>
    /// Gets all unique project paths that have Claude sessions.
    /// </summary>
    /// <returns>List of project paths.</returns>
    IReadOnlyList<string> GetAllProjectPaths();

    /// <summary>
    /// Refreshes the session index cache from disk.
    /// </summary>
    void Refresh();

    /// <summary>
    /// Event fired when session index data changes.
    /// </summary>
    event EventHandler? SessionsChanged;
}
