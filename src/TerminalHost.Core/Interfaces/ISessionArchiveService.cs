using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Persists session summaries to disk so they survive app restart.
/// Primarily used for devcontainer sessions that cannot be rediscovered
/// from the host-side Claude session index.
/// </summary>
public interface ISessionArchiveService
{
    /// <summary>
    /// Archive a completed session's summary to disk.
    /// </summary>
    void ArchiveSession(SessionActivityState state);

    /// <summary>
    /// Get archived sessions, optionally filtered by max age.
    /// </summary>
    IReadOnlyList<SessionArchiveEntry> GetArchivedSessions(TimeSpan? maxAge = null);

    /// <summary>
    /// Delete archive entries older than the specified age.
    /// </summary>
    void CleanupOldEntries(TimeSpan maxAge);
}
