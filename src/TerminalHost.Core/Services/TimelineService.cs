using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for managing Timeline IDE state, intents, and Claude Code sessions.
/// Persists data through ConfigurationService.
/// </summary>
public sealed class TimelineService : ITimelineService
{
    private readonly IConfigurationService _configService;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitProcessRunner _gitRunner;
    private readonly object _lock = new();

    // Cached state (loaded from config)
    private TimelineState _state = new();

    public TimelineService(
        IConfigurationService configService,
        IGitWorktreeService worktreeService,
        IGitProcessRunner gitRunner)
    {
        _configService = configService;
        _worktreeService = worktreeService;
        _gitRunner = gitRunner;
        LoadFromConfig();
    }

    #region Events

    public event EventHandler<bool>? EnabledChanged;
    public event EventHandler? IntentsChanged;
    public event EventHandler<Intent?>? CurrentIntentChanged;
    public event EventHandler? SessionsChanged;
    public event EventHandler<ClaudeSession>? SessionStatusChanged;
    public event EventHandler<bool>? FocusStateChanged;
    public event EventHandler<TimeScale>? TimeScaleChanged;
    public event EventHandler<(string WorktreePath, string? InitialPrompt)>? OpenProjectRequested;
    public event EventHandler? OrphanSessionsChanged;

    private void OnEnabledChanged(bool enabled) =>
        EnabledChanged?.Invoke(this, enabled);

    private void OnIntentsChanged() =>
        IntentsChanged?.Invoke(this, EventArgs.Empty);

    private void OnCurrentIntentChanged(Intent? intent) =>
        CurrentIntentChanged?.Invoke(this, intent);

    private void OnSessionsChanged() =>
        SessionsChanged?.Invoke(this, EventArgs.Empty);

    private void OnSessionStatusChanged(ClaudeSession session) =>
        SessionStatusChanged?.Invoke(this, session);

    private void OnFocusStateChanged(bool isFocusing) =>
        FocusStateChanged?.Invoke(this, isFocusing);

    private void OnTimeScaleChanged(TimeScale scale) =>
        TimeScaleChanged?.Invoke(this, scale);

