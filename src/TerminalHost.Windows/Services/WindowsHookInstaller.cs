using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows hook installer — registers host.exe --hook commands in Claude Code's settings.json.
/// </summary>
public class WindowsHookInstaller : IHookInstaller
{
    private const string HookMarker = "--hook";
    private readonly IFileSystem _fileSystem;

    public WindowsHookInstaller(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public bool AreHooksInstalled()
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            if (!_fileSystem.FileExists(settingsPath)) return false;
            var json = _fileSystem.ReadAllText(settingsPath);
            return json.Contains("--hook session-start", StringComparison.OrdinalIgnoreCase)
                && json.Contains("--hook subagent-start", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void UpgradeHooksIfNeeded()
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            if (!_fileSystem.FileExists(settingsPath)) return;
            var json = _fileSystem.ReadAllText(settingsPath);

            var hasHooks = json.Contains("--hook session-start", StringComparison.OrdinalIgnoreCase);
            if (!hasHooks) return;

            var needsUpgrade = !json.Contains("--hook subagent-start", StringComparison.OrdinalIgnoreCase)
                || !json.Contains("\"async\"", StringComparison.OrdinalIgnoreCase);

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

            const string hostExe = "host.exe";
            var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
            root["hooks"] = hooks;

            MergeHookEntry(hooks, "SessionStart", $"{hostExe} --hook session-start", 10);
            MergeHookEntry(hooks, "Stop", $"{hostExe} --hook session-stop", 10);
            MergeHookEntry(hooks, "SessionEnd", $"{hostExe} --hook session-end", 10);
            MergeHookEntry(hooks, "PreToolUse", $"{hostExe} --hook tool-start", 5);
            MergeHookEntry(hooks, "PostToolUse", $"{hostExe} --hook tool-end", 5);
            MergeHookEntry(hooks, "PostToolUseFailure", $"{hostExe} --hook tool-error", 5);
            MergeHookEntry(hooks, "SubagentStart", $"{hostExe} --hook subagent-start", 5);
            MergeHookEntry(hooks, "SubagentStop", $"{hostExe} --hook subagent-stop", 5);
            MergeHookEntry(hooks, "Notification", $"{hostExe} --hook notification", 5);

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

            var keysToRemove = new List<string>();
            foreach (var kvp in hooks)
            {
                if (kvp.Value?.ToJsonString().Contains(HookMarker, StringComparison.OrdinalIgnoreCase) == true)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var key in keysToRemove)
                hooks.Remove(key);

            _fileSystem.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch { return false; }
    }

    private static void MergeHookEntry(JsonObject hooks, string eventName, string command, int timeout)
    {
        var existing = hooks[eventName]?.AsArray();
        if (existing == null)
        {
            hooks[eventName] = CreateHookArray(command, timeout);
            return;
        }

        for (int i = existing.Count - 1; i >= 0; i--)
        {
            if (existing[i]?.ToJsonString().Contains(HookMarker, StringComparison.OrdinalIgnoreCase) == true)
                existing.RemoveAt(i);
        }

        var newEntry = CreateHookArray(command, timeout);
        existing.Add(newEntry[0]!.DeepClone());
    }

    private static JsonArray CreateHookArray(string command, int timeout)
    {
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
