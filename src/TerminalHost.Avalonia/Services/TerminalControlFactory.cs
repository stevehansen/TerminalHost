using System.IO;
using Avalonia.Threading;
using TerminalHost.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
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
    private readonly ICommandComposer _composer;
    private readonly ParleyLaunchIntegration _parley;

    public TerminalControlFactory(
        IFileSystem fileSystem,
        IDialogService dialogService,
        ISystemInfoService systemInfoService,
        IConfigurationService configurationService,
        IContainerService containerService,
        ICommandComposer composer,
        ParleyLaunchIntegration parley)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _systemInfoService = systemInfoService;
        _configurationService = configurationService;
        _containerService = containerService;
        _composer = composer;
        _parley = parley;
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

            // AI commands (non-shell; matches the WPF BuildLocalCommand early-out): Parley session
            // env for any AI CLI, plus channel flags for Claude Code
            if (!_composer.IsBuiltInShell(command.Split(' ')[0]))
            {
                command = AppendAiSessionSetup(command, workingDir);
            }
        }

        // Ensure working directory exists
        if (string.IsNullOrEmpty(workingDir) || !_fileSystem.DirectoryExists(workingDir))
        {
            workingDir = _systemInfoService.GetUserHomePath();
        }

        // Get custom paths from configuration
        var customPaths = _configurationService.Load().Settings.CustomPaths;

        var control = new MacTerminalControl();

        await control.InitializeAsync(command, workingDir, customPaths);

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
    /// Prepares an AI command: Parley registration + session env (any AI CLI), and for Claude Code
    /// the channel servers to load (TerminalHost events and/or Parley push delivery).
    /// Environment variables are composed per platform (inline VAR=val on POSIX).
    /// </summary>
    private string AppendAiSessionSetup(string command, string workingDir)
    {
        try
        {
            var parley = _parley.PrepareLaunch(workingDir);
            var env = new Dictionary<string, string>(parley.Environment);

            var commandExe = command.Split(' ')[0];
            var binaryName = Path.GetFileNameWithoutExtension(
                Environment.ExpandEnvironmentVariables(commandExe));
            var channelFlags = binaryName.Equals("claude", StringComparison.OrdinalIgnoreCase)
                ? BuildChannelFlags(workingDir, parley.PushViaChannels, env)
                : "";

            var fullCommand = command + channelFlags;
            return env.Count > 0 ? _composer.WithEnvironment(fullCommand, env) : fullCommand;
        }
        catch
        {
            // If anything goes wrong with session setup, fall back to plain command
            return command;
        }
    }

    /// <summary>
    /// Builds Claude Code's channel flags (with a leading space, or empty) and adds the TerminalHost
    /// channel server's env vars to <paramref name="env"/>. Development channels share one
    /// space-separated flag, e.g. <c>--dangerously-load-development-channels server:terminalhost server:parley</c>.
    /// </summary>
    private string BuildChannelFlags(string workingDir, bool parleyPush, Dictionary<string, string> env)
    {
        var config = _configurationService.Load();
        var channelSettings = config.Settings.Channel;
        var developmentServers = new List<string>();
        var approvedServers = new List<string>();

        var channelServerPath = channelSettings.Enabled ? ResolveChannelServerPath(channelSettings) : null;
        if (!string.IsNullOrEmpty(channelServerPath))
        {
            // Register the channel server for Claude Code if auto-register is enabled
            if (channelSettings.AutoRegisterMcp && !string.IsNullOrEmpty(workingDir))
            {
                EnsureMcpJsonRegistered(workingDir, channelServerPath, channelSettings);
            }

            (channelSettings.UseDevelopmentFlag ? developmentServers : approvedServers).Add("server:terminalhost");

            // Environment variables so the channel server can find the API
            var apiSettings = config.Settings.Api;
            env["TERMINALHOST_API_URL"] = $"http://{(apiSettings.BindAddress == "0.0.0.0" ? "127.0.0.1" : apiSettings.BindAddress)}:{apiSettings.Port}";
            env["TERMINALHOST_EVENTS"] = string.Join(",", channelSettings.EventFilters);
        }

        // Parley is not an approved channel plugin, so it always needs the development flag
        if (parleyPush)
            developmentServers.Add(ParleyLaunchIntegration.ChannelEntry);

        var flags = "";
        if (approvedServers.Count > 0)
            flags += " --channels " + string.Join(" ", approvedServers);
        if (developmentServers.Count > 0)
            flags += " --dangerously-load-development-channels " + string.Join(" ", developmentServers);
        return flags;
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
