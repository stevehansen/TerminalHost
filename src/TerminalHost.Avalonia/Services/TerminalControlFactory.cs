using System.IO;
using Avalonia.Threading;
using TerminalHost.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Factory for creating terminal controls using VtNetCore and MacPtyService.
/// </summary>
internal sealed class TerminalControlFactory : ITerminalControlFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogService;
    private readonly ISystemInfoService _systemInfoService;
    private readonly IConfigurationService _configurationService;
    private readonly IContainerService _containerService;
    private readonly IProcessService _processService;
    private readonly ICommandComposer _composer;

    private static int _codexMcpCheckedFlag;

    public TerminalControlFactory(
        IFileSystem fileSystem,
        IDialogService dialogService,
        ISystemInfoService systemInfoService,
        IConfigurationService configurationService,
        IContainerService containerService,
        IProcessService processService,
        ICommandComposer composer)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _systemInfoService = systemInfoService;
        _configurationService = configurationService;
        _containerService = containerService;
        _processService = processService;
        _composer = composer;
    }

    public async Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = GetCommand(profile);
        var startupCommand = profile.StartupCommand;

        // Containerized session: use docker exec instead of local command
        if (!string.IsNullOrEmpty(profile.ContainerName))
        {
            command = BuildContainerCommand(profile.ContainerName, workingDir, command);
        }
        else
        {
            // Verify command exists (only for local commands; container commands resolve inside the container)
            var commandHead = command.Split(' ')[0];
            if (!_composer.TryResolveExecutable(commandHead, out _) && !_composer.IsBuiltInShell(commandHead))
            {
                await ShowCommandWarningAsync(command);
                command = _systemInfoService.GetDefaultShell();
            }

            // Register MCP collab server for any AI agent (HTTP transport, universal)
            // Only for non-shell commands; the shell early-out matches the WPF BuildLocalCommand path.
            var commandExeForMcp = command.Split(' ')[0];
            if (!_composer.IsBuiltInShell(commandExeForMcp))
            {
                EnsureMcpCollabRegistered();
                EnsureCodexMcpCollabRegistered();
            }

            // Append channel flags for Claude Code with channels enabled
            command = AppendChannelFlags(command, workingDir);
        }

        // Ensure working directory exists
        if (string.IsNullOrEmpty(workingDir) || !_fileSystem.DirectoryExists(workingDir))
        {
            workingDir = _systemInfoService.GetUserHomePath();
        }

        // Get custom paths from configuration
        var customPaths = _configurationService.Load().Settings.CustomPaths;

#if WINDOWS
        // EasyTerminalControl spawns the command via cmd.exe under the hood and has no separate
        // working-directory parameter, so fold the cd into the command string up front.
        var windowsCommand = _composer.WithWorkingDirectory(command, workingDir);
        var windowsControl = new TerminalHost.Controls.WindowsTerminalControl();
        await windowsControl.InitializeAsync(windowsCommand, workingDir, customPaths);
        ITerminalControl control = windowsControl;
#else
        var control = new MacTerminalControl();
        await control.InitializeAsync(command, workingDir, customPaths);
