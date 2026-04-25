using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Persistent timeline state: enabled flag, intents, focus time. Single writer
/// for <see cref="TimelineState"/> on disk.
/// </summary>
public sealed class SessionStateStore : ISessionStateStore
{
    private readonly IConfigurationService _configService;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitProcessRunner _gitRunner;
    private readonly object _lock = new();

    private TimelineState _state = new();

    public SessionStateStore(
        IConfigurationService configService,
        IGitWorktreeService worktreeService,
        IGitProcessRunner gitRunner)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _worktreeService = worktreeService ?? throw new ArgumentNullException(nameof(worktreeService));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        LoadFromConfig();
    }

    public event EventHandler<bool>? EnabledChanged;
    public event EventHandler? IntentsChanged;
    public event EventHandler<Intent?>? CurrentIntentChanged;
    public event EventHandler<bool>? FocusStateChanged;

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
        lock (_lock) { SaveToConfig(); }
        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        lock (_lock) { LoadFromConfig(); }
        return Task.CompletedTask;
    }

    public bool IsEnabled
    {
        get { lock (_lock) { return _state.Enabled; } }
    }

    public void Enable()
    {
        lock (_lock)
        {
            if (_state.Enabled) return;
            _state.Enabled = true;
            SaveToConfig();
        }
        EnabledChanged?.Invoke(this, true);
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (!_state.Enabled) return;
            _state.PauseFocus();
            _state.Enabled = false;
            SaveToConfig();
        }
        EnabledChanged?.Invoke(this, false);
    }

    public TimelineState GetState()
    {
        lock (_lock) { return _state; }
    }

    public async Task<Intent?> CreateIntentAsync(
        string name, string branchName, string mainRepoPath,
        string? baseBranch = null, string? context = null)
    {
        var parentDir = Path.GetDirectoryName(mainRepoPath);
        if (string.IsNullOrEmpty(parentDir)) return null;

        var repoName = Path.GetFileName(mainRepoPath);
        var safeBranchName = branchName.Replace("/", "-").Replace("\\", "-");
        var worktreePath = Path.Combine(parentDir, $"{repoName}-{safeBranchName}");

        var result = await _worktreeService.CreateWorktreeAsync(
            mainRepoPath, branchName, worktreePath, createBranch: true);

        if (!result.Success) return null;

        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, branchName, worktreePath, mainRepoPath);
            if (!string.IsNullOrEmpty(context))
                intent.ContextContent = context;
            _state.AddIntent(intent);
            SaveToConfig();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return intent;
    }

    public async Task<Intent> CreateIntentFromExistingFolderAsync(
        string name, string existingFolderPath, string? context = null)
    {
        string branchName = "main";
        try
        {
            var output = await _gitRunner.RunGitCommandAsync(existingFolderPath, "rev-parse --abbrev-ref HEAD");
            if (!string.IsNullOrWhiteSpace(output))
                branchName = output.Trim();
        }
        catch { }

        Intent intent;
        lock (_lock)
        {
            intent = Intent.Create(name, branchName, existingFolderPath, existingFolderPath);
            if (!string.IsNullOrEmpty(context))
                intent.ContextContent = context;
            _state.AddIntent(intent);
            SaveToConfig();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return intent;
    }

    public Intent? GetIntent(string intentId)
    {
        lock (_lock) { return _state.GetIntent(intentId); }
    }

    public IReadOnlyList<Intent> GetAllIntents()
    {
        lock (_lock) { return _state.Intents.ToList(); }
    }

    public IReadOnlyList<Intent> GetOrderedIntents()
    {
        lock (_lock) { return _state.GetOrderedIntents().ToList(); }
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
        bool currentChanged;
        lock (_lock)
        {
            var existing = _state.Intents.FirstOrDefault(i => i.Id == intent.Id);
            if (existing == null) return;
            var index = _state.Intents.IndexOf(existing);
            _state.Intents[index] = intent;
            SaveToConfig();
            currentChanged = _state.CurrentIntentId == intent.Id;
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged)
            CurrentIntentChanged?.Invoke(this, intent);
    }

    public void UpdateIntentStatus(string intentId, IntentStatus status)
    {
        Intent? intent;
        bool currentChanged;
        lock (_lock)
        {
            intent = _state.GetIntent(intentId);
            if (intent == null) return;
            intent.Status = status;
            if (status == IntentStatus.Completed || status == IntentStatus.Abandoned)
                intent.CompletedAt = DateTime.UtcNow;
            SaveToConfig();
            currentChanged = _state.CurrentIntentId == intentId;
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged)
            CurrentIntentChanged?.Invoke(this, intent);
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
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> DeleteIntentAsync(string intentId, bool removeWorktree = false)
    {
        Intent? intent;
        lock (_lock)
        {
            intent = _state.GetIntent(intentId);
            if (intent == null) return false;
        }

        if (removeWorktree && !string.IsNullOrEmpty(intent.WorktreePath))
        {
            try { await _worktreeService.RemoveWorktreeAsync(intent.WorktreePath, force: true); }
            catch { }
        }

        bool wasCurrent;
        lock (_lock)
        {
            wasCurrent = _state.CurrentIntentId == intentId;
            _state.RemoveIntent(intentId);
            SaveToConfig();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        if (wasCurrent)
            CurrentIntentChanged?.Invoke(this, null);
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
                _state.IntentOrder.Add(intentId);
            else
                _state.IntentOrder.Insert(Math.Max(0, newIndex), intentId);
            SaveToConfig();
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Intent? GetCurrentIntent()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_state.CurrentIntentId)) return null;
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
        CurrentIntentChanged?.Invoke(this, intent);
    }

    public Intent? FindIntentByWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return null;
        lock (_lock) { return FindIntentByWorkingDirectoryLocked(workingDirectory); }
    }

    public Intent EnsureIntentForWorkingDirectory(string cwd, string displayName)
    {
        if (string.IsNullOrEmpty(cwd))
            throw new ArgumentException("Working directory required", nameof(cwd));

        var existing = FindIntentByWorkingDirectory(cwd);
        if (existing != null) return existing;

        Intent intent;
        lock (_lock)
        {
            // Re-check under lock to avoid duplicate creation under contention
            intent = FindIntentByWorkingDirectoryLocked(cwd) ?? Intent.Create(displayName, "", cwd, cwd);
            if (!_state.Intents.Contains(intent))
            {
                _state.Intents.Add(intent);
                _state.IntentOrder.Add(intent.Id);
                SaveToConfig();
            }
        }
        IntentsChanged?.Invoke(this, EventArgs.Empty);
        return intent;
    }

    private Intent? FindIntentByWorkingDirectoryLocked(string workingDirectory)
    {
        var normalizedCwd = NormalizeWorkingDirectory(workingDirectory);
        return _state.Intents.FirstOrDefault(intent =>
        {
            if (string.IsNullOrEmpty(intent.WorktreePath)) return false;
            var normalizedWorktree = NormalizeWorkingDirectory(intent.WorktreePath);
            return string.Equals(normalizedCwd, normalizedWorktree, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string NormalizeWorkingDirectory(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // Container-native Linux path on a Windows host
            return path.TrimEnd('/', '\\');
        }
    }

    public TimeSpan GetTotalFocusTime()
    {
        lock (_lock) { return _state.TotalFocusTime; }
    }

    public TimeSpan GetCurrentFocusTime()
    {
        lock (_lock) { return _state.CurrentFocusTime; }
    }

    public bool IsFocusing
    {
        get { lock (_lock) { return _state.IsFocusing; } }
    }

    public void StartFocusTimer()
    {
        lock (_lock)
        {
            if (_state.IsFocusing) return;
            _state.StartFocus();
            SaveToConfig();
        }
        FocusStateChanged?.Invoke(this, true);
    }

    public void PauseFocusTimer()
    {
        lock (_lock)
        {
            if (!_state.IsFocusing) return;
            _state.PauseFocus();
            SaveToConfig();
        }
        FocusStateChanged?.Invoke(this, false);
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
        if (wasFocusing) FocusStateChanged?.Invoke(this, false);
    }
}
