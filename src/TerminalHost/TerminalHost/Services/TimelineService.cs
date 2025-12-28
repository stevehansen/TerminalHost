using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Implementation of the Timeline Mode service.
/// Thread-safe with persistence to AppConfiguration.
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
    public event EventHandler<string?>? CurrentIntentChanged;
    public event EventHandler? SessionsChanged;
    public event EventHandler<ClaudeCodeSession>? SessionStatusChanged;
    public event EventHandler<bool>? FocusStateChanged;
    public event EventHandler<TimeScale>? TimeScaleChanged;

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

    public TimelineState GetState()
    {
        lock (_lock) return _state;
    }

    // Time scale

    public TimeScale CurrentScale
    {
        get { lock (_lock) return _state.CurrentScale; }
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

    public async Task<Intent> CreateIntentAsync(string name, string branchName, string? parentRepoPath = null, string? contextFile = null)
    {
        Intent intent;

        // Create worktree if we have a parent repo
        string worktreePath;
        if (!string.IsNullOrEmpty(parentRepoPath))
        {
            // Generate worktree path based on branch name
            var worktreeName = branchName.Replace("/", "-");
            var parentDir = Path.GetDirectoryName(parentRepoPath) ?? parentRepoPath;
            worktreePath = Path.Combine(parentDir, $"{Path.GetFileName(parentRepoPath)}-{worktreeName}");

            var result = await _worktreeService.CreateWorktreeAsync(parentRepoPath, worktreePath, branchName, createBranch: true);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to create worktree: {result.Error}");
            }
        }
        else
        {
            worktreePath = string.Empty;
        }

        lock (_lock)
        {
            intent = Intent.Create(name, worktreePath, branchName, parentRepoPath);

            if (!string.IsNullOrEmpty(contextFile))
            {
                intent.ContextFilePath = contextFile;
            }

            _state.AddIntent(intent);

            // Assign any orphan sessions for this working directory
            if (!string.IsNullOrEmpty(worktreePath))
            {
                _state.AssignOrphansToIntent(intent.Id, worktreePath);
            }

            SaveState();
        }

        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return intent;
    }

    public async Task<Intent> CreateIntentFromExistingFolderAsync(string name, string folderPath)
    {
        Intent intent;

        // Try to get branch name from the folder
        var branchName = await GetBranchNameAsync(folderPath) ?? "main";
        var repoRoot = await _worktreeService.GetRepositoryRootAsync(folderPath);

        lock (_lock)
        {
            intent = Intent.Create(name, folderPath, branchName, repoRoot);
            _state.AddIntent(intent);

            // Assign any orphan sessions for this working directory
            _state.AssignOrphansToIntent(intent.Id, folderPath);

            SaveState();
        }

        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return intent;
    }

    private async Task<string?> GetBranchNameAsync(string path)
    {
        try
        {
            var result = await _gitRunner.RunGitOperationAsync(path, "rev-parse --abbrev-ref HEAD");
            return result.Success ? result.Output?.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public Intent? GetIntent(string id)
    {
        lock (_lock) return _state.GetIntent(id);
    }

    public List<Intent> GetAllIntents()
    {
        lock (_lock) return [.. _state.Intents];
    }

    public List<Intent> GetOrderedIntents()
    {
        lock (_lock) return _state.GetOrderedIntents();
    }

    public List<Intent> GetActiveIntents()
    {
        lock (_lock) return _state.GetActiveIntents();
    }

    public void UpdateIntent(Intent intent)
    {
        lock (_lock)
        {
            var existing = _state.GetIntent(intent.Id);
            if (existing == null) return;

            // Update properties
            existing.Name = intent.Name;
            existing.Status = intent.Status;
            existing.ContextFilePath = intent.ContextFilePath;
            existing.ContextContent = intent.ContextContent;
            existing.LastActiveAt = DateTime.UtcNow;

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

            switch (status)
            {
                case IntentStatus.Active:
                    intent.Activate();
                    break;
                case IntentStatus.Paused:
                    intent.Pause();
                    break;
                case IntentStatus.Completed:
                    intent.Complete();
                    break;
                case IntentStatus.Abandoned:
                    intent.Abandon();
                    break;
            }

            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetIntentContext(string intentId, string? contextFilePath, string? contextContent)
    {
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent == null) return;

            intent.ContextFilePath = contextFilePath;
            intent.ContextContent = contextContent;
            SaveState();
        }
    }

    public async Task DeleteIntentAsync(string intentId, bool removeWorktree = false)
    {
        Intent? intent;
        lock (_lock)
        {
            intent = _state.GetIntent(intentId);
            if (intent == null) return;
        }

        // Remove worktree if requested
        if (removeWorktree && !string.IsNullOrEmpty(intent.WorktreePath) && !string.IsNullOrEmpty(intent.MainRepoPath))
        {
            await _worktreeService.RemoveWorktreeAsync(intent.MainRepoPath, intent.WorktreePath, force: true);
        }

        lock (_lock)
        {
            _state.RemoveIntent(intentId);
            SaveState();
        }

        IntentsChanged?.Invoke(this, EventArgs.Empty);
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
        lock (_lock) return _state.GetCurrentIntent();
    }

    public void SetCurrentIntent(string? intentId)
    {
        lock (_lock)
        {
            if (_state.CurrentIntentId == intentId) return;
            _state.CurrentIntentId = intentId;
            SaveState();
        }
        CurrentIntentChanged?.Invoke(this, intentId);
    }

    // Session management

    public ClaudeCodeSession StartSession(string intentId, string? parentSessionId = null)
    {
        ClaudeCodeSession session;
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent == null)
                throw new InvalidOperationException($"Intent {intentId} not found");

            session = ClaudeCodeSession.Create(intentId, parentSessionId);
            _state.AddSession(session);

            // Set intent as active
            intent.Activate();

            SaveState();
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public async Task<ClaudeCodeSession> ForkSessionAsync(string sessionId)
    {
        ClaudeCodeSession newSession;
        lock (_lock)
        {
            var parentSession = _state.GetSession(sessionId);
            if (parentSession == null)
                throw new InvalidOperationException($"Session {sessionId} not found");

            newSession = ClaudeCodeSession.Create(parentSession.IntentId, sessionId);
            _state.AddSession(newSession);
            SaveState();
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return newSession;
    }

    public ClaudeCodeSession? GetSession(string id)
    {
        lock (_lock) return _state.GetSession(id);
    }

    public List<ClaudeCodeSession> GetAllSessions()
    {
        lock (_lock) return [.. _state.Sessions];
    }

    public List<ClaudeCodeSession> GetSessionsForIntent(string intentId)
    {
        lock (_lock) return _state.GetSessionsForIntent(intentId);
    }

    public List<ClaudeCodeSession> GetRunningSessions()
    {
        lock (_lock) return _state.GetRunningSessions();
    }

    public void UpdateSession(ClaudeCodeSession session)
    {
        lock (_lock)
        {
            var existing = _state.GetSession(session.Id);
            if (existing == null) return;

            // Update properties
            existing.Status = session.Status;
            existing.EndTime = session.EndTime;
            existing.CommitHash = session.CommitHash;
            existing.CommitMessage = session.CommitMessage;
            existing.AgentNotes = session.AgentNotes;
            existing.FilesChanged = session.FilesChanged;
            existing.CommandsExecuted = session.CommandsExecuted;

            SaveState();
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSessionSuccess(string sessionId, string? commitHash = null, string? commitMessage = null)
    {
        ClaudeCodeSession? session;
        lock (_lock)
        {
            session = _state.GetSession(sessionId);
            if (session == null) return;

            session.MarkSuccess(commitHash, commitMessage);
            SaveState();
        }
        SessionStatusChanged?.Invoke(this, session);
    }

    public void MarkSessionFailed(string sessionId)
    {
        ClaudeCodeSession? session;
        lock (_lock)
        {
            session = _state.GetSession(sessionId);
            if (session == null) return;

            session.MarkFailed();
            SaveState();
        }
        SessionStatusChanged?.Invoke(this, session);
    }

    public void MarkSessionAbandoned(string sessionId)
    {
        ClaudeCodeSession? session;
        lock (_lock)
        {
            session = _state.GetSession(sessionId);
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
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.AddFileChange(filePath, additions, deletions);
            SaveState();
        }
    }

    public void AddCommand(string sessionId, string command)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.AddCommand(command);
            SaveState();
        }
    }

    public void SetContinueSessionId(string sessionId, string continueSessionId)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.ContinueSessionId = continueSessionId;
            SaveState();
        }
    }

    // Cherry-pick

    public async Task<(bool Success, string? Error)> CherryPickAsync(string sessionId, string targetIntentId)
    {
        ClaudeCodeSession? session;
        Intent? targetIntent;

        lock (_lock)
        {
            session = _state.GetSession(sessionId);
            targetIntent = _state.GetIntent(targetIntentId);
        }

        if (session == null)
            return (false, "Session not found");
        if (targetIntent == null)
            return (false, "Target intent not found");
        if (string.IsNullOrEmpty(session.CommitHash))
            return (false, "Session has no commit to cherry-pick");
        if (string.IsNullOrEmpty(targetIntent.WorktreePath))
            return (false, "Target intent has no worktree");

        var result = await _gitRunner.RunGitOperationAsync(targetIntent.WorktreePath, $"cherry-pick {session.CommitHash}");
        return (result.Success, result.Success ? null : result.Error);
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

    public async Task HandleSessionStartAsync(string sessionId, string cwd, string? transcriptPath)
    {
        lock (_lock)
        {
            // Check if there's an intent for this working directory
            var intent = _state.Intents.FirstOrDefault(i =>
                i.WorktreePath.Equals(cwd, StringComparison.OrdinalIgnoreCase));

            if (intent != null)
            {
                // Create a new session for this intent
                var session = ClaudeCodeSession.Create(intent.Id);
                session.ContinueSessionId = sessionId;
                session.TranscriptPath = transcriptPath;
                _state.AddSession(session);
            }
            else
            {
                // Store as orphan session
                var orphan = new OrphanSession
                {
                    SessionId = sessionId,
                    Cwd = cwd,
                    TranscriptPath = transcriptPath,
                    StartTime = DateTime.UtcNow
                };
                _state.AddOrUpdateOrphanSession(orphan);
            }

            SaveState();
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task HandleFileChangedAsync(string sessionId, string filePath)
    {
        lock (_lock)
        {
            // Find session by Claude Code session ID
            var session = _state.Sessions.FirstOrDefault(s => s.ContinueSessionId == sessionId);
            if (session != null)
            {
                session.AddFileChange(filePath);

                // If session was marked as completed, set back to running
                if (session.Status != ClaudeSessionStatus.Running)
                {
                    session.Status = ClaudeSessionStatus.Running;
                    session.EndTime = null;
                }
            }
            else
            {
                // Update orphan session
                var orphan = _state.GetOrphanSession(sessionId);
                if (orphan != null && !orphan.FilesModified.Contains(filePath))
                {
                    orphan.FilesModified.Add(filePath);
                }
            }

            SaveState();
        }
    }

    public async Task HandleSessionStopAsync(string sessionId)
    {
        ClaudeCodeSession? session;
        lock (_lock)
        {
            // Find session by Claude Code session ID
            session = _state.Sessions.FirstOrDefault(s => s.ContinueSessionId == sessionId);
            if (session != null)
            {
                session.MarkSuccess();
            }
            else
            {
                // Update orphan session
                var orphan = _state.GetOrphanSession(sessionId);
                if (orphan != null)
                {
                    orphan.EndTime = DateTime.UtcNow;
                }
            }

            SaveState();
        }

        if (session != null)
        {
            SessionStatusChanged?.Invoke(this, session);
        }
    }

    public List<ClaudeCodeSession> AssignOrphansToIntent(string intentId, string cwd)
    {
        List<ClaudeCodeSession> created;
        lock (_lock)
        {
            created = _state.AssignOrphansToIntent(intentId, cwd);
            SaveState();
        }

        if (created.Count > 0)
        {
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        return created;
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
}
