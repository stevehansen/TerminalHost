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

        // Remove worktree if requested
        if (removeWorktree && !string.IsNullOrEmpty(intent.WorktreePath))
        {
            var result = await _worktreeService.RemoveWorktreeAsync(intent.WorktreePath, force: true);
            if (!result.Success)
                return false;
        }

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
        lock (_lock)
        {
            session = ClaudeSession.Create(intentId);
            session.InitialPrompt = initialPrompt;

            _state.AddSession(session);

            // Update intent's last active time
            var intent = _state.GetIntent(intentId);
            if (intent != null)
            {
                intent.LastActiveAt = DateTime.UtcNow;
            }

            SaveToConfig();
        }

        OnSessionsChanged();
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
            // No matching intent - could optionally track unassigned sessions
            return;
        }

        // Check if we already have a session with this Claude session ID
        var existingSession = GetSessionByClaudeId(hookEvent.SessionId);
        if (existingSession != null)
        {
            // Session already exists (possibly from a --continue invocation)
            // Just update the timestamp
            lock (_lock)
            {
                existingSession.StartTime = hookEvent.Timestamp;
                existingSession.Status = ClaudeSessionStatus.Running;
                SaveToConfig();
            }
            OnSessionsChanged();
            return;
        }

        // Create a new session
        ClaudeSession session;
        lock (_lock)
        {
            session = ClaudeSession.Create(intent.Id);
            session.ContinueSessionId = hookEvent.SessionId;
            session.StartTime = hookEvent.Timestamp;

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
        if (session == null)
        {
            // No matching session - might be an untracked session
            return;
        }

        lock (_lock)
        {
            // Add the file to the session (we don't have line counts from hooks yet)
            session.AddFileChange(hookEvent.FilePath, 0, 0);
            SaveToConfig();
        }

        OnSessionsChanged();
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
            // No matching session
            return;
        }

        // Find the intent to get the worktree path for git operations
        Intent? intent;
        lock (_lock)
        {
            intent = _state.GetIntent(session.IntentId);
        }

        // Set end time first
        lock (_lock)
        {
            session.EndTime = hookEvent.Timestamp;
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

        OnSessionsChanged();
        OnSessionStatusChanged(session);
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
