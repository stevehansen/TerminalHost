using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// In-memory tracker of running Claude Code sessions. Holds the inactivity clock,
/// transcript-watcher subscription, and routes hook events into live session
/// state. Defers all persistent state to <see cref="ISessionStateStore"/>.
/// </summary>
public sealed class LiveSessionTracker : ILiveSessionTracker, IDisposable
{
    private readonly ISessionStateStore _stateStore;
    private readonly IClaudeSessionIndexService? _sessionIndexService;
    private readonly ITranscriptWatcher? _transcriptWatcher;
    private readonly ISessionActivityService? _activityService;
    private readonly ICollabService? _collabService;
    private readonly object _lock = new();

    private const int InactivityCheckIntervalMs = 30_000;
    private const int InactivityTimeoutMinutes = 2;
    private const int NoActivityTimeoutMinutes = 5;
    private const int CompletedSessionRetentionSeconds = 60;

    private Timer? _inactivityTimer;
    private readonly Dictionary<string, LiveSession> _liveSessions = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? LiveSessionsChanged;

    public LiveSessionTracker(
        ISessionStateStore stateStore,
        IClaudeSessionIndexService? sessionIndexService = null,
        ITranscriptWatcher? transcriptWatcher = null,
        ISessionActivityService? activityService = null,
        ICollabService? collabService = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _sessionIndexService = sessionIndexService;
        _transcriptWatcher = transcriptWatcher;
        _activityService = activityService;
        _collabService = collabService;

        if (_transcriptWatcher != null)
        {
            _transcriptWatcher.OnEvent += OnTranscriptWatcherEvent;
            _transcriptWatcher.OnSessionInactive += OnTranscriptSessionInactive;
        }
    }

    public IReadOnlyList<LiveSession> GetLiveSessions()
    {
        lock (_lock) { return _liveSessions.Values.ToList(); }
    }

    public LiveSession? GetLiveSessionByClaudeId(string claudeSessionId)
    {
        if (string.IsNullOrEmpty(claudeSessionId)) return null;
        lock (_lock)
        {
            _liveSessions.TryGetValue(claudeSessionId, out var session);
            return session;
        }
    }

