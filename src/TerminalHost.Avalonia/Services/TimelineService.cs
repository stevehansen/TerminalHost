using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Stub implementation of the Timeline Mode service.
/// STUB: Core types differ significantly from Avalonia types - major refactoring needed.
/// </summary>
public class TimelineService : ITimelineService
{
    private readonly IConfigurationService _configService;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitProcessRunner _gitRunner;
    private readonly object _lock = new();
    private TimelineState _state;

    public TimelineService(
        IConfigurationService configService,
        IGitWorktreeService worktreeService,
        IGitProcessRunner gitRunner)
    {
        _configService = configService;
        _worktreeService = worktreeService;
        _gitRunner = gitRunner;
        _state = LoadState();
    }

    // Events
    public event EventHandler<bool>? EnabledChanged;
    public event EventHandler? IntentsChanged;
    public event EventHandler<Intent?>? CurrentIntentChanged;
    public event EventHandler? SessionsChanged;
    public event EventHandler<ClaudeSession>? SessionStatusChanged;
    public event EventHandler<bool>? FocusStateChanged;
    public event EventHandler<TimeScale>? TimeScaleChanged;
    public event EventHandler<(string WorktreePath, string? InitialPrompt)>? OpenProjectRequested;
    public event EventHandler? OrphanSessionsChanged;

    // Timeline Mode state

    public bool IsEnabled
    {
        get { lock (_lock) return _state.Enabled; }
    }

