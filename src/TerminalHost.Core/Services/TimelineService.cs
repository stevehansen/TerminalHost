using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Thin facade over <see cref="ISessionStateStore"/>, <see cref="ILiveSessionTracker"/>,
/// and <see cref="IHookInstaller"/>. Exists to keep ViewModel ergonomics — callers
/// see one cohesive surface, while the internals are split into deep modules with
/// independent responsibilities and testable boundaries.
/// </summary>
public sealed class TimelineService : ITimelineService, IDisposable
{
    private readonly ISessionStateStore _stateStore;
    private readonly ILiveSessionTracker _liveTracker;
    private readonly IHookInstaller? _hookInstaller;
    private readonly bool _ownsLiveTracker;

    // Constructor is internal because ILiveSessionTracker is an internal interface.
    // The class itself stays public (DI registers by interface); tests and Core
    // construct instances via InternalsVisibleTo.
    internal TimelineService(
        ISessionStateStore stateStore,
        ILiveSessionTracker liveTracker,
        IHookInstaller? hookInstaller = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _liveTracker = liveTracker ?? throw new ArgumentNullException(nameof(liveTracker));
        _hookInstaller = hookInstaller;
        _ownsLiveTracker = false;

        _stateStore.EnabledChanged += OnEnabledChanged;
        _stateStore.IntentsChanged += OnIntentsChanged;
        _stateStore.CurrentIntentChanged += OnCurrentIntentChanged;
        _stateStore.FocusStateChanged += OnFocusStateChanged;
    }

    public event EventHandler<bool>? EnabledChanged;
    public event EventHandler? IntentsChanged;
    public event EventHandler<Intent?>? CurrentIntentChanged;
    public event EventHandler<bool>? FocusStateChanged;
    public event EventHandler<(string WorktreePath, string? InitialPrompt)>? OpenProjectRequested;

    private void OnEnabledChanged(object? sender, bool enabled) => EnabledChanged?.Invoke(this, enabled);
    private void OnIntentsChanged(object? sender, EventArgs e) => IntentsChanged?.Invoke(this, e);
    private void OnCurrentIntentChanged(object? sender, Intent? intent) => CurrentIntentChanged?.Invoke(this, intent);
    private void OnFocusStateChanged(object? sender, bool isFocusing) => FocusStateChanged?.Invoke(this, isFocusing);

    // Suppress unused-warning: kept on the interface for forward compatibility.
    private void TouchOpenProject() => OpenProjectRequested?.Invoke(this, default);

    public bool IsEnabled => _stateStore.IsEnabled;
    public void Enable() => _stateStore.Enable();
    public void Disable() => _stateStore.Disable();
    public TimelineState GetState() => _stateStore.GetState();

    public Task<Intent?> CreateIntentAsync(string name, string branchName, string mainRepoPath, string? baseBranch = null, string? context = null)
        => _stateStore.CreateIntentAsync(name, branchName, mainRepoPath, baseBranch, context);

    public Task<Intent> CreateIntentFromExistingFolderAsync(string name, string existingFolderPath, string? context = null)
        => _stateStore.CreateIntentFromExistingFolderAsync(name, existingFolderPath, context);

    public Intent? GetIntent(string intentId) => _stateStore.GetIntent(intentId);
    public IReadOnlyList<Intent> GetAllIntents() => _stateStore.GetAllIntents();
    public IReadOnlyList<Intent> GetOrderedIntents() => _stateStore.GetOrderedIntents();
    public IReadOnlyList<Intent> GetActiveIntents() => _stateStore.GetActiveIntents();
    public void UpdateIntent(Intent intent) => _stateStore.UpdateIntent(intent);
    public void UpdateIntentStatus(string intentId, IntentStatus status) => _stateStore.UpdateIntentStatus(intentId, status);
    public void SetIntentContext(string intentId, string? context) => _stateStore.SetIntentContext(intentId, context);
    public Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false) => _stateStore.DeleteIntentAsync(intentId, removeWorktree);
    public void ReorderIntent(string intentId, int newIndex) => _stateStore.ReorderIntent(intentId, newIndex);
    public Intent? GetCurrentIntent() => _stateStore.GetCurrentIntent();
    public void SetCurrentIntent(string? intentId) => _stateStore.SetCurrentIntent(intentId);
    public Intent? FindIntentByWorkingDirectory(string workingDirectory) => _stateStore.FindIntentByWorkingDirectory(workingDirectory);

    public IReadOnlyList<LiveSession> GetLiveSessions() => _liveTracker.GetLiveSessions();
    public LiveSession? GetLiveSessionByClaudeId(string claudeSessionId) => _liveTracker.GetLiveSessionByClaudeId(claudeSessionId);

    public TimeSpan GetTotalFocusTime() => _stateStore.GetTotalFocusTime();
    public TimeSpan GetCurrentFocusTime() => _stateStore.GetCurrentFocusTime();
    public bool IsFocusing => _stateStore.IsFocusing;
    public void StartFocusTimer() => _stateStore.StartFocusTimer();
    public void PauseFocusTimer() => _stateStore.PauseFocusTimer();
    public void ResetFocusTime() => _stateStore.ResetFocusTime();

    public Task SaveAsync() => _stateStore.SaveAsync();
    public Task LoadAsync() => _stateStore.LoadAsync();

    public bool AreHooksInstalled() => _hookInstaller?.AreHooksInstalled() ?? false;
    public bool InstallHooks() => _hookInstaller?.InstallHooks() ?? false;
    public bool UninstallHooks() => _hookInstaller?.UninstallHooks() ?? true;
    public void UpgradeHooksIfNeeded() => _hookInstaller?.UpgradeHooksIfNeeded();

    public void Dispose()
    {
        _stateStore.EnabledChanged -= OnEnabledChanged;
        _stateStore.IntentsChanged -= OnIntentsChanged;
        _stateStore.CurrentIntentChanged -= OnCurrentIntentChanged;
        _stateStore.FocusStateChanged -= OnFocusStateChanged;

        if (_ownsLiveTracker && _liveTracker is IDisposable disposable)
            disposable.Dispose();
    }
}