#endif

        // If there's a startup command, send it after the shell has initialized
        if (!string.IsNullOrEmpty(startupCommand))
        {
            // Schedule the startup command to be sent after a short delay
            // This gives the shell time to initialize and print its prompt
            _ = SendStartupCommandAsync(control, startupCommand);
        }

        return control;
    }

    /// <summary>
    /// Sends a startup command to the terminal after ensuring the shell is ready.
    /// This allows the shell to fully initialize and print its prompt before the command is sent.
    /// </summary>
    private static async Task SendStartupCommandAsync(ITerminalControl control, string command)
    {
        // Wait for shell to fully initialize and print its prompt
        await Task.Delay(1000);

        // Check if the terminal process is actually running
        if (!control.IsProcessRunning)
        {
            // Process not running yet, wait a bit more and retry
            await Task.Delay(1000);
        }

        if (control.IsProcessRunning)
        {
            // Send the command with a newline to execute it
            control.WriteToTerminal(command + "\r");
        }
    }

    private string GetCommand(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Command))
        {
            return _systemInfoService.GetDefaultShell();
        }

        // Expand environment variables
        return Environment.ExpandEnvironmentVariables(profile.Command);
    }

    private async Task ShowCommandWarningAsync(string command)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dialogService.ShowWarning(
                $"Command not found: {command}\n\nFalling back to default shell.",
                "Terminal Warning");
        });
    }

    /// <summary>
    /// Build a docker exec command for running inside a container.
    /// </summary>
    private string BuildContainerCommand(string containerName, string workspaceDir, string command)
    {
        // For shell profiles (zsh, bash, sh, etc.), launch bash inside the container
        var commandExe = command.Split(' ')[0];
        if (_composer.IsBuiltInShell(commandExe))
        {
            return _containerService.BuildExecCommand(containerName, workspaceDir, "/bin/bash");
        }

        // For AI assistants and other commands, extract just the binary name
        // (the host path like ~/.local/bin/claude doesn't exist in the container)
        var binaryName = Path.GetFileNameWithoutExtension(
            Environment.ExpandEnvironmentVariables(commandExe));

        // Pass --dangerously-skip-permissions for Claude Code in containers
        // (container runs as non-root 'developer' user, container itself is the sandbox)
        string? extraArgs = null;
        if (_containerService.IsAutoApproveEnabled &&
            binaryName.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            extraArgs = "--dangerously-skip-permissions";
        }

        return _containerService.BuildExecCommand(containerName, workspaceDir, binaryName, extraArgs);
    }

    /// <summary>
    /// If channels are enabled and the command is Claude Code, append the channel server flags
    /// and set up environment variables for the channel server.
    /// On macOS, environment variables are prefixed inline: VAR=val command args
    /// </summary>
    private string AppendChannelFlags(string command, string workingDir)
    {
        try
        {
            var commandExe = command.Split(' ')[0];
            var binaryName = Path.GetFileNameWithoutExtension(
                Environment.ExpandEnvironmentVariables(commandExe));

            // Only add channel flags for Claude Code
            if (!binaryName.Equals("claude", StringComparison.OrdinalIgnoreCase))
                return command;

            var config = _configurationService.Load();
            var channelSettings = config.Settings.Channel;
            if (!channelSettings.Enabled)
                return command;

            // Resolve channel server path
            var channelServerPath = ResolveChannelServerPath(channelSettings);
            if (string.IsNullOrEmpty(channelServerPath))
                return command;

            // Register .mcp.json for this project if auto-register is enabled
            if (channelSettings.AutoRegisterMcp && !string.IsNullOrEmpty(workingDir))
            {
                EnsureMcpJsonRegistered(workingDir, channelServerPath, channelSettings);
            }

            // Build the channel flag
            var channelFlag = channelSettings.UseDevelopmentFlag
                ? "--dangerously-load-development-channels server:terminalhost"
                : "--channels server:terminalhost";

            // Set environment variables for the channel server
            var apiSettings = config.Settings.Api;
            var apiUrl = $"http://{(apiSettings.BindAddress == "0.0.0.0" ? "127.0.0.1" : apiSettings.BindAddress)}:{apiSettings.Port}";
            var eventFilters = string.Join(",", channelSettings.EventFilters);

            // Compose env vars in a platform-correct way (inline on POSIX, set/&& chain on Windows cmd)
            var envVars = new Dictionary<string, string>
            {
                ["TERMINALHOST_API_URL"] = apiUrl,
                ["TERMINALHOST_EVENTS"] = eventFilters,
            };
            return _composer.WithEnvironment($"{command} {channelFlag}", envVars);
        }
        catch
        {
            // If anything goes wrong with channel setup, fall back to plain command
            return command;
        }
    }

    /// <summary>
    /// Resolves the path to the channel bridge executable (terminalhost-channel).
    /// </summary>
    private string? ResolveChannelServerPath(ChannelSettings channelSettings)
    {
        // Use explicit path if configured
        if (!string.IsNullOrEmpty(channelSettings.ChannelServerPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(channelSettings.ChannelServerPath);
            return _fileSystem.FileExists(expanded) ? expanded : null;
        }

        // Auto-detect: look for the C# channel bridge executable relative to application directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var exeName = "terminalhost-channel";
        var candidates = new[]
        {
            Path.Combine(appDir, exeName),
            Path.Combine(appDir, "terminalhost-channel", exeName),
            // Development: relative to the Avalonia project bin output
            Path.Combine(appDir, "..", "..", "..", "..", "TerminalHost.Channel", "bin", "Debug", "net8.0", exeName),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (_fileSystem.FileExists(fullPath))
                return fullPath;
        }

        return null;
    }

    /// <summary>
    /// Ensures the user's global Claude settings (~/.claude/settings.json) has the terminalhost channel server registered.
    /// Uses global settings instead of per-project .mcp.json to avoid polluting every workspace.
    /// </summary>
    private void EnsureMcpJsonRegistered(string workingDir, string channelServerPath, ChannelSettings channelSettings)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claudeDir = Path.Combine(home, ".claude");
            if (!Directory.Exists(claudeDir))
                return;

            var settingsPath = Path.Combine(claudeDir, "settings.json");

            Dictionary<string, object>? config = null;
            if (_fileSystem.FileExists(settingsPath))
            {
                var existing = _fileSystem.ReadAllText(settingsPath);
                config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(existing);
            }
            config ??= new Dictionary<string, object>();

            Dictionary<string, object>? mcpServers = null;
            if (config.TryGetValue("mcpServers", out var serversObj) && serversObj is System.Text.Json.JsonElement serversElement)
            {
                mcpServers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(serversElement.GetRawText());
            }
            mcpServers ??= new Dictionary<string, object>();

            if (mcpServers.ContainsKey("terminalhost"))
                return; // Already registered

            // Register the C# channel bridge executable directly (no runtime needed)
            mcpServers["terminalhost"] = new Dictionary<string, object>
            {
                ["command"] = channelServerPath.Replace("\\", "/")
            };

            config["mcpServers"] = mcpServers;

            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            _fileSystem.WriteAllText(settingsPath, json);
        }
        catch
        {
            // MCP registration is best-effort
        }
    }

    /// <summary>
    /// Ensures the user's global Claude config (~/.claude.json) has an HTTP-based
    /// terminalhost-collab entry that any MCP-capable AI agent can use.
    /// Uses MCP Streamable HTTP transport (type: http), which is the universal format supported by
    /// Claude Code, Gemini CLI, Codex CLI, and other modern AI agents.
    /// Uses global config instead of per-project .mcp.json to avoid polluting every workspace.
    /// </summary>
    private void EnsureMcpCollabRegistered()
    {
        try
        {
            var appConfig = _configurationService.Load();
            if (!appConfig.Settings.Api.Enabled)
                return;

            var apiSettings = appConfig.Settings.Api;
            var host = apiSettings.BindAddress == "0.0.0.0" ? "127.0.0.1" : apiSettings.BindAddress;
            var mcpUrl = $"http://{host}:{apiSettings.Port}/api/mcp";

            var settingsPath = GetClaudeSettingsPath();
            if (settingsPath == null) return;

            var config = ReadClaudeSettings(settingsPath);
            var mcpServers = GetOrCreateMcpServers(config);

            if (mcpServers.ContainsKey("terminalhost-collab"))
                return;

            var serverEntry = new Dictionary<string, object>
            {
                ["type"] = "http",
                ["url"] = mcpUrl
            };

            if (!string.IsNullOrEmpty(apiSettings.ApiKey))
            {
                serverEntry["headers"] = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {apiSettings.ApiKey}"
                };
            }

            mcpServers["terminalhost-collab"] = serverEntry;
            config["mcpServers"] = mcpServers;
            WriteClaudeSettings(settingsPath, config);
        }
        catch
        {
            // MCP registration is best-effort
        }
    }

    /// <summary>
    /// Ensures the Codex CLI's global config has terminalhost-collab registered as a streamable HTTP MCP server.
    /// Uses `codex mcp add` rather than editing ~/.codex/config.toml directly so Codex owns its schema.
    /// Skips silently if Codex isn't installed. Runs once per app session and is fire-and-forget.
    /// </summary>
    private void EnsureCodexMcpCollabRegistered()
    {
        if (Interlocked.Exchange(ref _codexMcpCheckedFlag, 1) == 1)
            return;

        try
        {
            var appConfig = _configurationService.Load();
            if (!appConfig.Settings.Api.Enabled)
                return;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(Path.Combine(home, ".codex")))
                return;

            var apiSettings = appConfig.Settings.Api;
            var host = apiSettings.BindAddress == "0.0.0.0" ? "127.0.0.1" : apiSettings.BindAddress;
            var mcpUrl = $"http://{host}:{apiSettings.Port}/api/mcp";

            // Codex's `mcp add` only takes --bearer-token-env-var, which is too intrusive to wire
            // up automatically. Skip the API-key case and let the user wire it up manually.
            if (!string.IsNullOrEmpty(apiSettings.ApiKey))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var (listExit, listOutput, _) = await _processService.RunAsync(
                        "codex", "mcp list", timeout: TimeSpan.FromSeconds(10));

                    if (listExit == 0 && listOutput.Contains("terminalhost-collab", StringComparison.Ordinal))
                        return;

                    await _processService.RunAsync(
                        "codex",
                        $"mcp add terminalhost-collab --url {mcpUrl}",
                        timeout: TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // Best-effort: codex CLI missing from PATH or other failures shouldn't disrupt terminal launch
                }
            });
        }
        catch
        {
            // Best-effort
        }
    }

    /// <summary>
    /// Returns the path to ~/.claude.json (the global Claude Code config file).
    /// Returns null if ~/.claude/ directory doesn't exist (Claude not installed).
    /// </summary>
    private static string? GetClaudeSettingsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeDir = Path.Combine(home, ".claude");
        if (!Directory.Exists(claudeDir))
            return null;
        return Path.Combine(home, ".claude.json");
    }

    private Dictionary<string, object> ReadClaudeSettings(string settingsPath)
    {
        if (_fileSystem.FileExists(settingsPath))
        {
            var existing = _fileSystem.ReadAllText(settingsPath);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(existing)
                   ?? new Dictionary<string, object>();
        }
        return new Dictionary<string, object>();
    }

    private static Dictionary<string, object> GetOrCreateMcpServers(Dictionary<string, object> config)
    {
        if (config.TryGetValue("mcpServers", out var serversObj) && serversObj is System.Text.Json.JsonElement serversElement)
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(serversElement.GetRawText())
                   ?? new Dictionary<string, object>();
        }
        return new Dictionary<string, object>();
    }

    private void WriteClaudeSettings(string settingsPath, Dictionary<string, object> config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        _fileSystem.WriteAllText(settingsPath, json);
    }

}
