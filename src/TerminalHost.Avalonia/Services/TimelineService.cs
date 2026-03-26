using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Stub implementation of the Timeline Mode service for macOS/Avalonia.
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
        IGitProcessRunner gitRunner,
        IFileSystem fileSystem,
        IClaudeTaskFileService? taskFileService = null,
        IClaudeSessionIndexService? sessionIndexService = null,
        string? userDataDir = null)
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
    public event EventHandler<bool>? FocusStateChanged;
    public event EventHandler? LiveSessionsChanged;
#pragma warning disable CS0067
    public event EventHandler<(string WorktreePath, string? InitialPrompt)>? OpenProjectRequested;
#pragma warning restore CS0067

    // Timeline state
    public bool IsEnabled { get { lock (_lock) return _state.Enabled; } }

    public void Enable()
    {
        lock (_lock) { _state.Enabled = true; SaveState(); }
        EnabledChanged?.Invoke(this, true);
    }

    public void Disable()
    {
        lock (_lock) { _state.Enabled = false; SaveState(); }
        EnabledChanged?.Invoke(this, false);
    }

    public TimelineState GetState() { lock (_lock) return _state; }

    // Intent management
    public Task<Intent?> CreateIntentAsync(string name, string branchName, string mainRepoPath, string? baseBranch = null, string? context = null)
    {
        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, "", mainRepoPath);
            _state.Intents.Add(intent);
            _state.IntentOrder.Add(intent.Id);
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
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
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(intent);
    }

    public Intent? GetIntent(string id) { lock (_lock) return _state.GetIntent(id); }
    public IReadOnlyList<Intent> GetAllIntents() { lock (_lock) return [.. _state.Intents]; }
    public IReadOnlyList<Intent> GetOrderedIntents()
    {
        lock (_lock) return _state.GetOrderedIntents().ToList();
    }
    public IReadOnlyList<Intent> GetActiveIntents()
    {
        lock (_lock) return _state.Intents.Where(i => i.Status == IntentStatus.Active).ToList();
    }
    public void UpdateIntent(Intent intent)
    {
        lock (_lock) { SaveState(); }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void UpdateIntentStatus(string intentId, IntentStatus status)
    {
        lock (_lock)
        {
            var intent = _state.GetIntent(intentId);
            if (intent != null) { intent.Status = status; SaveState(); }
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetIntentContext(string intentId, string? context) { }
    public Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false)
    {
        lock (_lock) { _state.RemoveIntent(intentId); SaveState(); }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }
    public void ReorderIntent(string intentId, int newIndex)
    {
        lock (_lock)
        {
            _state.IntentOrder.Remove(intentId);
            _state.IntentOrder.Insert(Math.Clamp(newIndex, 0, _state.IntentOrder.Count), intentId);
            SaveState();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }
    public Intent? GetCurrentIntent()
    {
        lock (_lock) return _state.CurrentIntentId != null ? _state.GetIntent(_state.CurrentIntentId) : null;
    }
    public void SetCurrentIntent(string? intentId)
    {
        lock (_lock) { _state.CurrentIntentId = intentId; SaveState(); }
        CurrentIntentChanged?.Invoke(this, intentId != null ? _state.GetIntent(intentId) : null);
    }

    // Live sessions (stub)
    public IReadOnlyList<LiveSession> GetLiveSessions() => [];
    public LiveSession? GetLiveSessionByClaudeId(string claudeSessionId) => null;

    // Focus time
    public bool IsFocusing { get { lock (_lock) return _state.IsFocusing; } }
    public void StartFocusTimer()
    {
        lock (_lock) { _state.StartFocus(); SaveState(); }
        FocusStateChanged?.Invoke(this, true);
    }
    public void PauseFocusTimer()
    {
        lock (_lock) { _state.PauseFocus(); SaveState(); }
        FocusStateChanged?.Invoke(this, false);
    }
    public void ResetFocusTime()
    {
        lock (_lock) { _state.ResetFocusTime(); SaveState(); }
    }
    public TimeSpan GetTotalFocusTime() { lock (_lock) return _state.TotalFocusTime; }
    public TimeSpan GetCurrentFocusTime() { lock (_lock) return _state.CurrentFocusTime; }

    // Hook handling (stub)
    public void HandleSessionStart(HookEvent hookEvent) { }
    public void HandleFileChanged(HookEvent hookEvent) { }
    public Task HandleSessionStopAsync(HookEvent hookEvent) => Task.CompletedTask;
    public Intent? FindIntentByWorkingDirectory(string workingDirectory)
    {
        lock (_lock) return _state.Intents.FirstOrDefault(i =>
            string.Equals(i.WorktreePath, workingDirectory, StringComparison.OrdinalIgnoreCase));
    }
    public void HandleToolStart(HookEvent hookEvent) { }
    public void HandleToolEnd(HookEvent hookEvent) { }
    public void StartInactivityTimer() { }
    public void StopInactivityTimer() { }
    public bool AreHooksInstalled() => false;
    public bool InstallHooks() => false;
    public bool UninstallHooks() => false;
    public void UpgradeHooksIfNeeded() { }

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
    public Task SaveAsync() { SaveState(); return Task.CompletedTask; }
    public Task LoadAsync() { lock (_lock) _state = LoadState(); return Task.CompletedTask; }
}
