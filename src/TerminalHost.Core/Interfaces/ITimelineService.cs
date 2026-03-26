using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for managing Timeline IDE state, intents, and live Claude Code sessions.
/// Historical session data comes from IClaudeSessionIndexService.
/// </summary>
public interface ITimelineService
{
    #region Timeline State

    bool IsEnabled { get; }
    void Enable();
    void Disable();
    TimelineState GetState();

    #endregion

    #region Intent Management

    Task<Intent?> CreateIntentAsync(string name, string branchName, string mainRepoPath, string? baseBranch = null, string? context = null);
    Task<Intent> CreateIntentFromExistingFolderAsync(string name, string existingFolderPath, string? context = null);
    Intent? GetIntent(string intentId);
    IReadOnlyList<Intent> GetAllIntents();
    IReadOnlyList<Intent> GetOrderedIntents();
    IReadOnlyList<Intent> GetActiveIntents();
    void UpdateIntent(Intent intent);
    void UpdateIntentStatus(string intentId, IntentStatus status);
    void SetIntentContext(string intentId, string? context);
    Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false);
    void ReorderIntent(string intentId, int newIndex);
    Intent? GetCurrentIntent();
    void SetCurrentIntent(string? intentId);

    #endregion

    #region Live Sessions

    /// <summary>
    /// Gets all currently tracked live sessions (active + recently ended).
    /// </summary>
    IReadOnlyList<LiveSession> GetLiveSessions();

    /// <summary>
    /// Gets a live session by its Claude Code session ID.
    /// </summary>
    LiveSession? GetLiveSessionByClaudeId(string claudeSessionId);

    #endregion

    #region Focus Time

    TimeSpan GetTotalFocusTime();
    TimeSpan GetCurrentFocusTime();
    bool IsFocusing { get; }
    void StartFocusTimer();
    void PauseFocusTimer();
    void ResetFocusTime();

    #endregion

    #region Persistence

    Task SaveAsync();
    Task LoadAsync();

    #endregion

    #region Hook Event Handling

    void HandleSessionStart(HookEvent hookEvent);
    void HandleFileChanged(HookEvent hookEvent);
    Task HandleSessionStopAsync(HookEvent hookEvent);
    Intent? FindIntentByWorkingDirectory(string workingDirectory);
    void HandleToolStart(HookEvent hookEvent);
    void HandleToolEnd(HookEvent hookEvent);
    void StartInactivityTimer();
    void StopInactivityTimer();
    bool AreHooksInstalled();
    bool InstallHooks();
    bool UninstallHooks();

    /// <summary>
    /// Checks if hooks are partially installed (old version) and upgrades them to the full set.
    /// Called on startup to auto-upgrade from 4-hook to 9-hook configuration.
    /// </summary>
    void UpgradeHooksIfNeeded();

    #endregion

    #region Events

    event EventHandler<bool>? EnabledChanged;
    event EventHandler? IntentsChanged;
    event EventHandler<Intent?>? CurrentIntentChanged;
    event EventHandler<bool>? FocusStateChanged;
    event EventHandler<(string WorktreePath, string? InitialPrompt)>? OpenProjectRequested;

    /// <summary>
    /// Fired when live sessions change (added, removed, or status changed).
    /// </summary>
    event EventHandler? LiveSessionsChanged;

    #endregion
}
