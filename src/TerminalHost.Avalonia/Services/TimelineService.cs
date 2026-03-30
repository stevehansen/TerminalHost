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
    private readonly IFileSystem _fileSystem;
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
        _fileSystem = fileSystem;
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

    // Live sessions
    private readonly Dictionary<string, LiveSession> _liveSessions = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LiveSession> GetLiveSessions()
    {
        lock (_lock) return [.. _liveSessions.Values];
    }

    public LiveSession? GetLiveSessionByClaudeId(string claudeSessionId)
    {
        lock (_lock) return _liveSessions.GetValueOrDefault(claudeSessionId);
    }

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

    // Hook handling
    public void HandleSessionStart(HookEvent hookEvent)
    {
        var sessionId = hookEvent.SessionId;
        if (string.IsNullOrEmpty(sessionId)) return;

        lock (_lock)
        {
            if (!_liveSessions.ContainsKey(sessionId))
            {
                _liveSessions[sessionId] = new LiveSession
                {
                    ClaudeSessionId = sessionId,
                    WorkingDirectory = hookEvent.Cwd,
                    TranscriptPath = hookEvent.TranscriptPath,
                    StartTime = DateTime.UtcNow,
                    LastActivityTime = DateTime.UtcNow
                };
            }
        }
        LiveSessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HandleFileChanged(HookEvent hookEvent)
    {
        var sessionId = hookEvent.SessionId;
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(sessionId, out var session))
            {
                session.LastActivityTime = DateTime.UtcNow;
                session.HadFileWrites = true;
            }
        }
    }

    public Task HandleSessionStopAsync(HookEvent hookEvent)
    {
        var sessionId = hookEvent.SessionId;
        if (string.IsNullOrEmpty(sessionId)) return Task.CompletedTask;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(sessionId, out var session))
            {
                session.EndTime = DateTime.UtcNow;
                session.EndReason = "explicit";
            }
        }
        LiveSessionsChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
    public Intent? FindIntentByWorkingDirectory(string workingDirectory)
    {
        lock (_lock) return _state.Intents.FirstOrDefault(i =>
            string.Equals(i.WorktreePath, workingDirectory, StringComparison.OrdinalIgnoreCase));
    }
    public void HandleToolStart(HookEvent hookEvent)
    {
        TouchSession(hookEvent.SessionId);
    }

    public void HandleToolEnd(HookEvent hookEvent)
    {
        TouchSession(hookEvent.SessionId);
        if (hookEvent.EventType == HookEventType.ToolError)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(hookEvent.SessionId) && _liveSessions.TryGetValue(hookEvent.SessionId, out var session))
                    session.HadErrors = true;
            }
        }
    }

    private void TouchSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_lock)
        {
            if (_liveSessions.TryGetValue(sessionId, out var session))
                session.LastActivityTime = DateTime.UtcNow;
        }
    }
    public void StartInactivityTimer() { }
    public void StopInactivityTimer() { }

    public bool AreHooksInstalled()
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            if (!_fileSystem.FileExists(settingsPath)) return false;
            var json = _fileSystem.ReadAllText(settingsPath);
            return json.Contains("/api/hooks/session-start", StringComparison.OrdinalIgnoreCase)
                && json.Contains("/api/hooks/subagent-start", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public bool InstallHooks()
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            var dir = Path.GetDirectoryName(settingsPath);
            if (dir != null && !_fileSystem.DirectoryExists(dir))
                _fileSystem.CreateDirectory(dir);

            System.Text.Json.Nodes.JsonObject root;
            if (_fileSystem.FileExists(settingsPath))
            {
                var existingJson = _fileSystem.ReadAllText(settingsPath);
                root = System.Text.Json.Nodes.JsonNode.Parse(existingJson)?.AsObject()
                    ?? new System.Text.Json.Nodes.JsonObject();
            }
            else
            {
                root = new System.Text.Json.Nodes.JsonObject();
            }

            // Read API port from config (default 19280)
            var config = _configService.Load();
            var apiPort = config.Settings.Api.Port;
            var apiUrl = $"http://127.0.0.1:{apiPort}/api/hooks";

            // Use curl to POST stdin JSON directly to the API server — no .NET process startup
            var hooks = root["hooks"]?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
            root["hooks"] = hooks;

            hooks["SessionStart"] = CreateCurlHookArray($"{apiUrl}/session-start", 10);
            hooks["Stop"] = CreateCurlHookArray($"{apiUrl}/session-stop", 10);
            hooks["SessionEnd"] = CreateCurlHookArray($"{apiUrl}/session-end", 10);
            hooks["PreToolUse"] = CreateCurlHookArray($"{apiUrl}/tool-start", 5);
            hooks["PostToolUse"] = CreateCurlHookArray($"{apiUrl}/tool-end", 5);
            hooks["PostToolUseFailure"] = CreateCurlHookArray($"{apiUrl}/tool-error", 5);
            hooks["SubagentStart"] = CreateCurlHookArray($"{apiUrl}/subagent-start", 5);
            hooks["SubagentStop"] = CreateCurlHookArray($"{apiUrl}/subagent-stop", 5);
            hooks["Notification"] = CreateCurlHookArray($"{apiUrl}/notification", 5);

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            _fileSystem.WriteAllText(settingsPath, root.ToJsonString(options));
            return true;
        }
        catch { return false; }
    }

    public bool UninstallHooks()
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            if (!_fileSystem.FileExists(settingsPath)) return true;

            var existingJson = _fileSystem.ReadAllText(settingsPath);
            var root = System.Text.Json.Nodes.JsonNode.Parse(existingJson)?.AsObject();
            if (root == null) return true;

            var hooks = root["hooks"]?.AsObject();
            if (hooks == null) return true;

            string[] hookNames = ["SessionStart", "Stop", "SessionEnd", "PreToolUse", "PostToolUse", "PostToolUseFailure", "SubagentStart", "SubagentStop", "Notification"];
            foreach (var name in hookNames)
                hooks.Remove(name);

            if (hooks.Count == 0)
                root.Remove("hooks");

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            _fileSystem.WriteAllText(settingsPath, root.ToJsonString(options));
            return true;
        }
        catch { return false; }
    }

    public void UpgradeHooksIfNeeded()
    {
        try
        {
            if (!AreHooksInstalled()) return;
            var settingsPath = GetClaudeSettingsPath();
            var json = _fileSystem.ReadAllText(settingsPath);

            // Detect old host-based hooks or missing hook types — force upgrade to curl
            var needsUpgrade = json.Contains("--hook session-start", StringComparison.OrdinalIgnoreCase)
                || !json.Contains("/api/hooks/subagent-start", StringComparison.OrdinalIgnoreCase)
                || !json.Contains("/api/hooks/notification", StringComparison.OrdinalIgnoreCase);
            if (needsUpgrade)
                InstallHooks();
        }
        catch { }
    }

    private static System.Text.Json.Nodes.JsonArray CreateCurlHookArray(string url, int timeout)
    {
        // curl reads stdin via -d @-, POSTs as JSON, fails silently on connection error
        var command = $"curl -sS -X POST -H 'Content-Type: application/json' -d @- '{url}'";
        return new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["hooks"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = command,
                        ["timeout"] = timeout,
                        ["async"] = true
                    }
                }
            }
        };
    }

    private static string GetClaudeSettingsPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", "settings.json");
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
    public Task SaveAsync() { SaveState(); return Task.CompletedTask; }
    public Task LoadAsync() { lock (_lock) _state = LoadState(); return Task.CompletedTask; }
}
