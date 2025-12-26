using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for managing Timeline IDE state, intents, and Claude Code sessions.
/// </summary>
public interface ITimelineService
{
    #region Timeline State

    /// <summary>
    /// Gets whether Timeline IDE mode is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Enables Timeline IDE mode.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disables Timeline IDE mode.
    /// </summary>
    void Disable();

    /// <summary>
    /// Gets the current timeline state.
    /// </summary>
    TimelineState GetState();

    /// <summary>
    /// Gets the current time scale for the timeline view.
    /// </summary>
    TimeScale CurrentScale { get; }

    /// <summary>
    /// Sets the time scale for the timeline view.
    /// </summary>
    void SetTimeScale(TimeScale scale);

    #endregion

    #region Intent Management

    /// <summary>
    /// Creates a new intent with an associated git worktree.
    /// </summary>
    /// <param name="name">Human-readable name for the intent.</param>
    /// <param name="branchName">Git branch name for the worktree.</param>
    /// <param name="mainRepoPath">Path to the main repository.</param>
    /// <param name="baseBranch">Base branch to create the worktree from (default: current branch).</param>
    /// <param name="context">Optional context content for Claude Code sessions.</param>
    /// <returns>The created intent, or null if creation failed.</returns>
    Task<Intent?> CreateIntentAsync(string name, string branchName, string mainRepoPath, string? baseBranch = null, string? context = null);

    /// <summary>
    /// Creates a new intent from an existing folder (no worktree creation).
    /// </summary>
    /// <param name="name">Human-readable name for the intent.</param>
    /// <param name="existingFolderPath">Path to the existing folder.</param>
    /// <param name="context">Optional context content for Claude Code sessions.</param>
    /// <returns>The created intent.</returns>
    Task<Intent> CreateIntentFromExistingFolderAsync(string name, string existingFolderPath, string? context = null);

    /// <summary>
    /// Gets an intent by ID.
    /// </summary>
    Intent? GetIntent(string intentId);

    /// <summary>
    /// Gets all intents.
    /// </summary>
    IReadOnlyList<Intent> GetAllIntents();

    /// <summary>
    /// Gets intents in display order.
    /// </summary>
    IReadOnlyList<Intent> GetOrderedIntents();

    /// <summary>
    /// Gets active intents only.
    /// </summary>
    IReadOnlyList<Intent> GetActiveIntents();

    /// <summary>
    /// Updates an intent's properties.
    /// </summary>
    void UpdateIntent(Intent intent);

    /// <summary>
    /// Updates the status of an intent.
    /// </summary>
    void UpdateIntentStatus(string intentId, IntentStatus status);

    /// <summary>
    /// Sets the context for an intent (inline content).
    /// </summary>
    void SetIntentContext(string intentId, string? context);

