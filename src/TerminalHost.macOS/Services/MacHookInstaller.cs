using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.macOS.Services;

/// <summary>
/// macOS hook installer — registers curl commands in Claude Code's settings.json
/// that POST hook events to TerminalHost's REST API server.
/// </summary>
public class MacHookInstaller : IHookInstaller
{
    private const string HookMarker = "/api/hooks/";
    private readonly IFileSystem _fileSystem;
    private readonly IConfigurationService _configService;

    public MacHookInstaller(IFileSystem fileSystem, IConfigurationService configService)
    {
        _fileSystem = fileSystem;
        _configService = configService;
    }

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

    public bool InstallHooks()
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            var dir = Path.GetDirectoryName(settingsPath);
            if (dir != null && !_fileSystem.DirectoryExists(dir))
                _fileSystem.CreateDirectory(dir);

            JsonObject root;
            if (_fileSystem.FileExists(settingsPath))
            {
                var existingJson = _fileSystem.ReadAllText(settingsPath);
                root = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var config = _configService.Load();
            var apiPort = config.Settings.Api.Port;
            var apiUrl = $"http://127.0.0.1:{apiPort}/api/hooks";

            var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
            root["hooks"] = hooks;

            MergeHookEntry(hooks, "SessionStart", $"{apiUrl}/session-start", 10);
            MergeHookEntry(hooks, "Stop", $"{apiUrl}/session-stop", 10);
            MergeHookEntry(hooks, "SessionEnd", $"{apiUrl}/session-end", 10);
            MergeHookEntry(hooks, "PreToolUse", $"{apiUrl}/tool-start", 5);
            MergeHookEntry(hooks, "PostToolUse", $"{apiUrl}/tool-end", 5);
            MergeHookEntry(hooks, "PostToolUseFailure", $"{apiUrl}/tool-error", 5);
            MergeHookEntry(hooks, "SubagentStart", $"{apiUrl}/subagent-start", 5);
            MergeHookEntry(hooks, "SubagentStop", $"{apiUrl}/subagent-stop", 5);
            MergeHookEntry(hooks, "Notification", $"{apiUrl}/notification", 5);

            _fileSystem.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
            var root = JsonNode.Parse(existingJson)?.AsObject();
            if (root == null) return true;

            var hooks = root["hooks"]?.AsObject();
            if (hooks == null) return true;

            string[] hookNames = ["SessionStart", "Stop", "SessionEnd", "PreToolUse", "PostToolUse", "PostToolUseFailure", "SubagentStart", "SubagentStop", "Notification"];
            foreach (var name in hookNames)
            {
                var eventArray = hooks[name]?.AsArray();
                if (eventArray == null) continue;

                for (int i = eventArray.Count - 1; i >= 0; i--)
                {
                    if (eventArray[i]?.ToJsonString().Contains(HookMarker, StringComparison.OrdinalIgnoreCase) == true)
                        eventArray.RemoveAt(i);
                }

                if (eventArray.Count == 0)
                    hooks.Remove(name);
            }

            if (hooks.Count == 0)
                root.Remove("hooks");

            _fileSystem.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch { return false; }
    }

    private static void MergeHookEntry(JsonObject hooks, string eventName, string url, int timeout)
    {
        var existing = hooks[eventName]?.AsArray();
        if (existing == null)
        {
            hooks[eventName] = CreateCurlHookArray(url, timeout);
            return;
        }

        for (int i = existing.Count - 1; i >= 0; i--)
        {
            if (existing[i]?.ToJsonString().Contains(HookMarker, StringComparison.OrdinalIgnoreCase) == true)
                existing.RemoveAt(i);
        }

        var newEntry = CreateCurlHookArray(url, timeout);
        existing.Add(newEntry[0]!.DeepClone());
    }

    private static JsonArray CreateCurlHookArray(string url, int timeout)
    {
        var command = $"curl -sS -X POST -H 'Content-Type: application/json' -d @- '{url}'";
        return new JsonArray
        {
            new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject
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
}
