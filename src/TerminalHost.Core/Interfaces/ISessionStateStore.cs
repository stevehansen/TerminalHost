using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Durable timeline state: enabled flag, intents, focus time, persistence.
/// Owns the only writer for <see cref="TimelineState"/> on disk and is the
/// single source of truth for "which intents exist and which one is current".
/// Hides config-file layout, focus-time math, and worktree provisioning.
/// </summary>
public interface ISessionStateStore
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
    TimelineState GetState();

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

    /// <summary>
    /// Resolves the intent (if any) whose worktree path matches the given working
    /// directory. Path comparison is canonicalized (full path, trimmed separators,
    /// case-insensitive on the host filesystem semantics).
    /// </summary>
    Intent? FindIntentByWorkingDirectory(string workingDirectory);

    /// <summary>
    /// Returns an intent for the given working directory, creating one if none
    /// exists. Used by the live tracker to attach incoming hook sessions to an
    /// intent without duplicating the lookup-or-create dance.
    /// </summary>
    Intent EnsureIntentForWorkingDirectory(string cwd, string displayName);

    TimeSpan GetTotalFocusTime();
    TimeSpan GetCurrentFocusTime();
    bool IsFocusing { get; }
    void StartFocusTimer();
    void PauseFocusTimer();
    void ResetFocusTime();

    Task SaveAsync();
    Task LoadAsync();

    event EventHandler<bool>? EnabledChanged;
    event EventHandler? IntentsChanged;
    event EventHandler<Intent?>? CurrentIntentChanged;
    event EventHandler<bool>? FocusStateChanged;
}
