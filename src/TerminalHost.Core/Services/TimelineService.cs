using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for managing Timeline IDE state, intents, and live Claude Code sessions.
/// Historical session data comes from IClaudeSessionIndexService — this service
/// only tracks in-memory live sessions from hook events. No file persistence for sessions.
/// </summary>
public sealed class TimelineService : ITimelineService, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitProcessRunner _gitRunner;
    private readonly IFileSystem _fileSystem;
    private readonly object _lock = new();

    // Inactivity timeout for detecting stuck sessions
    private Timer? _inactivityTimer;
    private const int InactivityCheckIntervalMs = 30_000;
    private const int InactivityTimeoutMinutes = 2;
    private const int NoActivityTimeoutMinutes = 5;
    private const int CompletedSessionRetentionSeconds = 60;

    // Cached state (loaded from config) — intents, focus time, preferences
    private TimelineState _state = new();

    // In-memory live sessions only — no file persistence
    private readonly Dictionary<string, LiveSession> _liveSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IClaudeSessionIndexService? _sessionIndexService;
    private readonly ITranscriptWatcher? _transcriptWatcher;
    private readonly ISessionActivityService? _activityService;
    private readonly ICollabService? _collabService;
    private readonly IHookInstaller? _hookInstaller;

    public TimelineService(
        IConfigurationService configService,
        IGitWorktreeService worktreeService,
        IGitProcessRunner gitRunner,
        IFileSystem fileSystem,
        IClaudeTaskFileService? taskFileService = null,
        IClaudeSessionIndexService? sessionIndexService = null,
        ITranscriptWatcher? transcriptWatcher = null,
        ISessionActivityService? activityService = null,
        ICollabService? collabService = null,
        IHookInstaller? hookInstaller = null,
        string? userDataDir = null)
    {
        _configService = configService;
        _worktreeService = worktreeService;
        _gitRunner = gitRunner;
        _fileSystem = fileSystem;
        _sessionIndexService = sessionIndexService;
        _transcriptWatcher = transcriptWatcher;
        _activityService = activityService;
        _collabService = collabService;
        _hookInstaller = hookInstaller;

        // Subscribe to transcript watcher events
        if (_transcriptWatcher != null)
        {
            _transcriptWatcher.OnEvent += OnTranscriptWatcherEvent;
            _transcriptWatcher.OnSessionInactive += OnTranscriptSessionInactive;
        }

        LoadFromConfig();
    }

    #region Events

    public event EventHandler<bool>? EnabledChanged;
    public event EventHandler? IntentsChanged;
    public event EventHandler<Intent?>? CurrentIntentChanged;
    public event EventHandler<bool>? FocusStateChanged;
    public event EventHandler<(string WorktreePath, string? InitialPrompt)>? OpenProjectRequested;
    public event EventHandler? LiveSessionsChanged;

    private void OnEnabledChanged(bool enabled) => EnabledChanged?.Invoke(this, enabled);
    private void OnIntentsChanged() => IntentsChanged?.Invoke(this, EventArgs.Empty);
    private void OnCurrentIntentChanged(Intent? intent) => CurrentIntentChanged?.Invoke(this, intent);
    private void OnFocusStateChanged(bool isFocusing) => FocusStateChanged?.Invoke(this, isFocusing);
    private void OnLiveSessionsChanged() => LiveSessionsChanged?.Invoke(this, EventArgs.Empty);

    #endregion

    #region Data Loading/Saving

    private void LoadFromConfig()
    {
        var config = _configService.Load();
        _state = config.TimelineState ?? new TimelineState();
    }

    private void SaveToConfig()
    {
        var config = _configService.Load();
        config.TimelineState = _state;
        _configService.Save(config);
    }

    public Task SaveAsync()
    {
        lock (_lock) { SaveToConfig(); }
        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        lock (_lock) { LoadFromConfig(); }
        return Task.CompletedTask;
    }

    #endregion

    #region Timeline State

    public bool IsEnabled
    {
        get { lock (_lock) { return _state.Enabled; } }
    }

    public void Enable()
    {
        lock (_lock)
        {
            if (_state.Enabled) return;
            _state.Enabled = true;
            SaveToConfig();
        }
        OnEnabledChanged(true);
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (!_state.Enabled) return;
            _state.PauseFocus();
            _state.Enabled = false;
            SaveToConfig();
        }
        OnEnabledChanged(false);
    }

    public TimelineState GetState()
    {
        lock (_lock) { return _state; }
    }

    #endregion

    #region Intent Management

    public async Task<Intent?> CreateIntentAsync(
        string name, string branchName, string mainRepoPath,
        string? baseBranch = null, string? context = null)
    {
        var parentDir = Path.GetDirectoryName(mainRepoPath);
        if (string.IsNullOrEmpty(parentDir)) return null;

        var repoName = Path.GetFileName(mainRepoPath);
        var safeBranchName = branchName.Replace("/", "-").Replace("\\", "-");
        var worktreePath = Path.Combine(parentDir, $"{repoName}-{safeBranchName}");

        var result = await _worktreeService.CreateWorktreeAsync(
            mainRepoPath, branchName, worktreePath, createBranch: true);

        if (!result.Success) return null;

        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, branchName, worktreePath, mainRepoPath);
            if (!string.IsNullOrEmpty(context))
                intent.ContextContent = context;
            _state.AddIntent(intent);
            SaveToConfig();
        }
        OnIntentsChanged();
        return intent;
    }

    public async Task<Intent> CreateIntentFromExistingFolderAsync(
        string name, string existingFolderPath, string? context = null)
    {
        string branchName = "main";
        try
        {
            var output = await _gitRunner.RunGitCommandAsync(existingFolderPath, "rev-parse --abbrev-ref HEAD");
            if (!string.IsNullOrWhiteSpace(output))
                branchName = output.Trim();
        }
        catch { }

        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, branchName, existingFolderPath, existingFolderPath);
            if (!string.IsNullOrEmpty(context))
                intent.ContextContent = context;
            _state.AddIntent(intent);
            SaveToConfig();
        }
        OnIntentsChanged();
        return intent;
    }

    public Intent? GetIntent(string intentId)
    {
        lock (_lock) { return _state.GetIntent(intentId); }
    }

    public IReadOnlyList<Intent> GetAllIntents()
    {
        lock (_lock) { return _state.Intents.ToList(); }
    }

    public IReadOnlyList<Intent> GetOrderedIntents()
    {
        lock (_lock) { return _state.GetOrderedIntents().ToList(); }
    }

    public IReadOnlyList<Intent> GetActiveIntents()
    {
        lock (_lock)
        {
            return _state.Intents
                .Where(i => i.Status == IntentStatus.Active)
                .OrderByDescending(i => i.LastActiveAt ?? i.CreatedAt)
                .ToList();
        }
    }

    public void UpdateIntent(Intent intent)
    {
        lock (_lock)
        {
            var existing = _state.Intents.FirstOrDefault(i => i.Id == intent.Id);
            if (existing != null)
            {
                var index = _state.Intents.IndexOf(existing);
                _state.Intents[index] = intent;
                SaveToConfig();
            }
        }
        OnIntentsChanged();
        if (_state.CurrentIntentId == intent.Id)
            OnCurrentIntentChanged(intent);
    }

    public void UpdateIntentStatus(string intentId, IntentStatus status)
    {
        Intent? intent;
        lock (_lock)
        {
            intent = _state.GetIntent(intentId);
            if (intent == null) return;
            intent.Status = status;
            if (status == IntentStatus.Completed || status == IntentStatus.Abandoned)
                intent.CompletedAt = DateTime.UtcNow;
            SaveToConfig();
        }
        OnIntentsChanged();
        if (_state.CurrentIntentId == intentId)
            OnCurrentIntentChanged(intent);
    }

    public void SetIntentContext(string intentId, string? context)
    {
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent == null) return;
            intent.ContextContent = context;
            SaveToConfig();
        }
        OnIntentsChanged();
    }

    public async Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false)
    {
        Intent? intent;
        lock (_lock)
        {
            intent = _state.GetIntent(intentId);
            if (intent == null) return false;
        }

        if (removeWorktree && !string.IsNullOrEmpty(intent.WorktreePath))
        {
            try { await _worktreeService.RemoveWorktreeAsync(intent.WorktreePath, force: true); }
            catch { }
        }

        lock (_lock)
        {
            _state.RemoveIntent(intentId);
            SaveToConfig();
        }
        OnIntentsChanged();
        if (_state.CurrentIntentId == intentId)
            OnCurrentIntentChanged(null);
        return true;
    }

    public void ReorderIntent(string intentId, int newIndex)
    {
        lock (_lock)
        {
            var currentIndex = _state.IntentOrder.IndexOf(intentId);
            if (currentIndex < 0) return;
            _state.IntentOrder.RemoveAt(currentIndex);
            if (newIndex >= _state.IntentOrder.Count)
                _state.IntentOrder.Add(intentId);
            else
                _state.IntentOrder.Insert(Math.Max(0, newIndex), intentId);
            SaveToConfig();
        }
        OnIntentsChanged();
    }

    public Intent? GetCurrentIntent()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_state.CurrentIntentId)) return null;
            return _state.GetIntent(_state.CurrentIntentId);
        }
    }

    public void SetCurrentIntent(string? intentId)
    {
        Intent? intent = null;
        lock (_lock)
        {
            if (_state.CurrentIntentId == intentId) return;
            _state.CurrentIntentId = intentId;
            if (!string.IsNullOrEmpty(intentId))
            {
                intent = _state.GetIntent(intentId);
                intent?.Activate();
            }
            SaveToConfig();
        }
        OnCurrentIntentChanged(intent);
    }

    #endregion

    #region Live Sessions

    public IReadOnlyList<LiveSession> GetLiveSessions()
    {
        lock (_lock)
        {
            return _liveSessions.Values.ToList();
        }
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

    #endregion

    #region Focus Time

    public TimeSpan GetTotalFocusTime()
    {
        lock (_lock) { return _state.TotalFocusTime; }
    }

    public TimeSpan GetCurrentFocusTime()
    {
        lock (_lock) { return _state.CurrentFocusTime; }
    }

    public bool IsFocusing
    {
        get { lock (_lock) { return _state.IsFocusing; } }
    }

    public void StartFocusTimer()
    {
        lock (_lock)
        {
            if (_state.IsFocusing) return;
            _state.StartFocus();
            SaveToConfig();
        }
        OnFocusStateChanged(true);
    }

    public void PauseFocusTimer()
    {
        lock (_lock)
        {
            if (!_state.IsFocusing) return;
            _state.PauseFocus();
            SaveToConfig();
        }
        OnFocusStateChanged(false);
    }

    public void ResetFocusTime()
    {
        bool wasFocusing;
        lock (_lock)
        {
            wasFocusing = _state.IsFocusing;
            _state.ResetFocusTime();
            SaveToConfig();
        }
        if (wasFocusing) OnFocusStateChanged(false);
    }

    #endregion

    #region Hook Event Handling

    public Intent? FindIntentByWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return null;
        lock (_lock) { return FindIntentByWorkingDirectoryLocked(workingDirectory); }
    }

    private Intent? FindIntentByWorkingDirectoryLocked(string workingDirectory)
    {
        string normalizedCwd;
        try
        {
            normalizedCwd = Path.GetFullPath(workingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // Path might be a container-native Linux path on a Windows host
            normalizedCwd = workingDirectory.TrimEnd('/', '\\');
        }

        return _state.Intents.FirstOrDefault(intent =>
        {
            if (string.IsNullOrEmpty(intent.WorktreePath)) return false;
            string normalizedWorktree;
            try
            {
                normalizedWorktree = Path.GetFullPath(intent.WorktreePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                normalizedWorktree = intent.WorktreePath.TrimEnd('/', '\\');
            }
            return string.Equals(normalizedCwd, normalizedWorktree, StringComparison.OrdinalIgnoreCase);
        });
    }

    public void HandleSessionStart(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId) || string.IsNullOrEmpty(hookEvent.Cwd))
            return;

        bool isDevContainer = hookEvent.Source == SessionSource.DevContainer;

        // Find or auto-create intent
        var intent = FindIntentByWorkingDirectory(hookEvent.Cwd);
        if (intent == null)
        {
            // Use Split for cross-platform safety (Linux paths on Windows host)
            var segments = hookEvent.Cwd.TrimEnd('/', '\\').Split('/', '\\');
            var dirName = segments.LastOrDefault(s => s.Length > 0) ?? hookEvent.Cwd;
            if (isDevContainer && !string.IsNullOrEmpty(hookEvent.ContainerName))
                dirName = $"{dirName} [{hookEvent.ContainerName}]";

            lock (_lock)
            {
                intent = FindIntentByWorkingDirectoryLocked(hookEvent.Cwd);
                if (intent == null)
                {
                    intent = Intent.Create(dirName, "", hookEvent.Cwd, hookEvent.Cwd);
                    _state.Intents.Add(intent);
                    _state.IntentOrder.Add(intent.Id);
                    SaveToConfig();
                }
            }
            OnIntentsChanged();
        }

        lock (_lock)
        {
            // Check if we already track this session
            if (_liveSessions.TryGetValue(hookEvent.SessionId, out var existing))
            {
                // Reactivate if it was ended (e.g., by timeout)
                if (!existing.IsActive)
                {
                    existing.EndTime = null;
                    existing.LastActivityTime = DateTime.UtcNow;
                }
                return; // Already tracked
            }

            // Create new live session
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

            // Update intent
            intent.LastActiveAt = DateTime.UtcNow;
            SaveToConfig();
        }

        // Start watching the transcript file
        // For Container sessions, the proxy translates /home/developer → host profile but
        // Docker overlay mounts map container project key → host project key, so we resolve
        // the correct host path. DevContainer sessions can't be watched (no host access).
        var transcriptPath = hookEvent.TranscriptPath;

        // Fallback: derive transcript path from session ID if not provided by hook
        if (string.IsNullOrEmpty(transcriptPath) && !string.IsNullOrEmpty(hookEvent.SessionId))
        {
            transcriptPath = FindTranscriptPath(hookEvent.SessionId);
        }

        if (!string.IsNullOrEmpty(transcriptPath))
        {
            var watchPath = transcriptPath;
            if (hookEvent.Source == SessionSource.Container)
                watchPath = ResolveContainerTranscriptPath(transcriptPath, hookEvent.Cwd);

            if (!isDevContainer && !string.IsNullOrEmpty(watchPath))
                _transcriptWatcher?.Watch(hookEvent.SessionId, watchPath);

            // Update activity state with resolved path
            _activityService?.GetOrCreateState(hookEvent.SessionId, transcriptPath: watchPath);
        }

        OnLiveSessionsChanged();
    }

    public void HandleFileChanged(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;

        // Ensure live session exists (handles sessions that started before we did)
        EnsureLiveSession(hookEvent);

        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId, out var live) && live.IsActive)
            {
                live.LastActivityTime = DateTime.UtcNow;
            }
        }
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

                // Populate status hints from activity data
                var activityState = _activityService?.GetState(hookEvent.SessionId);
                if (activityState != null)
                {
                    live.HadFileWrites = activityState.FileActivities.Values.Any(f => f.WriteCount > 0);
                    live.HadErrors = activityState.ToolCalls.Values.Any(t => t.State == ToolCallState.Error);
                }
            }
        }

        // Stop watching the transcript file
        _transcriptWatcher?.Unwatch(hookEvent.SessionId);

        OnLiveSessionsChanged();

        // Schedule removal after retention period (gives ClaudeSessionIndexService time to update)
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(CompletedSessionRetentionSeconds));
            lock (_lock)
            {
                if (_liveSessions.TryGetValue(hookEvent.SessionId, out var live) && !live.IsActive)
                {
                    _liveSessions.Remove(hookEvent.SessionId);
                }
            }
            OnLiveSessionsChanged();
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Ensures a live session exists for the given hook event.
    /// Handles the case where we missed the session_start hook (e.g., session started
    /// before TerminalHost launched, or before hooks were installed).
    /// Hydrates from ClaudeSessionIndexService if available to preserve real start time and metadata.
    /// </summary>
    private void EnsureLiveSession(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;

        lock (_lock)
        {
            if (_liveSessions.ContainsKey(hookEvent.SessionId))
                return; // Already tracked
        }

        // Try to hydrate from the session index (has real start time, summary, etc.)
        var indexEntry = _sessionIndexService?.GetSessionById(hookEvent.SessionId);

        if (indexEntry != null)
        {
            // Build a hook event enriched with index data, then let HandleSessionStart do the work
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
            // No index data — fall back to current hook event (start time will be "now")
            HandleSessionStart(hookEvent);
        }
    }

    public void HandleToolStart(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;

        EnsureLiveSession(hookEvent);

        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId, out var live) && live.IsActive)
            {
                live.LastActivityTime = DateTime.UtcNow;
            }
        }

        // Eager-parse transcript: by the time a tool starts, the user message and
        // assistant thinking/text are already written to the JSONL. Parse immediately
        // so messages appear in Spark Canvas without waiting for the FSW debounce.
        _transcriptWatcher?.EagerParse(hookEvent.SessionId);
    }

    public void HandleToolEnd(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId)) return;

        EnsureLiveSession(hookEvent);

        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId, out var live) && live.IsActive)
            {
                live.LastActivityTime = DateTime.UtcNow;
            }
        }

        // If this was a collab tool call, try to fix auto-generated session names
        TryFixCollabSessionName(hookEvent);
    }

    /// <summary>
    /// When a collab tool (subscribe, send_message, read_messages) completes,
    /// correlate the Claude session ID with the collab subscriber name.
    /// If the subscriber used an auto-generated name like "session-3",
    /// rename it to the project folder name from the Claude session's cwd.
    /// </summary>
    private void TryFixCollabSessionName(HookEvent hookEvent)
    {
        if (_collabService == null) return;
        var toolName = (hookEvent.ToolName ?? "").ToLowerInvariant();
        if (!toolName.Contains("collab__subscribe") &&
            !toolName.Contains("collab__send_message") &&
            !toolName.Contains("collab__read_messages"))
            return;

        // Get the project name from the Claude session's working directory
        string? projectName = null;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId!, out var live))
            {
                projectName = live.DisplayName;
            }
        }
        if (string.IsNullOrEmpty(projectName) || projectName == "Unknown") return;

        // Extract topic name from tool input
        string? topic = null;
        if (hookEvent.RawData?.ToolInput is { } input && input.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            topic = input.TryGetProperty("topic", out var t) ? t.GetString()
                  : input.TryGetProperty("name", out var n) ? n.GetString()
                  : null;
        }
        if (string.IsNullOrEmpty(topic)) return;

        // Get full identity from live session
        string? workingDir = null;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(hookEvent.SessionId!, out var ls))
                workingDir = ls.WorkingDirectory;
        }

        var collabSessions = _collabService.GetSessions();
        var allTopics = _collabService.GetTopics();
        var collabTopic = allTopics.FirstOrDefault(ct => ct.Name == topic);
        if (collabTopic == null) return;

        // Find which collab session made this call.
        // Priority: 1) already tagged with this claudeSessionId
        //           2) subscriber with auto-generated name ("session-N" / "unknown")
        //           3) any subscriber not yet tagged with a claudeSessionId
        var collabSession = collabSessions.FirstOrDefault(s => s.ClaudeSessionId == hookEvent.SessionId)
            ?? collabSessions.FirstOrDefault(s =>
                collabTopic.Subscribers.Contains(s.Name) &&
                (s.Name.StartsWith("session-", StringComparison.OrdinalIgnoreCase) || s.Name == "unknown"))
            ?? collabSessions.FirstOrDefault(s =>
                collabTopic.Subscribers.Contains(s.Name) &&
                string.IsNullOrEmpty(s.ClaudeSessionId));

        if (collabSession == null) return;

        // Always enrich with identity fields from hooks
        collabSession.ClaudeSessionId = hookEvent.SessionId;
        collabSession.WorkingDir ??= workingDir;
        collabSession.ProjectName ??= projectName;

        // Rename auto-generated names to project name
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

    private void CheckInactiveSessions()
    {
        bool changed = false;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            foreach (var live in _liveSessions.Values)
            {
                if (!live.IsActive) continue;

                // Cross-reference with transcript file mtime before timing out
                var lastFileChange = _transcriptWatcher?.GetLastFileChangeTime(live.ClaudeSessionId);
                if (lastFileChange.HasValue)
                {
                    // Use the most recent activity signal from either hooks or file changes
                    if (!live.LastActivityTime.HasValue || lastFileChange.Value > live.LastActivityTime.Value)
                    {
                        live.LastActivityTime = lastFileChange.Value;
                    }
                }

                bool timedOut;
                if (live.LastActivityTime.HasValue)
                    timedOut = (now - live.LastActivityTime.Value).TotalMinutes > InactivityTimeoutMinutes;
                else
                    timedOut = (now - live.StartTime).TotalMinutes > NoActivityTimeoutMinutes;

                if (timedOut)
                {
                    live.EndTime = live.LastActivityTime ?? now;
                    live.EndReason = "timeout";

                    // Update activity state lifecycle
                    var activityState = _activityService?.GetState(live.ClaudeSessionId);
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

        if (changed) OnLiveSessionsChanged();
    }

    private void OnTranscriptWatcherEvent(object? sender, TranscriptWatcherEventArgs e)
    {
        // Forward transcript events to SessionActivityService
        _activityService?.ProcessTranscriptEvents(e.SessionId, e.Events, e.Summary, e.Model);

        // Update live session last activity time
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(e.SessionId, out var live) && live.IsActive)
            {
                live.LastActivityTime = DateTime.UtcNow;
            }
        }
    }

    private void OnTranscriptSessionInactive(object? sender, string sessionId)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(sessionId, out var live) && live.IsActive)
            {
                live.EndTime = live.LastActivityTime ?? DateTime.UtcNow;
                live.EndReason = "timeout";

                // Update activity state lifecycle
                var activityState = _activityService?.GetState(sessionId);
                if (activityState != null)
                {
                    activityState.Lifecycle = SessionActivityService.DetermineEndStatus(activityState, "timeout");
                    activityState.EndTime = live.EndTime;
                }

                changed = true;
            }
        }

        _transcriptWatcher?.Unwatch(sessionId);
        if (changed) OnLiveSessionsChanged();
    }

    /// <summary>
    /// Resolves a container-translated transcript path to the correct host path.
    /// The proxy translates /home/developer → host profile, but Docker overlay mounts
    /// map the container project key (e.g., -workspace-HC) to the host project key
    /// (e.g., P--HC). We reconstruct the path using the host CWD.
    /// </summary>
    private static string ResolveContainerTranscriptPath(string translatedPath, string hostCwd)
    {
        // Normalize separators for matching
        var normalized = translatedPath.Replace('\\', '/');

        // Find ".claude/projects/" in the path — everything after the project key segment
        // is the relative path we need to preserve (e.g., "abc123.jsonl")
        const string marker = ".claude/projects/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return translatedPath; // Can't parse — return as-is

        var afterMarker = normalized[(idx + marker.Length)..];
        // afterMarker = "{containerProjectKey}/abc123.jsonl" or "{containerProjectKey}/sessions/abc.jsonl"
        var slashIdx = afterMarker.IndexOf('/');
        if (slashIdx < 0)
            return translatedPath; // No file after project key

        var relativePath = afterMarker[(slashIdx + 1)..]; // e.g., "abc123.jsonl"

        // Compute host project key from the host working directory
        var hostProjectKey = ContainerService.EncodeClaudeProjectPath(hostCwd);

        // Build the correct host path: ~/.claude/projects/{hostProjectKey}/{relativePath}
        var claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(claudeDir, "projects", hostProjectKey, relativePath);
    }

    /// <summary>
    /// Attempts to find a transcript JSONL file for a session ID by scanning ~/.claude/projects/.
    /// Used as a fallback when the hook event doesn't include transcript_path.
    /// </summary>
    private static string? FindTranscriptPath(string sessionId)
    {
        try
        {
            var projectsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            if (!Directory.Exists(projectsRoot))
                return null;

            var targetFile = sessionId + ".jsonl";
            foreach (var projectDir in Directory.GetDirectories(projectsRoot))
            {
                var candidatePath = Path.Combine(projectDir, targetFile);
                if (File.Exists(candidatePath))
                    return candidatePath;
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

    #endregion

    #region Hook Installation (delegated to IHookInstaller)

    public bool AreHooksInstalled() => _hookInstaller?.AreHooksInstalled() ?? false;
    public bool InstallHooks() => _hookInstaller?.InstallHooks() ?? false;
    public bool UninstallHooks() => _hookInstaller?.UninstallHooks() ?? true;
    public void UpgradeHooksIfNeeded() => _hookInstaller?.UpgradeHooksIfNeeded();

    #endregion
}