    private void OnOrphanSessionsChanged() =>
        OrphanSessionsChanged?.Invoke(this, EventArgs.Empty);

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
        lock (_lock)
        {
            SaveToConfig();
        }
        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        lock (_lock)
        {
            LoadFromConfig();
        }
        return Task.CompletedTask;
    }

    #endregion

    #region Timeline State

    public bool IsEnabled
    {
        get
        {
            lock (_lock)
            {
                return _state.Enabled;
            }
        }
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

            // Pause focus timer if running
            _state.PauseFocus();
            _state.Enabled = false;
            SaveToConfig();
        }
        OnEnabledChanged(false);
    }

    public TimelineState GetState()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    public TimeScale CurrentScale
    {
        get
        {
            lock (_lock)
            {
                return _state.CurrentScale;
            }
        }
    }

    public void SetTimeScale(TimeScale scale)
    {
        lock (_lock)
        {
            if (_state.CurrentScale == scale) return;

            _state.CurrentScale = scale;
            SaveToConfig();
        }
        OnTimeScaleChanged(scale);
    }

    #endregion

    #region Intent Management

    public async Task<Intent?> CreateIntentAsync(
        string name,
        string branchName,
        string mainRepoPath,
        string? baseBranch = null,
        string? context = null)
    {
        // Generate worktree path (sibling to main repo)
        var parentDir = Path.GetDirectoryName(mainRepoPath);
        if (string.IsNullOrEmpty(parentDir))
            return null;

        var repoName = Path.GetFileName(mainRepoPath);
        var safeBranchName = branchName.Replace("/", "-").Replace("\\", "-");
        var worktreePath = Path.Combine(parentDir, $"{repoName}-{safeBranchName}");

        // Create the git worktree
        var result = await _worktreeService.CreateWorktreeAsync(
            mainRepoPath,
            branchName,
            worktreePath,
            createBranch: true);

        if (!result.Success)
            return null;

        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, branchName, worktreePath, mainRepoPath);

            if (!string.IsNullOrEmpty(context))
            {
                intent.ContextContent = context;
            }

            _state.AddIntent(intent);
            SaveToConfig();
        }

        // Import any orphan sessions that were tracked for this directory
        AssignOrphansToIntent(worktreePath, intent.Id);

        OnIntentsChanged();
        return intent;
    }

    public async Task<Intent> CreateIntentFromExistingFolderAsync(
        string name,
        string existingFolderPath,
        string? context = null)
    {
        // Get the current branch name from the folder (if it's a git repo)
        string branchName = "main";
        try
        {
            var output = await _gitRunner.RunGitCommandAsync(
                existingFolderPath,
                "rev-parse --abbrev-ref HEAD");

            if (!string.IsNullOrWhiteSpace(output))
            {
                branchName = output.Trim();
            }
        }
        catch
        {
            // Not a git repo or git not available - use default branch name
        }

        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, branchName, existingFolderPath, existingFolderPath);

            if (!string.IsNullOrEmpty(context))
            {
                intent.ContextContent = context;
            }

            _state.AddIntent(intent);
            SaveToConfig();
        }

        // Import any orphan sessions that were tracked for this directory
        var imported = AssignOrphansToIntent(existingFolderPath, intent.Id);

        OnIntentsChanged();
        return intent;
    }

    public Intent? GetIntent(string intentId)
    {
        lock (_lock)
        {
            return _state.GetIntent(intentId);
        }
    }

    public IReadOnlyList<Intent> GetAllIntents()
    {
        lock (_lock)
        {
            return _state.Intents.ToList();
        }
    }

    public IReadOnlyList<Intent> GetOrderedIntents()
    {
        lock (_lock)
        {
            return _state.GetOrderedIntents().ToList();
        }
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
        {
            OnCurrentIntentChanged(intent);
        }
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
            {
                intent.CompletedAt = DateTime.UtcNow;
            }

            SaveToConfig();
        }
        OnIntentsChanged();

        if (_state.CurrentIntentId == intentId)
        {
            OnCurrentIntentChanged(intent);
        }
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

        // Try to remove worktree if requested (don't fail if worktree removal fails)
        if (removeWorktree && !string.IsNullOrEmpty(intent.WorktreePath))
        {
            try
            {
                await _worktreeService.RemoveWorktreeAsync(intent.WorktreePath, force: true);
            }
            catch
            {
                // Ignore worktree removal errors - still delete the intent
            }
        }

        // Always delete the intent from state
        lock (_lock)
        {
            _state.RemoveIntent(intentId);
            SaveToConfig();
        }

        OnIntentsChanged();

        if (_state.CurrentIntentId == intentId)
        {
            OnCurrentIntentChanged(null);
        }

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
            {
                _state.IntentOrder.Add(intentId);
            }
            else
            {
                _state.IntentOrder.Insert(Math.Max(0, newIndex), intentId);
            }

            SaveToConfig();
        }
        OnIntentsChanged();
    }

    public Intent? GetCurrentIntent()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_state.CurrentIntentId))
                return null;

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

    #region Session Management

    public ClaudeSession StartSession(string intentId, string? initialPrompt = null)
    {
        ClaudeSession session;
        string? worktreePath = null;

        lock (_lock)
        {
            session = ClaudeSession.Create(intentId);
            session.InitialPrompt = initialPrompt;

            _state.AddSession(session);

            // Update intent's last active time and get worktree path
            var intent = _state.GetIntent(intentId);
            if (intent != null)
            {
                intent.LastActiveAt = DateTime.UtcNow;
                worktreePath = intent.WorktreePath;
            }

            SaveToConfig();
        }

        OnSessionsChanged();

        // Request opening the project (this will open a new terminal tab)
        if (!string.IsNullOrEmpty(worktreePath))
        {
            OpenProjectRequested?.Invoke(this, (worktreePath, initialPrompt));
        }

        return session;
    }

    public async Task<ClaudeSession?> ForkSessionAsync(string parentSessionId, string? initialPrompt = null)
    {
        ClaudeSession? parentSession;
        Intent? intent;

        lock (_lock)
        {
            parentSession = _state.GetSession(parentSessionId);
            if (parentSession == null) return null;

            intent = _state.GetIntent(parentSession.IntentId);
            if (intent == null) return null;
        }

        // If the parent session has a commit, we need to checkout that commit
        // in the worktree before starting the fork
        if (!string.IsNullOrEmpty(parentSession.CommitHash) && !string.IsNullOrEmpty(intent.WorktreePath))
        {
            var checkoutResult = await _gitRunner.RunGitOperationAsync(
                intent.WorktreePath,
                $"checkout {parentSession.CommitHash}");

            if (!checkoutResult.Success)
                return null;
        }

        ClaudeSession forkedSession;
        lock (_lock)
        {
            forkedSession = ClaudeSession.Create(parentSession.IntentId, parentSessionId);
            forkedSession.InitialPrompt = initialPrompt;

            _state.AddSession(forkedSession);
            SaveToConfig();
        }

        OnSessionsChanged();
        return forkedSession;
    }

    public ClaudeSession? GetSession(string sessionId)
    {
        lock (_lock)
        {
            return _state.GetSession(sessionId);
        }
    }

    public IReadOnlyList<ClaudeSession> GetAllSessions()
    {
        lock (_lock)
        {
            return _state.Sessions.ToList();
        }
    }

    public IReadOnlyList<ClaudeSession> GetSessionsForIntent(string intentId)
    {
        lock (_lock)
        {
            return _state.GetSessionsForIntent(intentId).ToList();
        }
    }

    public IReadOnlyList<ClaudeSession> GetRunningSessions()
    {
        lock (_lock)
        {
            return _state.Sessions
                .Where(s => s.Status == ClaudeSessionStatus.Running)
                .ToList();
        }
    }

    public void UpdateSession(ClaudeSession session)
    {
        lock (_lock)
        {
            var existing = _state.Sessions.FirstOrDefault(s => s.Id == session.Id);
            if (existing != null)
            {
                var index = _state.Sessions.IndexOf(existing);
                _state.Sessions[index] = session;
                SaveToConfig();
            }
        }
        OnSessionsChanged();
    }

    public void MarkSessionSuccess(string sessionId, string? commitHash = null, string? commitMessage = null, string? agentNotes = null)
    {
        ClaudeSession? session;
        lock (_lock)
        {
            session = _state.GetSession(sessionId);
            if (session == null) return;

            session.MarkSuccess(commitHash, commitMessage, agentNotes);
            SaveToConfig();
        }
        OnSessionsChanged();
        OnSessionStatusChanged(session);
    }

    public void MarkSessionFailed(string sessionId, string? agentNotes = null)
    {
        ClaudeSession? session;
        lock (_lock)
        {
            session = _state.GetSession(sessionId);
            if (session == null) return;

            session.MarkFailed(agentNotes);
            SaveToConfig();
        }
        OnSessionsChanged();
        OnSessionStatusChanged(session);
    }

    public void MarkSessionAbandoned(string sessionId)
    {
        ClaudeSession? session;
        lock (_lock)
        {
            session = _state.GetSession(sessionId);
            if (session == null) return;

            session.MarkAbandoned();
            SaveToConfig();
        }
        OnSessionsChanged();
        OnSessionStatusChanged(session);
    }

    public void AddFileChange(string sessionId, string filePath, int additions, int deletions)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.AddFileChange(filePath, additions, deletions);
            SaveToConfig();
        }
        OnSessionsChanged();
    }

    public void AddCommand(string sessionId, string command)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.AddCommand(command);
            SaveToConfig();
        }
    }

    public void SetContinueSessionId(string sessionId, string continueId)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.ContinueSessionId = continueId;
            SaveToConfig();
        }
    }

    /// <summary>
    /// Gets the currently active (running) Claude session for a specific project path.
    /// Returns the most recent running session if multiple exist.
    /// </summary>
    /// <param name="projectPath">The project directory path</param>
    /// <returns>Active session or null if none found</returns>
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

    /// <summary>
    /// Adds or updates a Claude task in the specified session.
    /// Creates a snapshot of the task and stores it in the session's task list.
    /// </summary>
    /// <param name="sessionId">The session ID to add the task to</param>
    /// <param name="task">The FocusTask to add/update</param>
    public void AddTaskToSession(string sessionId, FocusTask task)
    {
        lock (_lock)
        {
            var session = _state.GetSession(sessionId);
            if (session == null) return;

            session.AddOrUpdateTask(task);
            SaveToConfig();
        }
        OnSessionsChanged();
    }

    #endregion

    #region Orphan Sessions

    /// <summary>
    /// Gets all unassigned orphan sessions.
    /// </summary>
    public IReadOnlyList<OrphanSession> GetOrphanSessions()
    {
        lock (_lock)
        {
            return _state.GetUnassignedOrphanSessions().ToList();
        }
    }

    /// <summary>
    /// Gets orphan sessions for a specific working directory.
    /// </summary>
    public IReadOnlyList<OrphanSession> GetOrphanSessionsForPath(string path)
    {
        lock (_lock)
        {
            return _state.GetOrphanSessionsForCwd(path).ToList();
        }
    }

    /// <summary>
    /// Gets the count of unassigned orphan sessions.
    /// </summary>
    public int GetOrphanSessionCount()
    {
        lock (_lock)
        {
            return _state.OrphanSessionCount;
        }
    }

    /// <summary>
    /// Assigns an orphan session to an intent, converting it to a proper ClaudeSession.
    /// </summary>
    public ClaudeSession? AssignOrphanToIntent(string orphanSessionId, string intentId)
    {
        ClaudeSession? session = null;
        lock (_lock)
        {
            var orphan = _state.OrphanSessions.FirstOrDefault(o =>
                o.SessionId == orphanSessionId && !o.IsAssigned);
            if (orphan == null) return null;

            var intent = _state.GetIntent(intentId);
            if (intent == null) return null;

            session = orphan.ToClaudeSession(intentId);
            orphan.IsAssigned = true;
            orphan.AssignedSessionId = session.Id;

            _state.AddSession(session);
            SaveToConfig();
        }

        OnSessionsChanged();
        OnOrphanSessionsChanged();
        return session;
    }

    /// <summary>
    /// Assigns all orphan sessions from a directory to an intent.
    /// Called when creating an intent - imports any previous sessions.
    /// </summary>
    public List<ClaudeSession> AssignOrphansToIntent(string cwd, string intentId)
    {
        var assigned = new List<ClaudeSession>();
        lock (_lock)
        {
            var orphans = _state.GetOrphanSessionsForCwd(cwd).ToList();
            foreach (var orphan in orphans)
            {
                var session = orphan.ToClaudeSession(intentId);
                orphan.IsAssigned = true;
                orphan.AssignedSessionId = session.Id;
                _state.AddSession(session);
                assigned.Add(session);
            }

            if (assigned.Count > 0)
                SaveToConfig();
        }

        if (assigned.Count > 0)
        {
            OnSessionsChanged();
            OnOrphanSessionsChanged();
        }
        return assigned;
    }

    /// <summary>
    /// Removes an orphan session (if user dismisses it).
    /// </summary>
    public void RemoveOrphanSession(string orphanSessionId)
    {
        lock (_lock)
        {
            var orphan = _state.OrphanSessions.FirstOrDefault(o => o.SessionId == orphanSessionId);
            if (orphan != null)
            {
                _state.OrphanSessions.Remove(orphan);
                SaveToConfig();
            }
        }
        OnOrphanSessionsChanged();
    }

    #endregion

    #region Cherry-pick

    public async Task<GitOperationResult> CherryPickSessionAsync(string sourceSessionId, string targetIntentId)
    {
        ClaudeSession? sourceSession;
        Intent? targetIntent;

        lock (_lock)
        {
            sourceSession = _state.GetSession(sourceSessionId);
            if (sourceSession == null || string.IsNullOrEmpty(sourceSession.CommitHash))
            {
                return new GitOperationResult
                {
                    Success = false,
                    Error = "Source session has no commit to cherry-pick"
                };
            }

            targetIntent = _state.GetIntent(targetIntentId);
            if (targetIntent == null || string.IsNullOrEmpty(targetIntent.WorktreePath))
            {
                return new GitOperationResult
                {
                    Success = false,
                    Error = "Target intent not found or has no worktree"
                };
            }
        }

        // Run git cherry-pick in the target worktree
        return await _gitRunner.RunGitOperationAsync(
            targetIntent.WorktreePath,
            $"cherry-pick {sourceSession.CommitHash}");
    }

    #endregion

    #region Focus Time

    public TimeSpan GetTotalFocusTime()
    {
        lock (_lock)
        {
            return _state.TotalFocusTime;
        }
    }

    public TimeSpan GetCurrentFocusTime()
    {
        lock (_lock)
        {
            return _state.CurrentFocusTime;
        }
    }

    public bool IsFocusing
    {
        get
        {
            lock (_lock)
            {
                return _state.IsFocusing;
            }
        }
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

        if (wasFocusing)
        {
            OnFocusStateChanged(false);
        }
    }

    #endregion

    #region Hook Event Handling

    /// <summary>
    /// Finds an intent by matching the working directory to worktree paths.
    /// </summary>
    public Intent? FindIntentByWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
            return null;

        // Normalize the path for comparison
        var normalizedCwd = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        lock (_lock)
        {
            return _state.Intents.FirstOrDefault(intent =>
            {
                if (string.IsNullOrEmpty(intent.WorktreePath))
                    return false;

                var normalizedWorktree = Path.GetFullPath(intent.WorktreePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return string.Equals(normalizedCwd, normalizedWorktree, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    /// <summary>
    /// Gets a session by its Claude Code session ID (from hooks).
    /// </summary>
    public ClaudeSession? GetSessionByClaudeId(string claudeSessionId)
    {
        if (string.IsNullOrEmpty(claudeSessionId))
            return null;

        lock (_lock)
        {
            return _state.Sessions.FirstOrDefault(s =>
                string.Equals(s.ContinueSessionId, claudeSessionId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Handles session start events from Claude Code hooks.
    /// </summary>
    public void HandleSessionStart(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId) || string.IsNullOrEmpty(hookEvent.Cwd))
            return;

        // Find the intent by working directory
        var intent = FindIntentByWorkingDirectory(hookEvent.Cwd);
        if (intent == null)
        {
            // No matching intent - store as orphan session for later assignment
            lock (_lock)
            {
                var existingOrphan = _state.GetOrphanSession(hookEvent.SessionId);
                if (existingOrphan != null)
                {
                    // Update existing orphan (might be a --continue)
                    // Keep the earliest start time
                    if (hookEvent.Timestamp < existingOrphan.StartTime)
                        existingOrphan.StartTime = hookEvent.Timestamp;
                    existingOrphan.TranscriptPath = hookEvent.TranscriptPath ?? existingOrphan.TranscriptPath;
                }
                else
                {
                    var orphan = new OrphanSession
                    {
                        SessionId = hookEvent.SessionId,
                        Cwd = hookEvent.Cwd,
                        TranscriptPath = hookEvent.TranscriptPath,
                        StartTime = hookEvent.Timestamp
                    };
                    _state.AddOrUpdateOrphanSession(orphan);
                }
                SaveToConfig();
            }
            OnOrphanSessionsChanged();
            return;
        }

        // Check if we already have a session with this Claude session ID
        var existingSession = GetSessionByClaudeId(hookEvent.SessionId);
        if (existingSession != null)
        {
            // Session already exists - only reactivate if it was still in Running status
            // Don't reactivate sessions that were explicitly marked as Success/Failed/Abandoned
            // This prevents /compact and other commands from incorrectly restarting completed sessions
            lock (_lock)
            {
                if (hookEvent.Timestamp < existingSession.StartTime)
                    existingSession.StartTime = hookEvent.Timestamp;

                // Only update to Running if it wasn't explicitly completed/failed/abandoned
                if (existingSession.Status == ClaudeSessionStatus.Running)
                {
                    existingSession.RecordActivity();
                }
                // If session was explicitly ended, don't touch it - this is likely a follow-up command
                // like /compact that shouldn't restart the session

                SaveToConfig();
            }
            OnSessionsChanged();
            return;
        }

        // Check if there's an orphan session that should be converted
        OrphanSession? orphanToConvert;
        lock (_lock)
        {
            orphanToConvert = _state.GetOrphanSession(hookEvent.SessionId);
        }

        // Create a new session
        ClaudeSession session;
        lock (_lock)
        {
            session = ClaudeSession.Create(intent.Id);
            session.ContinueSessionId = hookEvent.SessionId;
            session.TranscriptPath = hookEvent.TranscriptPath;

            // Use the earliest timestamp between hook event and any existing orphan data
            session.StartTime = orphanToConvert != null && orphanToConvert.StartTime < hookEvent.Timestamp
                ? orphanToConvert.StartTime
                : hookEvent.Timestamp;

            // Copy files and transcript from orphan if present
            if (orphanToConvert != null)
            {
                foreach (var file in orphanToConvert.FilesModified)
                    session.AddFileChange(file, 0, 0);
                session.TranscriptPath ??= orphanToConvert.TranscriptPath;
                orphanToConvert.IsAssigned = true;
                orphanToConvert.AssignedSessionId = session.Id;
            }

            _state.AddSession(session);

            // Update intent's last active time
            intent.LastActiveAt = DateTime.UtcNow;

            SaveToConfig();
        }

        OnSessionsChanged();
    }

    /// <summary>
    /// Handles file changed events from Claude Code hooks.
    /// </summary>
    public void HandleFileChanged(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId) || string.IsNullOrEmpty(hookEvent.FilePath))
            return;

        // Find the session by Claude session ID
        var session = GetSessionByClaudeId(hookEvent.SessionId);
        if (session != null)
        {
            lock (_lock)
            {
                // Only track file changes for running sessions
                // Don't reactivate completed/failed/abandoned sessions - the file change
                // might be from a follow-up command like /compact that modifies files
                if (session.Status == ClaudeSessionStatus.Running)
                {
                    // Add the file to the session (we don't have line counts from hooks yet)
                    // Note: AddFileChange already updates LastActivityTime
                    session.AddFileChange(hookEvent.FilePath, 0, 0);
                    SaveToConfig();
                }
                // If session was explicitly ended, ignore file changes
            }
            OnSessionsChanged();
            return;
        }

        // Check if there's an orphan session for this
        lock (_lock)
        {
            var orphan = _state.GetOrphanSession(hookEvent.SessionId);
            if (orphan != null)
            {
                orphan.AddFile(hookEvent.FilePath);
                // Also clear end time if session continues
                if (orphan.EndTime.HasValue)
                    orphan.EndTime = null;
                SaveToConfig();
                OnOrphanSessionsChanged();
            }
            // If no session and no orphan, the file change is lost
            // This shouldn't happen in normal flow since session_start should create one
        }
    }

    /// <summary>
    /// Handles session stop events from Claude Code hooks.
    /// </summary>
    public async Task HandleSessionStopAsync(HookEvent hookEvent)
    {
        if (string.IsNullOrEmpty(hookEvent.SessionId))
            return;

        // Find the session by Claude session ID
        var session = GetSessionByClaudeId(hookEvent.SessionId);
        if (session == null)
        {
            // Check if there's an orphan session to finalize
            lock (_lock)
            {
                var orphan = _state.GetOrphanSession(hookEvent.SessionId);
                if (orphan != null)
                {
                    orphan.EndTime = hookEvent.Timestamp;
                    orphan.TranscriptPath = hookEvent.TranscriptPath ?? orphan.TranscriptPath;
                    CleanupOldOrphanSessions();
                    SaveToConfig();
                    OnOrphanSessionsChanged();
                }
            }
            return;
        }

        // Find the intent to get the worktree path for git operations
        Intent? intent;
        lock (_lock)
        {
            intent = _state.GetIntent(session.IntentId);
        }

        // Set end time and transcript path first
        lock (_lock)
        {
            session.EndTime = hookEvent.Timestamp;
            session.TranscriptPath ??= hookEvent.TranscriptPath;
        }

        // Gather git data if we have a worktree
        if (intent != null && !string.IsNullOrEmpty(intent.WorktreePath))
        {
            await GatherSessionGitDataAsync(session, intent.WorktreePath);
        }
        else
        {
            // No worktree - just mark as complete without git data
            lock (_lock)
            {
                if (session.FilesChanged.Count > 0)
                {
                    session.Status = ClaudeSessionStatus.Success;
                }
                else
                {
                    // No files changed - could be a failed or cancelled session
                    session.Status = ClaudeSessionStatus.Success; // Default to success
                }
                SaveToConfig();
            }
        }

        // Try to parse transcript for commands and summary
        await TryParseTranscriptAsync(session);

        OnSessionsChanged();
        OnSessionStatusChanged(session);
    }

    /// <summary>
    /// Attempts to parse the transcript file to extract commands and agent notes.
    /// </summary>
    private async Task TryParseTranscriptAsync(ClaudeSession session)
    {
        if (string.IsNullOrEmpty(session.TranscriptPath))
            return;

        // Only parse if we don't already have commands/notes
        if (session.CommandsExecuted.Count > 0 || !string.IsNullOrEmpty(session.AgentNotes))
            return;

        try
        {
            var parser = new TranscriptParserService();
            var result = await parser.ParseTranscriptAsync(session.TranscriptPath);

            if (result.ParsedSuccessfully)
            {
                lock (_lock)
                {
                    foreach (var command in result.Commands)
                    {
                        session.AddCommand(command);
                    }

                    if (!string.IsNullOrEmpty(result.Summary))
                    {
                        session.AgentNotes = result.Summary;
                    }

                    SaveToConfig();
                }
            }
        }
        catch
        {
            // Transcript parsing is best-effort, don't fail the session
        }
    }

    /// <summary>
    /// Cleans up old orphan sessions, keeping only the most recent unassigned ones.
    /// </summary>
    private void CleanupOldOrphanSessions()
    {
        const int maxOrphanSessions = 20;

        // Get unassigned orphans ordered by start time (most recent first)
        var unassigned = _state.OrphanSessions
            .Where(o => !o.IsAssigned)
            .OrderByDescending(o => o.StartTime)
            .ToList();

        if (unassigned.Count > maxOrphanSessions)
        {
            // Remove the oldest ones
            var toRemove = unassigned.Skip(maxOrphanSessions).ToList();
            foreach (var orphan in toRemove)
            {
                _state.OrphanSessions.Remove(orphan);
            }
        }

        // Also remove old assigned orphans (keep for 7 days for reference)
        var oldAssigned = _state.OrphanSessions
            .Where(o => o.IsAssigned && o.EndTime.HasValue &&
                (DateTime.UtcNow - o.EndTime.Value).TotalDays > 7)
            .ToList();
        foreach (var orphan in oldAssigned)
        {
            _state.OrphanSessions.Remove(orphan);
        }
    }

    /// <summary>
    /// Gathers git commit data for a completed session.
    /// </summary>
    private async Task GatherSessionGitDataAsync(ClaudeSession session, string worktreePath)
    {
        try
        {
            // Get the latest commit info
            var logResult = await _gitRunner.RunGitOperationAsync(
                worktreePath,
                "log -1 --format=%H|||%s");

            if (logResult.Success && !string.IsNullOrEmpty(logResult.Output))
            {
                var parts = logResult.Output.Trim().Split("|||", 2);
                if (parts.Length >= 1)
                {
                    lock (_lock)
                    {
                        session.CommitHash = parts[0].Trim();
                        if (parts.Length >= 2)
                        {
                            session.CommitMessage = parts[1].Trim();
                        }
                    }
                }
            }

            // Get file stats from the last commit
            var diffStatResult = await _gitRunner.RunGitOperationAsync(
                worktreePath,
                "diff --stat HEAD~1 HEAD 2>/dev/null || git diff --stat HEAD");

            if (diffStatResult.Success && !string.IsNullOrEmpty(diffStatResult.Output))
            {
                // Parse diff stat output (e.g., " src/file.cs | 10 +++++-----")
                var lines = diffStatResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("|"))
                    {
                        var lineParts = line.Split('|', 2);
                        if (lineParts.Length == 2)
                        {
                            var filePath = lineParts[0].Trim();
                            var stats = lineParts[1].Trim();

                            // Count + and - characters for rough additions/deletions
                            var additions = stats.Count(c => c == '+');
                            var deletions = stats.Count(c => c == '-');

                            lock (_lock)
                            {
                                session.AddFileChange(filePath, additions, deletions);
                            }
                        }
                    }
                }
            }

            lock (_lock)
            {
                session.Status = ClaudeSessionStatus.Success;
                SaveToConfig();
            }
        }
        catch
        {
            // Git operations failed - still mark session as complete
            lock (_lock)
            {
                session.Status = ClaudeSessionStatus.Success;
                SaveToConfig();
            }
        }
    }

    #endregion
}