    public void HandleSessionStart(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId) || string.IsNullOrEmpty(hookEvent.Cwd))
            return;

        bool isDevContainer = hookEvent.Source == SessionSource.DevContainer;

        // Use Split for cross-platform safety (Linux paths on Windows host)
        var segments = hookEvent.Cwd.TrimEnd('/', '\\').Split('/', '\\');
        var dirName = segments.LastOrDefault(s => s.Length > 0) ?? hookEvent.Cwd;
        if (isDevContainer && !string.IsNullOrEmpty(hookEvent.ContainerName))
            dirName = $"{dirName} [{hookEvent.ContainerName}]";

        var intent = _stateStore.EnsureIntentForWorkingDirectory(hookEvent.Cwd, dirName);

        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId, out var existing))
            {
                if (!existing.IsActive)
                    existing.EndTime = null;
                return;
            }

            var live = new LiveSession
            {
                ClaudeSessionId = hookEvent.SessionId,
                WorkingDirectory = hookEvent.Cwd,
                ProjectPath = hookEvent.Cwd,
                TranscriptPath = hookEvent.TranscriptPath,
                IntentId = intent.Id,
                StartTime = hookEvent.Timestamp,
                Source = hookEvent.Source,
                ContainerName = hookEvent.ContainerName,
            };
            _liveSessions[hookEvent.SessionId] = live;
            intent.LastActiveAt = DateTime.UtcNow;
        }

        // Start watching the transcript file
        var transcriptPath = hookEvent.TranscriptPath;
        if (string.IsNullOrEmpty(transcriptPath) && !string.IsNullOrEmpty(hookEvent.SessionId))
            transcriptPath = FindTranscriptPath(hookEvent.SessionId);

        if (!string.IsNullOrEmpty(transcriptPath))
        {
            var watchPath = transcriptPath;
            if (hookEvent.Source == SessionSource.Container)
                watchPath = ResolveContainerTranscriptPath(transcriptPath, hookEvent.Cwd);

            if (!isDevContainer && !string.IsNullOrEmpty(watchPath))
                _transcriptWatcher?.Watch(hookEvent.SessionId, watchPath);

            _activityService?.GetOrCreateState(hookEvent.SessionId, transcriptPath: watchPath);
        }

        LiveSessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HandleFileChanged(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;
        EnsureLiveSession(hookEvent);
    }

    public Task HandleSessionStopAsync(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId))
            return Task.CompletedTask;

        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId, out var live))
            {
                live.EndTime = hookEvent.Timestamp;
                live.TranscriptPath ??= hookEvent.TranscriptPath;
                live.EndReason = "explicit";

                var activityState = _activityService?.GetState(hookEvent.SessionId);
                if (activityState != null)
                {
                    live.HadFileWrites = activityState.FileActivities.Values.Any(f => f.WriteCount > 0);
                    live.HadErrors = activityState.ToolCalls.Values.Any(t => t.State == ToolCallState.Error);
                }
            }
        }

        _transcriptWatcher?.Unwatch(hookEvent.SessionId);
        LiveSessionsChanged?.Invoke(this, EventArgs.Empty);

        // Schedule removal after retention (gives ClaudeSessionIndexService time to update)
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(CompletedSessionRetentionSeconds));
            lock (_lock)
            {
                if (_liveSessions.TryGetValue(hookEvent.SessionId, out var live) && !live.IsActive)
                    _liveSessions.Remove(hookEvent.SessionId);
            }
            LiveSessionsChanged?.Invoke(this, EventArgs.Empty);
        });

        return Task.CompletedTask;
    }

    public void HandleToolStart(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;
        EnsureLiveSession(hookEvent);

        // Eager-parse transcript: by the time a tool starts, the user message and
        // assistant thinking/text are already written. Avoids waiting for FSW debounce.
        _transcriptWatcher?.EagerParse(hookEvent.SessionId);
    }

    public void HandleToolEnd(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;
        EnsureLiveSession(hookEvent);
        TryFixCollabSessionName(hookEvent);
    }

    public void StartInactivityTimer()
    {
        _inactivityTimer?.Dispose();
        _inactivityTimer = new Timer(
            _ => CheckInactiveSessions(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(InactivityCheckIntervalMs));
    }

    public void StopInactivityTimer()
    {
        _inactivityTimer?.Dispose();
        _inactivityTimer = null;
    }

    internal void CheckInactiveSessions()
    {
        bool changed = false;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            foreach (var live in _liveSessions.Values)
            {
                if (!live.IsActive) continue;

                var activityState = _activityService?.GetState(live.ClaudeSessionId);
                var lastActivity = activityState?.LastActivityTime;
                var lastFileChange = _transcriptWatcher?.GetLastFileChangeTime(live.ClaudeSessionId);
                if (lastFileChange.HasValue && (!lastActivity.HasValue || lastFileChange.Value > lastActivity.Value))
                    lastActivity = lastFileChange.Value;

                bool timedOut = lastActivity.HasValue
                    ? (now - lastActivity.Value).TotalMinutes > InactivityTimeoutMinutes
                    : (now - live.StartTime).TotalMinutes > NoActivityTimeoutMinutes;

                if (timedOut)
                {
                    live.EndTime = lastActivity ?? now;
                    live.EndReason = "timeout";

                    if (activityState != null)
                    {
                        activityState.Lifecycle = SessionActivityService.DetermineEndStatus(activityState, "timeout");
                        activityState.EndTime = live.EndTime;
                    }

                    _transcriptWatcher?.Unwatch(live.ClaudeSessionId);
                    changed = true;
                }
            }
        }

        if (changed) LiveSessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ensures a live session exists for the given hook event. Handles the case
    /// where we missed the session_start hook (e.g., session started before
    /// TerminalHost launched). Hydrates from <see cref="IClaudeSessionIndexService"/>
    /// when available so start time and metadata aren't lost.
    /// </summary>
    private void EnsureLiveSession(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;

        lock (_lock)
        {
            if (_liveSessions.ContainsKey(hookEvent.SessionId)) return;
        }

        var indexEntry = _sessionIndexService?.GetSessionById(hookEvent.SessionId);
        if (indexEntry != null)
        {
            var enriched = new HookEvent
            {
                SessionId = hookEvent.SessionId,
                Cwd = !string.IsNullOrEmpty(hookEvent.Cwd) ? hookEvent.Cwd : indexEntry.ProjectPath ?? "",
                TranscriptPath = hookEvent.TranscriptPath ?? indexEntry.FullPath,
                Timestamp = indexEntry.Created ?? hookEvent.Timestamp,
                EventType = HookEventType.SessionStart,
                ToolName = hookEvent.ToolName,
                Source = hookEvent.Source,
                ContainerName = hookEvent.ContainerName,
            };
            HandleSessionStart(enriched);
        }
        else
        {
            HandleSessionStart(hookEvent);
        }
    }

    /// <summary>
    /// When a collab tool completes, correlate the Claude session id with the collab
    /// subscriber name. If the subscriber used an auto-generated name, rename it to
    /// the project folder name from the Claude session's cwd.
    /// </summary>
    private void TryFixCollabSessionName(HookEvent hookEvent)
    {
        if (_collabService == null) return;
        var toolName = (hookEvent.ToolName ?? "").ToLowerInvariant();
        if (!toolName.Contains("collab__subscribe") &&
            !toolName.Contains("collab__send_message") &&
            !toolName.Contains("collab__read_messages"))
            return;

        string? projectName = null;
        string? workingDir = null;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId!, out var live))
            {
                projectName = live.DisplayName;
                workingDir = live.WorkingDirectory;
            }
        }
        if (string.IsNullOrEmpty(projectName) || projectName == "Unknown") return;

        string? topic = null;
        if (hookEvent.RawData?.ToolInput is { } input && input.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            topic = input.TryGetProperty("topic", out var t) ? t.GetString()
                  : input.TryGetProperty("name", out var n) ? n.GetString()
                  : null;
        }
        if (string.IsNullOrEmpty(topic)) return;

        var collabSessions = _collabService.GetSessions();
        var allTopics = _collabService.GetTopics();
        var collabTopic = allTopics.FirstOrDefault(ct => ct.Name == topic);
        if (collabTopic == null) return;

        var collabSession = collabSessions.FirstOrDefault(s => s.ClaudeSessionId == hookEvent.SessionId)
            ?? collabSessions.FirstOrDefault(s =>
                collabTopic.Subscribers.Contains(s.Name) &&
                (s.Name.StartsWith("session-", StringComparison.OrdinalIgnoreCase) || s.Name == "unknown"))
            ?? collabSessions.FirstOrDefault(s =>
                collabTopic.Subscribers.Contains(s.Name) &&
                string.IsNullOrEmpty(s.ClaudeSessionId));

        if (collabSession == null) return;

        collabSession.ClaudeSessionId = hookEvent.SessionId;
        collabSession.WorkingDir ??= workingDir;
        collabSession.ProjectName ??= projectName;

        var oldName = collabSession.Name;
        if (oldName.StartsWith("session-", StringComparison.OrdinalIgnoreCase) || oldName == "unknown")
        {
            if (!collabTopic.Subscribers.Contains(projectName))
            {
                collabTopic.Subscribers.Remove(oldName);
                collabTopic.Subscribers.Add(projectName);
                if (collabTopic.CreatedBy == oldName) collabTopic.CreatedBy = projectName;
                collabSession.Name = projectName;
            }
        }
    }

    private void OnTranscriptWatcherEvent(object? sender, TranscriptWatcherEventArgs e)
    {
        _activityService?.ProcessTranscriptEvents(e.SessionId, e.Events, e.Summary, e.Model);
    }

    private void OnTranscriptSessionInactive(object? sender, string sessionId)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(sessionId, out var live) && live.IsActive)
            {
                var activityState = _activityService?.GetState(sessionId);
                live.EndTime = activityState?.LastActivityTime ?? DateTime.UtcNow;
                live.EndReason = "timeout";

                if (activityState != null)
                {
                    activityState.Lifecycle = SessionActivityService.DetermineEndStatus(activityState, "timeout");
                    activityState.EndTime = live.EndTime;
                }

                changed = true;
            }
        }

        _transcriptWatcher?.Unwatch(sessionId);
        if (changed) LiveSessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resolves a container-translated transcript path to the host path. The proxy
    /// translates /home/developer → host profile, but Docker overlay mounts map
    /// the container project key to the host project key. We reconstruct the path
    /// using the host CWD.
    /// </summary>
    private static string ResolveContainerTranscriptPath(string translatedPath, string hostCwd)
    {
        var normalized = translatedPath.Replace('\\', '/');
        const string marker = ".claude/projects/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return translatedPath;

        var afterMarker = normalized[(idx + marker.Length)..];
        var slashIdx = afterMarker.IndexOf('/');
        if (slashIdx < 0) return translatedPath;

        var relativePath = afterMarker[(slashIdx + 1)..];
        var hostProjectKey = ContainerService.EncodeClaudeProjectPath(hostCwd);
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(claudeDir, "projects", hostProjectKey, relativePath);
    }

    private static string? FindTranscriptPath(string sessionId)
    {
        try
        {
            var projectsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            if (!Directory.Exists(projectsRoot)) return null;

            var targetFile = sessionId + ".jsonl";
            foreach (var projectDir in Directory.GetDirectories(projectsRoot))
            {
                var candidatePath = Path.Combine(projectDir, targetFile);
                if (File.Exists(candidatePath)) return candidatePath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FindTranscriptPath: {ex.Message}");
        }
        return null;
    }

    public void Dispose()
    {
        StopInactivityTimer();
        if (_transcriptWatcher != null)
        {
            _transcriptWatcher.OnEvent -= OnTranscriptWatcherEvent;
            _transcriptWatcher.OnSessionInactive -= OnTranscriptSessionInactive;
            _transcriptWatcher.UnwatchAll();
        }
    }
}