    /// <summary>
    /// Deletes an intent and optionally its associated worktree.
    /// </summary>
    /// <param name="intentId">ID of the intent to delete.</param>
    /// <param name="removeWorktree">If true, also removes the git worktree.</param>
    Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false);

    /// <summary>
    /// Reorders intents by moving an intent to a new position.
    /// </summary>
    void ReorderIntent(string intentId, int newIndex);

    /// <summary>
    /// Gets the currently selected/focused intent.
    /// </summary>
    Intent? GetCurrentIntent();

    /// <summary>
    /// Sets the currently selected/focused intent.
    /// </summary>
    void SetCurrentIntent(string? intentId);

    #endregion

    #region Session Management

    /// <summary>
    /// Starts a new Claude Code session for an intent.
    /// </summary>
    /// <param name="intentId">ID of the intent.</param>
    /// <param name="initialPrompt">Optional initial prompt/task for Claude Code.</param>
    /// <returns>The created session.</returns>
    ClaudeSession StartSession(string intentId, string? initialPrompt = null);

    /// <summary>
    /// Forks a new session from an existing session.
    /// </summary>
    /// <param name="parentSessionId">ID of the session to fork from.</param>
    /// <param name="initialPrompt">Optional initial prompt for the forked session.</param>
    /// <returns>The created forked session.</returns>
    Task<ClaudeSession?> ForkSessionAsync(string parentSessionId, string? initialPrompt = null);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    ClaudeSession? GetSession(string sessionId);

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetAllSessions();

    /// <summary>
    /// Gets all sessions for a specific intent.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetSessionsForIntent(string intentId);

    /// <summary>
    /// Gets currently running sessions.
    /// </summary>
    IReadOnlyList<ClaudeSession> GetRunningSessions();

    /// <summary>
    /// Updates a session's properties.
    /// </summary>
    void UpdateSession(ClaudeSession session);

    /// <summary>
    /// Marks a session as successful.
    /// </summary>
    /// <param name="sessionId">ID of the session.</param>
    /// <param name="commitHash">Git commit hash (if any).</param>
    /// <param name="commitMessage">Git commit message (if any).</param>
    /// <param name="agentNotes">Agent's summary notes.</param>
    void MarkSessionSuccess(string sessionId, string? commitHash = null, string? commitMessage = null, string? agentNotes = null);

    /// <summary>
    /// Marks a session as failed.
    /// </summary>
    /// <param name="sessionId">ID of the session.</param>
    /// <param name="agentNotes">Agent's notes about the failure.</param>
    void MarkSessionFailed(string sessionId, string? agentNotes = null);

    /// <summary>
    /// Marks a session as abandoned.
    /// </summary>
    void MarkSessionAbandoned(string sessionId);

    /// <summary>
    /// Adds a file change to a session.
    /// </summary>
    void AddFileChange(string sessionId, string filePath, int additions, int deletions);

    /// <summary>
    /// Adds a command to a session's executed commands.
    /// </summary>
    void AddCommand(string sessionId, string command);

    /// <summary>
    /// Sets the Claude Code continue session ID for resuming.
    /// </summary>
    void SetContinueSessionId(string sessionId, string continueId);

    #endregion

    #region Cherry-pick

    /// <summary>
    /// Cherry-picks changes from one session to another intent.
    /// </summary>
    /// <param name="sourceSessionId">ID of the source session with the commit.</param>
    /// <param name="targetIntentId">ID of the target intent to apply changes to.</param>
    /// <returns>Operation result.</returns>
    Task<GitOperationResult> CherryPickSessionAsync(string sourceSessionId, string targetIntentId);

    #endregion

    #region Focus Time

    /// <summary>
    /// Gets the total accumulated focus time.
    /// </summary>
    TimeSpan GetTotalFocusTime();

    /// <summary>
    /// Gets the current focus time including any active session.
    /// </summary>
    TimeSpan GetCurrentFocusTime();

    /// <summary>
    /// Whether focus tracking is currently active.
    /// </summary>
    bool IsFocusing { get; }

    /// <summary>
    /// Starts focus time tracking.
    /// </summary>
    void StartFocusTimer();

    /// <summary>
    /// Pauses focus time tracking.
    /// </summary>
    void PauseFocusTimer();

    /// <summary>
    /// Resets focus time for a new day/session.
    /// </summary>
    void ResetFocusTime();

    #endregion

    #region Persistence

    /// <summary>
    /// Saves the current state to persistent storage.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Loads state from persistent storage.
    /// </summary>
    Task LoadAsync();

    #endregion

    #region Hook Event Handling

    /// <summary>
    /// Handles a session start event from Claude Code hooks.
    /// Creates or updates a session based on the event data.
    /// </summary>
    /// <param name="hookEvent">The hook event data.</param>
    void HandleSessionStart(HookEvent hookEvent);

    /// <summary>
    /// Handles a file changed event from Claude Code hooks.
    /// Adds the file to the current session's modified files list.
    /// </summary>
    /// <param name="hookEvent">The hook event data.</param>
    void HandleFileChanged(HookEvent hookEvent);

    /// <summary>
    /// Handles a session stop event from Claude Code hooks.
    /// Finalizes the session and gathers git commit data.
    /// </summary>
    /// <param name="hookEvent">The hook event data.</param>
    Task HandleSessionStopAsync(HookEvent hookEvent);

    /// <summary>
    /// Finds an intent by its worktree path.
    /// Used to match Claude Code sessions to intents via working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory path.</param>
    /// <returns>The matching intent, or null if not found.</returns>
    Intent? FindIntentByWorkingDirectory(string workingDirectory);

    /// <summary>
    /// Gets the active session for a Claude Code session ID.
    /// </summary>
    /// <param name="claudeSessionId">The Claude Code session ID.</param>
    /// <returns>The matching session, or null if not found.</returns>
    ClaudeSession? GetSessionByClaudeId(string claudeSessionId);

    #endregion

    #region Events

    /// <summary>
    /// Fired when Timeline IDE mode is enabled or disabled.
    /// </summary>
    event EventHandler<bool>? EnabledChanged;

    /// <summary>
    /// Fired when an intent is added, updated, or removed.
    /// </summary>
    event EventHandler? IntentsChanged;

    /// <summary>
    /// Fired when the current intent changes.
    /// </summary>
    event EventHandler<Intent?>? CurrentIntentChanged;

    /// <summary>
    /// Fired when a session is added, updated, or removed.
    /// </summary>
    event EventHandler? SessionsChanged;

    /// <summary>
    /// Fired when a session's status changes.
    /// </summary>
    event EventHandler<ClaudeSession>? SessionStatusChanged;

    /// <summary>
    /// Fired when focus time tracking state changes.
    /// </summary>
    event EventHandler<bool>? FocusStateChanged;

    /// <summary>
    /// Fired when the time scale changes.
    /// </summary>
    event EventHandler<TimeScale>? TimeScaleChanged;

    #endregion
}
