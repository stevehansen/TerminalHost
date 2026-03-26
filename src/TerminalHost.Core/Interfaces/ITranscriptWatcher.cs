using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Watches active session JSONL transcript files for changes.
/// Incrementally parses new content and emits ActivityEvents.
/// Detects session inactivity when no file changes occur within a timeout.
/// </summary>
public interface ITranscriptWatcher : IDisposable
{
    /// <summary>
    /// Start watching a transcript file for a session.
    /// If the file already exists, performs an initial catch-up parse.
    /// </summary>
    void Watch(string sessionId, string transcriptPath);

    /// <summary>
    /// Stop watching a session's transcript file and clean up resources.
    /// </summary>
    void Unwatch(string sessionId);

    /// <summary>
    /// Stop watching all sessions and clean up all resources.
    /// </summary>
    void UnwatchAll();

    /// <summary>
    /// Whether a session is currently being watched.
    /// </summary>
    bool IsWatching(string sessionId);

    /// <summary>
    /// Gets the last time the transcript file was modified (from FileSystemWatcher).
    /// Used by TimelineService.CheckInactiveSessions to cross-reference with hook activity.
    /// Returns null if not watching or no changes detected.
    /// </summary>
    DateTime? GetLastFileChangeTime(string sessionId);

    /// <summary>
    /// Fired when new ActivityEvents are parsed from a transcript file change.
    /// </summary>
    event EventHandler<TranscriptWatcherEventArgs>? OnEvent;

    /// <summary>
    /// Fired when a watched session has no file changes for the inactivity timeout.
    /// </summary>
    event EventHandler<string>? OnSessionInactive;
}

/// <summary>
/// Event args for transcript watcher events, carrying a batch of parsed events.
/// </summary>
public class TranscriptWatcherEventArgs : EventArgs
{
    public string SessionId { get; init; } = "";
    public IReadOnlyList<ActivityEvent> Events { get; init; } = [];
    public string? Summary { get; init; }
    public string? Model { get; init; }
}
