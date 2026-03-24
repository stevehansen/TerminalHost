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

    public TerminalControlFactory(
        IFileSystem fileSystem,
        IDialogService dialogService,
        ISystemInfoService systemInfoService,
        IConfigurationService configurationService,
        IContainerService containerService)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _systemInfoService = systemInfoService;
        _configurationService = configurationService;
        _containerService = containerService;
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
            if (!IsValidCommand(command))
            {
                await ShowCommandWarningAsync(command);
                command = _systemInfoService.GetDefaultShell();
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

    private bool IsValidCommand(string command)
    {
        // Check if it's a full path that exists
        if (File.Exists(command))
            return true;

        // Get just the executable name if command has arguments
        var execName = command.Split(' ')[0];
        if (File.Exists(execName))
            return true;

        // Check if it's in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(':');

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, execName);
            if (File.Exists(fullPath))
                return true;
        }

        // Check common macOS locations
        var commonPaths = new[]
        {
            "/bin",
            "/usr/bin",
            "/usr/local/bin",
            "/opt/homebrew/bin", // Apple Silicon Homebrew
            "/usr/local/Homebrew/bin", // Intel Homebrew
        };

        foreach (var path in commonPaths)
        {
            var fullPath = Path.Combine(path, execName);
            if (File.Exists(fullPath))
                return true;
        }

        // Check if it's a built-in shell
        return IsBuiltInCommand(execName);
    }

    private static bool IsBuiltInCommand(string command)
    {
        var builtIns = new[]
        {
            "zsh", "/bin/zsh",
            "bash", "/bin/bash",
            "sh", "/bin/sh",
            "fish", "/usr/local/bin/fish", "/opt/homebrew/bin/fish",
            "tcsh", "/bin/tcsh",
            "csh", "/bin/csh",
        };

        return builtIns.Any(b => command.EndsWith(b, StringComparison.OrdinalIgnoreCase) ||
                                 command.Equals(b, StringComparison.OrdinalIgnoreCase));
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
        if (IsShellCommand(commandExe))
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

            // On macOS, prefix env vars inline (no 'set' or 'export' needed for single-command use)
            return $"TERMINALHOST_API_URL={apiUrl} TERMINALHOST_EVENTS={eventFilters} {command} {channelFlag}";
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
    /// Ensures a .mcp.json file exists in the project directory with the terminalhost channel server registered.
    /// </summary>
    private void EnsureMcpJsonRegistered(string workingDir, string channelServerPath, ChannelSettings channelSettings)
    {
        try
        {
            var mcpJsonPath = Path.Combine(workingDir, ".mcp.json");

            // Read existing .mcp.json or create new one
            Dictionary<string, object>? mcpConfig = null;
            if (_fileSystem.FileExists(mcpJsonPath))
            {
                var existing = _fileSystem.ReadAllText(mcpJsonPath);
                mcpConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(existing);
            }

            mcpConfig ??= new Dictionary<string, object>();

            // Check if terminalhost server is already registered
            Dictionary<string, object>? mcpServers = null;
            if (mcpConfig.TryGetValue("mcpServers", out var serversObj) && serversObj is System.Text.Json.JsonElement serversElement)
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

            mcpConfig["mcpServers"] = mcpServers;

            var json = System.Text.Json.JsonSerializer.Serialize(mcpConfig, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            _fileSystem.WriteAllText(mcpJsonPath, json);
        }
        catch
        {
            // .mcp.json registration is best-effort
        }
    }

    private static bool IsShellCommand(string command) =>
        IsBuiltInCommand(command);
}