    public void Enable()
    {
        lock (_lock)
        {
            if (_state.Enabled) return;
            _state.Enabled = true;
            SaveState();
        }
        EnabledChanged?.Invoke(this, true);
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (!_state.Enabled) return;
            _state.Enabled = false;
            SaveState();
        }
        EnabledChanged?.Invoke(this, false);
    }

    public TimeScale CurrentScale
    {
        get { lock (_lock) return _state.CurrentScale; }
    }

    // Alias for compatibility
    public TimeScale CurrentTimeScale => CurrentScale;

    public TimelineState GetState()
    {
        lock (_lock) return _state;
    }

    public void SetTimeScale(TimeScale scale)
    {
        lock (_lock)
        {
            if (_state.CurrentScale == scale) return;
            _state.CurrentScale = scale;
            SaveState();
        }
        TimeScaleChanged?.Invoke(this, scale);
    }

    // Intent management

    public Task<Intent?> CreateIntentAsync(string name, string branchName, string mainRepoPath, string? baseBranch = null, string? context = null)
    {
        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, "", mainRepoPath);
            _state.Intents.Add(intent);
            _state.IntentOrder.Add(intent.Id);
            _state.CurrentIntentId = intent.Id;
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        CurrentIntentChanged?.Invoke(this, intent);
        return Task.FromResult<Intent?>(intent);
    }

    public Task<Intent> CreateIntentFromExistingFolderAsync(string name, string existingFolderPath, string? context = null)
    {
        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, existingFolderPath, existingFolderPath);
            _state.Intents.Add(intent);
            _state.IntentOrder.Add(intent.Id);
            _state.CurrentIntentId = intent.Id;
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        CurrentIntentChanged?.Invoke(this, intent);
        return Task.FromResult(intent);
    }

    public Intent? GetIntent(string id)
    {
        lock (_lock) return _state.GetIntent(id);
    }

    IReadOnlyList<Intent> ITimelineService.GetAllIntents()
    {
        lock (_lock) return [.. _state.Intents];
    }

    // Internal version returns List for compatibility
    public List<Intent> GetAllIntents()
    {
        lock (_lock) return [.. _state.Intents];
    }

    public IReadOnlyList<Intent> GetOrderedIntents()
    {
        lock (_lock)
        {
            return _state.IntentOrder
                .Select(id => _state.GetIntent(id))
                .Where(i => i != null)
                .ToList()!;
        }
    }

    public IReadOnlyList<Intent> GetIntentsInOrder() => GetOrderedIntents();

    public IReadOnlyList<Intent> GetActiveIntents()
    {
        lock (_lock)
        {
            return _state.Intents
                .Where(i => i.Status == IntentStatus.Active || i.Status == IntentStatus.Paused)
                .ToList();
        }
    }

    public void UpdateIntent(Intent intent)
    {
        lock (_lock)
        {
            var existing = _state.GetIntent(intent.Id);
            if (existing == null) return;
            existing.Name = intent.Name;
            existing.Status = intent.Status;
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateIntentStatus(string intentId, IntentStatus status)
    {
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent == null) return;
            intent.Status = status;
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetIntentContext(string intentId, string? context)
    {
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent == null) return;
            SaveState();
        }
    }

    public Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false)
    {
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent == null) return Task.FromResult(false);
            _state.Intents.Remove(intent);
            _state.IntentOrder.Remove(intentId);
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }

    public void ReorderIntent(string intentId, int newIndex)
    {
        lock (_lock)
        {
            _state.IntentOrder.Remove(intentId);
            newIndex = Math.Clamp(newIndex, 0, _state.IntentOrder.Count);
            _state.IntentOrder.Insert(newIndex, intentId);
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Intent? GetCurrentIntent()
    {
        lock (_lock)
        {
            return _state.CurrentIntentId != null ? _state.GetIntent(_state.CurrentIntentId) : null;
        }
    }

    public void SetCurrentIntent(string? intentId)
    {
        Intent? intent = null;
        lock (_lock)
        {
            if (_state.CurrentIntentId == intentId) return;
            _state.CurrentIntentId = intentId;
            intent = intentId != null ? _state.GetIntent(intentId) : null;
            SaveState();
        }
        CurrentIntentChanged?.Invoke(this, intent);
    }

    // Session management

    public ClaudeSession StartSession(string intentId, string? parentSessionId = null)
    {
        ClaudeSession session;
        lock (_lock)
        {
            session = ClaudeSession.Create(intentId, parentSessionId);
            _state.Sessions.Add(session);
            SaveState();
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public Task<ClaudeSession?> ForkSessionAsync(string parentSessionId, string? initialPrompt = null)
    {
        ClaudeSession newSession;
        lock (_lock)
        {
            var parentSession = _state.Sessions.FirstOrDefault(s => s.Id == parentSessionId);
            if (parentSession == null)
                return Task.FromResult<ClaudeSession?>(null);
            newSession = ClaudeSession.Create(parentSession.IntentId, parentSessionId);
            _state.Sessions.Add(newSession);
            SaveState();
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult<ClaudeSession?>(newSession);
    }

    public ClaudeSession? GetSession(string id)
    {
        lock (_lock) return _state.Sessions.FirstOrDefault(s => s.Id == id);
    }

    IReadOnlyList<ClaudeSession> ITimelineService.GetAllSessions()
    {
        lock (_lock) return [.. _state.Sessions];
    }

    public List<ClaudeSession> GetAllSessions()
    {
        lock (_lock) return [.. _state.Sessions];
    }

    IReadOnlyList<ClaudeSession> ITimelineService.GetSessionsForIntent(string intentId)
    {
        lock (_lock) return _state.Sessions.Where(s => s.IntentId == intentId).ToList();
    }

    public List<ClaudeSession> GetSessionsForIntent(string intentId)
    {
        lock (_lock) return _state.Sessions.Where(s => s.IntentId == intentId).ToList();
    }

    IReadOnlyList<ClaudeSession> ITimelineService.GetRunningSessions()
    {
        lock (_lock) return _state.Sessions.Where(s => s.Status == ClaudeSessionStatus.Running).ToList();
    }

    public List<ClaudeSession> GetRunningSessions()
    {
        lock (_lock) return _state.Sessions.Where(s => s.Status == ClaudeSessionStatus.Running).ToList();
    }

    public void UpdateSession(ClaudeSession session)
    {
        lock (_lock)
        {
            var existing = _state.Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (existing == null) return;
            existing.Status = session.Status;
            existing.EndTime = session.EndTime;
            SaveState();
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSessionSuccess(string sessionId, string? commitHash = null, string? commitMessage = null, string? agentNotes = null)
    {
        ClaudeSession? session;
        lock (_lock)
        {
            session = _state.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;
            session.MarkSuccess(commitHash, commitMessage);
            SaveState();
        }
        SessionStatusChanged?.Invoke(this, session);
    }

    public void MarkSessionFailed(string sessionId, string? agentNotes = null)
    {
        ClaudeSession? session;
        lock (_lock)
        {
            session = _state.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;
            session.MarkFailed();
            SaveState();
        }
        SessionStatusChanged?.Invoke(this, session);
    }

    public void MarkSessionAbandoned(string sessionId)
    {
        ClaudeSession? session;
        lock (_lock)
        {
            session = _state.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;
            session.MarkAbandoned();
            SaveState();
        }
        SessionStatusChanged?.Invoke(this, session);
    }

    public void AddFileChange(string sessionId, string filePath, int additions = 0, int deletions = 0)
    {
        lock (_lock)
        {
            var session = _state.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;
            session.FilesChanged.Add(new FileChange { Path = filePath, Additions = additions, Deletions = deletions });
            SaveState();
        }
    }

    public void AddCommand(string sessionId, string command)
    {
        lock (_lock)
        {
            var session = _state.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;
            session.CommandsExecuted.Add(command);
            SaveState();
        }
    }

    public void SetContinueSessionId(string sessionId, string continueSessionId)
    {
        lock (_lock)
        {
            var session = _state.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;
            session.ContinueSessionId = continueSessionId;
            SaveState();
        }
    }

    // Cherry-pick

    public Task<GitOperationResult> CherryPickSessionAsync(string sourceSessionId, string targetIntentId)
    {
        // STUB: Not implemented
        return Task.FromResult(new GitOperationResult { Success = false, Error = "Not implemented in this version" });
    }

    public async Task<(bool Success, string? Error)> CherryPickAsync(string sessionId, string targetIntentId)
    {
        var result = await CherryPickSessionAsync(sessionId, targetIntentId);
        return (result.Success, result.Error);
    }

    // Focus time tracking

    public bool IsFocusing
    {
        get { lock (_lock) return _state.IsFocusing; }
    }

    public void StartFocusTimer()
    {
        lock (_lock)
        {
            _state.StartFocus();
            SaveState();
        }
        FocusStateChanged?.Invoke(this, true);
    }

    public void PauseFocusTimer()
    {
        lock (_lock)
        {
            _state.PauseFocus();
            SaveState();
        }
        FocusStateChanged?.Invoke(this, false);
    }

    public void ResetFocusTime()
    {
        lock (_lock)
        {
            _state.ResetFocusTime();
            SaveState();
        }
    }

    public TimeSpan GetTotalFocusTime()
    {
        lock (_lock) return _state.TotalFocusTime;
    }

    public TimeSpan GetCurrentFocusTime()
    {
        lock (_lock) return _state.CurrentFocusTime;
    }

    // Hook event processing

    public void HandleSessionStart(HookEvent hookEvent)
    {
        // STUB: Not fully implemented
    }

    public void HandleFileChanged(HookEvent hookEvent)
    {
        // STUB: Not fully implemented
    }

    public Task HandleSessionStopAsync(HookEvent hookEvent)
    {
        return Task.CompletedTask; // STUB
    }

    public Intent? FindIntentByWorkingDirectory(string workingDirectory)
    {
        lock (_lock)
        {
            return _state.Intents.FirstOrDefault(i =>
                string.Equals(i.WorktreePath, workingDirectory, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ClaudeSession? GetSessionByClaudeId(string claudeSessionId)
    {
        lock (_lock)
        {
            return _state.Sessions.FirstOrDefault(s => s.ContinueSessionId == claudeSessionId);
        }
    }

    // Legacy methods for compatibility
    public Task HandleSessionStartAsync(string sessionId, string cwd, string? transcriptPath)
    {
        return Task.CompletedTask; // STUB
    }

    public Task HandleFileChangedAsync(string sessionId, string filePath)
    {
        return Task.CompletedTask; // STUB
    }

    public Task HandleSessionStopAsync(string sessionId)
    {
        return Task.CompletedTask; // STUB
    }

    public List<ClaudeSession> AssignOrphansToIntent(string intentId, string cwd)
    {
        return []; // STUB
    }

    // Orphan sessions (stub implementation)
    public IReadOnlyList<OrphanSession> GetOrphanSessions()
    {
        return []; // STUB: macOS version doesn't track orphan sessions yet
    }

    public void RemoveOrphanSession(string orphanSessionId)
    {
        // STUB: macOS version doesn't track orphan sessions yet
    }

    public ClaudeSession? GetActiveClaudeSession(string projectPath)
    {
        lock (_lock)
        {
            // Normalize path for comparison
            var normalizedPath = Path.GetFullPath(projectPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();

            // Find running sessions, match by intent's main repo path
            return _state.Sessions
                .Where(s => s.Status == ClaudeSessionStatus.Running)
                .Select(s => new
                {
                    Session = s,
                    Intent = _state.GetIntent(s.IntentId)
                })
                .Where(x => x.Intent != null)
                .Where(x =>
                {
                    var intentPath = Path.GetFullPath(x.Intent!.MainRepoPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .ToLowerInvariant();
                    return intentPath == normalizedPath;
                })
                .OrderByDescending(x => x.Session.StartTime)
                .Select(x => x.Session)
                .FirstOrDefault();
        }
    }

    public void AddTaskToSession(string sessionId, FocusTask task)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.AddOrUpdateTask(task);
            SaveState();
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Persistence

    private TimelineState LoadState()
    {
        var config = _configService.Load();
        return config.TimelineState ?? new TimelineState();
    }

    private void SaveState()
    {
        var config = _configService.Load();
        config.TimelineState = _state;
        _configService.Save(config);
    }

    public Task SaveAsync()
    {
        SaveState();
        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        lock (_lock)
        {
            _state = LoadState();
        }
        return Task.CompletedTask;
    }
}
