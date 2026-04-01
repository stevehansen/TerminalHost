using System.Text;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Watches active session JSONL transcript files via FileSystemWatcher.
/// Incrementally parses new content (tracking byte offset) and emits ActivityEvents.
/// Detects session inactivity when no file changes occur within a configurable timeout.
/// </summary>
public sealed class TranscriptWatcher : ITranscriptWatcher
{
    private readonly object _lock = new();
    private readonly Dictionary<string, WatchedSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TranscriptParserService _parser = new();
    private readonly TimeSpan _inactivityTimeout;
    private readonly TimeSpan _debounceDelay;
    private bool _disposed;

    public event EventHandler<TranscriptWatcherEventArgs>? OnEvent;
    public event EventHandler<string>? OnSessionInactive;

    public TranscriptWatcher(
        TimeSpan? inactivityTimeout = null,
        TimeSpan? debounceDelay = null)
    {
        _inactivityTimeout = inactivityTimeout ?? TimeSpan.FromMinutes(2);
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(100);
    }

    public void Watch(string sessionId, string transcriptPath)
    {
        if (_disposed) return;
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(transcriptPath)) return;

        transcriptPath = ExpandPath(transcriptPath);
        var directory = Path.GetDirectoryName(transcriptPath);
        var fileName = Path.GetFileName(transcriptPath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            return;

        lock (_lock)
        {
            if (_sessions.ContainsKey(sessionId))
                return; // Already watching

            var session = new WatchedSession
            {
                SessionId = sessionId,
                TranscriptPath = transcriptPath,
                SeenMessageIds = [],
                SeenToolUseIds = [],
                ByteOffset = 0,
                LastFileChangeTime = null,
            };

            // Create FileSystemWatcher on the directory, filtering to the specific file
            try
            {
                var watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                watcher.Changed += (_, _) => OnFileChanged(sessionId);
                watcher.Created += (_, _) => OnFileChanged(sessionId);
                session.FileWatcher = watcher;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptWatcher: Failed to create FileSystemWatcher for {transcriptPath}: {ex.Message}");
                // Continue without watcher — inactivity timer still works
            }

            // Inactivity timer fires once after timeout, resets on each file change
            session.InactivityTimer = new Timer(
                _ => FireSessionInactive(sessionId),
                null,
                _inactivityTimeout,
                Timeout.InfiniteTimeSpan); // One-shot, reset on activity

            _sessions[sessionId] = session;
        }

        // Do initial catch-up parse if file already exists
        if (File.Exists(transcriptPath))
        {
            _ = Task.Run(() => ParseIncrementalAsync(sessionId));
        }
    }

    public void Unwatch(string sessionId)
    {
        WatchedSession? session;
        lock (_lock)
        {
            if (!_sessions.Remove(sessionId, out session))
                return;
        }
        DisposeSession(session);
    }

