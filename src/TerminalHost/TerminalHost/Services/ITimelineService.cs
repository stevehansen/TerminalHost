using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for managing Timeline Mode state, intents, and sessions.
/// </summary>
public interface ITimelineService
{
    // Events

    /// <summary>Raised when Timeline Mode is enabled or disabled.</summary>
    event EventHandler<bool>? EnabledChanged;

    /// <summary>Raised when the intent list changes.</summary>
    event EventHandler? IntentsChanged;

    /// <summary>Raised when the current/selected intent changes.</summary>
    event EventHandler<string?>? CurrentIntentChanged;

    /// <summary>Raised when the session list changes.</summary>
    event EventHandler? SessionsChanged;

    /// <summary>Raised when a session's status changes.</summary>
    event EventHandler<ClaudeCodeSession>? SessionStatusChanged;

    /// <summary>Raised when focus timer state changes.</summary>
    event EventHandler<bool>? FocusStateChanged;

    /// <summary>Raised when time scale changes.</summary>
    event EventHandler<TimeScale>? TimeScaleChanged;

    // Timeline Mode state

    /// <summary>Whether Timeline Mode is enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Enables Timeline Mode.</summary>
    void Enable();

    /// <summary>Disables Timeline Mode.</summary>
    void Disable();

    /// <summary>Gets the current timeline state.</summary>
    TimelineState GetState();

    // Time scale

    /// <summary>Gets the current time scale.</summary>
    TimeScale CurrentScale { get; }

    /// <summary>Sets the time scale.</summary>
    void SetTimeScale(TimeScale scale);

    // Intent management

    /// <summary>Creates a new intent with a git worktree.</summary>
    Task<Intent> CreateIntentAsync(string name, string branchName, string? parentRepoPath = null, string? contextFile = null);

    /// <summary>Creates an intent from an existing folder/worktree.</summary>
    Task<Intent> CreateIntentFromExistingFolderAsync(string name, string folderPath);

    /// <summary>Gets an intent by ID.</summary>
    Intent? GetIntent(string id);

    /// <summary>Gets all intents.</summary>
    List<Intent> GetAllIntents();

    /// <summary>Gets intents in display order.</summary>
    List<Intent> GetOrderedIntents();

    /// <summary>Gets all active intents.</summary>
    List<Intent> GetActiveIntents();

    /// <summary>Updates an intent.</summary>
    void UpdateIntent(Intent intent);

    /// <summary>Updates an intent's status.</summary>
    void UpdateIntentStatus(string intentId, IntentStatus status);

    /// <summary>Sets context for an intent.</summary>
    void SetIntentContext(string intentId, string? contextFilePath, string? contextContent);

    /// <summary>Deletes an intent.</summary>
    Task DeleteIntentAsync(string intentId, bool removeWorktree = false);

    /// <summary>Reorders an intent in the display list.</summary>
    void ReorderIntent(string intentId, int newIndex);

    /// <summary>Gets the current/selected intent.</summary>
    Intent? GetCurrentIntent();

    /// <summary>Sets the current/selected intent.</summary>
    void SetCurrentIntent(string? intentId);

    // Session management

    /// <summary>Starts a new session for an intent.</summary>
    ClaudeCodeSession StartSession(string intentId, string? parentSessionId = null);

    /// <summary>Forks from an existing session.</summary>
    Task<ClaudeCodeSession> ForkSessionAsync(string sessionId);

    /// <summary>Gets a session by ID.</summary>
    ClaudeCodeSession? GetSession(string id);

    /// <summary>Gets all sessions.</summary>
    List<ClaudeCodeSession> GetAllSessions();

    /// <summary>Gets all sessions for an intent.</summary>
    List<ClaudeCodeSession> GetSessionsForIntent(string intentId);

    /// <summary>Gets all running sessions.</summary>
    List<ClaudeCodeSession> GetRunningSessions();

    /// <summary>Updates a session.</summary>
    void UpdateSession(ClaudeCodeSession session);

    /// <summary>Marks a session as successful.</summary>
    void MarkSessionSuccess(string sessionId, string? commitHash = null, string? commitMessage = null);

    /// <summary>Marks a session as failed.</summary>
    void MarkSessionFailed(string sessionId);

    /// <summary>Marks a session as abandoned.</summary>
    void MarkSessionAbandoned(string sessionId);

    /// <summary>Adds a file change to a session.</summary>
    void AddFileChange(string sessionId, string filePath, int additions = 0, int deletions = 0);

    /// <summary>Adds a command to a session.</summary>
    void AddCommand(string sessionId, string command);

    /// <summary>Sets the Claude Code continue session ID.</summary>
    void SetContinueSessionId(string sessionId, string continueSessionId);

    // Cherry-pick

    /// <summary>Cherry-picks changes from one session to another intent.</summary>
    Task<(bool Success, string? Error)> CherryPickAsync(string sessionId, string targetIntentId);

    // Focus time tracking

    /// <summary>Whether the focus timer is running.</summary>
    bool IsFocusing { get; }

    /// <summary>Starts the focus timer.</summary>
    void StartFocusTimer();

    /// <summary>Pauses the focus timer.</summary>
    void PauseFocusTimer();

    /// <summary>Resets the accumulated focus time.</summary>
    void ResetFocusTime();

    /// <summary>Gets the total accumulated focus time.</summary>
    TimeSpan GetTotalFocusTime();

    /// <summary>Gets the current focus time including active session.</summary>
    TimeSpan GetCurrentFocusTime();

    // Hook event processing

    /// <summary>Handles a session start event from Claude Code hooks.</summary>
    Task HandleSessionStartAsync(string sessionId, string cwd, string? transcriptPath);

    /// <summary>Handles a file changed event from Claude Code hooks.</summary>
    Task HandleFileChangedAsync(string sessionId, string filePath);

    /// <summary>Handles a session stop event from Claude Code hooks.</summary>
    Task HandleSessionStopAsync(string sessionId);

    /// <summary>Assigns orphan sessions to an intent when it's created.</summary>
    List<ClaudeCodeSession> AssignOrphansToIntent(string intentId, string cwd);
}