    public void UnwatchAll()
    {
        List<WatchedSession> sessions;
        lock (_lock)
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }
        foreach (var session in sessions)
        {
            DisposeSession(session);
        }
    }

    public bool IsWatching(string sessionId)
    {
        lock (_lock)
        {
            return _sessions.ContainsKey(sessionId);
        }
    }

    /// <summary>
    /// Triggers an immediate parse of the transcript file, bypassing the debounce delay.
    /// Call this when a hook event arrives to pick up messages written before the tool call.
    /// </summary>
    public void EagerParse(string sessionId)
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (!_sessions.ContainsKey(sessionId)) return;
        }
        _ = Task.Run(() => ParseIncrementalAsync(sessionId));
    }

    public DateTime? GetLastFileChangeTime(string sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session.LastFileChangeTime : null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwatchAll();
    }

    private void OnFileChanged(string sessionId)
    {
        if (_disposed) return;

        bool shouldParseNow = false;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            session.LastFileChangeTime = DateTime.UtcNow;

            // Reset inactivity timer
            try { session.InactivityTimer?.Change(_inactivityTimeout, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }

            // Throttle (not debounce): fire immediately if cooldown has elapsed,
            // otherwise schedule a trailing parse after the cooldown.
            // This ensures messages appear within ~throttle ms of being written,
            // even during continuous file writes.
            var now = DateTime.UtcNow;
            if (!session.LastParseTime.HasValue ||
                (now - session.LastParseTime.Value) >= _debounceDelay)
            {
                // Cooldown elapsed — parse immediately
                session.LastParseTime = now;
                shouldParseNow = true;
            }
            else
            {
                // Still in cooldown — schedule trailing parse (if not already scheduled)
                if (!session.TrailingParseScheduled)
                {
                    session.TrailingParseScheduled = true;
                    var remaining = _debounceDelay - (now - session.LastParseTime.Value);
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay(remaining); }
                        catch { return; }
                        lock (_lock)
                        {
                            if (_sessions.TryGetValue(sessionId, out var s))
                            {
                                s.TrailingParseScheduled = false;
                                s.LastParseTime = DateTime.UtcNow;
                            }
                        }
                        await ParseIncrementalAsync(sessionId);
                    });
                }
            }
        }

        if (shouldParseNow)
        {
            _ = Task.Run(() => ParseIncrementalAsync(sessionId));
        }
    }

    private async Task ParseIncrementalAsync(string sessionId)
    {
        SemaphoreSlim parseLock;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var s))
                return;
            parseLock = s.ParseLock;
        }

        // Serialize parses per session to prevent the initial catch-up parse and
        // debounced file-change parses from racing on the same byte offset
        await parseLock.WaitAsync();
        try
        {
            await ParseIncrementalCoreAsync(sessionId);
        }
        finally
        {
            parseLock.Release();
        }
    }

    private async Task ParseIncrementalCoreAsync(string sessionId)
    {
        string transcriptPath;
        long byteOffset;
        HashSet<string> seenMessageIds;
        HashSet<string> seenToolUseIds;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;
            transcriptPath = session.TranscriptPath;
            byteOffset = session.ByteOffset;
            seenMessageIds = session.SeenMessageIds;
            seenToolUseIds = session.SeenToolUseIds;
        }

        // Read new lines from the byte offset
        List<string> newLines;
        long newOffset;
        try
        {
            (newLines, newOffset) = await ReadNewLinesAsync(transcriptPath, byteOffset);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TranscriptWatcher: Read error for {sessionId}: {ex.Message}");
            return;
        }

        if (newLines.Count == 0)
        {
            // Update offset even if no complete lines (file may have grown with partial content)
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var s))
                    s.ByteOffset = newOffset;
            }
            return;
        }

        // Parse the new lines
        var result = _parser.ParseLines(newLines, sessionId, seenMessageIds, seenToolUseIds);

        // Update offset
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var s))
                s.ByteOffset = newOffset;
        }

        if (!result.ParsedSuccessfully || result.Events.Count == 0)
            return;

        // Emit events
        OnEvent?.Invoke(this, new TranscriptWatcherEventArgs
        {
            SessionId = sessionId,
            Events = result.Events,
            Summary = result.Summary,
            Model = result.Model,
        });
    }

    /// <summary>
    /// Reads new complete lines from a file starting at the given byte offset.
    /// Returns the lines and the new byte offset (positioned at the start of any incomplete trailing line).
    /// Opens with FileShare.ReadWrite to avoid locking conflicts with Claude Code.
    /// </summary>
    private static async Task<(List<string> Lines, long NewOffset)> ReadNewLinesAsync(string path, long offset)
    {
        var lines = new List<string>();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (offset >= stream.Length)
            return (lines, offset); // No new content

        stream.Seek(offset, SeekOrigin.Begin);

        // Read remaining bytes
        var remainingBytes = (int)(stream.Length - offset);
        var buffer = new byte[remainingBytes];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, remainingBytes));

        if (bytesRead == 0)
            return (lines, offset);

        var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        // Split into lines, keeping track of whether the last chunk is complete
        var lineSpans = text.Split('\n');

        // All elements except the last are complete lines (they ended with \n)
        for (int i = 0; i < lineSpans.Length - 1; i++)
        {
            var line = lineSpans[i].TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        // Calculate new offset: advance past all complete lines
        var lastElement = lineSpans[^1];
        long newOffset;
        if (string.IsNullOrEmpty(lastElement))
        {
            // Text ended with \n — all lines are complete
            newOffset = offset + bytesRead;
        }
        else
        {
            // Last element is a partial line — don't consume it
            var partialBytes = Encoding.UTF8.GetByteCount(lastElement);
            newOffset = offset + bytesRead - partialBytes;
        }

        return (lines, newOffset);
    }

    private void FireSessionInactive(string sessionId)
    {
        if (_disposed) return;

        // Verify still watching and still inactive
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            // Double-check: if file was recently modified, don't fire
            if (session.LastFileChangeTime.HasValue &&
                (DateTime.UtcNow - session.LastFileChangeTime.Value) < _inactivityTimeout)
            {
                // Reset timer for the remaining time
                var remaining = _inactivityTimeout - (DateTime.UtcNow - session.LastFileChangeTime.Value);
                try { session.InactivityTimer?.Change(remaining, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
                return;
            }
        }

        OnSessionInactive?.Invoke(this, sessionId);
    }

    private static void DisposeSession(WatchedSession session)
    {
        try { session.FileWatcher?.Dispose(); } catch { }
        try { session.InactivityTimer?.Dispose(); } catch { }
        try { session.DebounceCts?.Cancel(); } catch { }
        try { session.DebounceCts?.Dispose(); } catch { }
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }
        return path;
    }

    private sealed class WatchedSession
    {
        public required string SessionId { get; init; }
        public required string TranscriptPath { get; init; }
        public FileSystemWatcher? FileWatcher { get; set; }
        public long ByteOffset { get; set; }
        public DateTime? LastFileChangeTime { get; set; }
        public required HashSet<string> SeenMessageIds { get; init; }
        public required HashSet<string> SeenToolUseIds { get; init; }
        public Timer? InactivityTimer { get; set; }
        public CancellationTokenSource? DebounceCts { get; set; }
        public SemaphoreSlim ParseLock { get; } = new(1, 1);
        /// <summary>Last time we ran a parse (for throttle, not debounce).</summary>
        public DateTime? LastParseTime { get; set; }
        /// <summary>Whether a trailing parse is already scheduled during cooldown.</summary>
        public bool TrailingParseScheduled { get; set; }
    }
}
