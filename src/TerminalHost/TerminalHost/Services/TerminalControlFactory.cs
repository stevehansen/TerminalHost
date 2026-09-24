using System.IO;
using System.Windows;
using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public sealed class TerminalControlFactory : ITerminalControlFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogService;
    private readonly IContainerService _containerService;
    private readonly IConfigurationService _configService;
    private readonly ParleyLaunchIntegration _parley;

    public TerminalControlFactory(IFileSystem fileSystem, IDialogService dialogService, IContainerService containerService, IConfigurationService configService, ParleyLaunchIntegration parley)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _containerService = containerService;
        _configService = configService;
        _parley = parley;
    }

    public EasyTerminalControl CreateTerminalControl(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = string.IsNullOrWhiteSpace(profile.Command) ? "cmd.exe" : profile.Command;

        string startupCommand;

        // Containerized session: use docker exec instead of local command
        if (!string.IsNullOrEmpty(profile.ContainerName))
        {
            startupCommand = BuildContainerCommand(profile.ContainerName, workingDir, command);
        }
        else
        {
            startupCommand = BuildLocalCommand(command, workingDir);
        }

        // Create the terminal control with configured command line
        var terminalControl = new EasyTerminalControl
        {
            StartupCommandLine = startupCommand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            // Font must be set before initialization or SetTheme() called after
            // Fallback chain: Cascadia Code NF -> other Nerd Fonts
            FontFamilyWhenSettingTheme = Application.Current?.Resources["FontFamilyMonospace"] as System.Windows.Media.FontFamily ?? new System.Windows.Media.FontFamily("Cascadia Code NF"),
            FontSizeWhenSettingTheme = (int)((double?)Application.Current?.Resources["FontSizeSmall"] ?? 12),
            MinHeight = 100,
            MinWidth = 100
        };

        // Initialize terminal after it's loaded into the visual tree
        terminalControl.Loaded += (s, e) =>
        {
            // Use Dispatcher to ensure we're fully in the visual tree before checking/starting process
            terminalControl.Dispatcher.InvokeAsync(async () =>
            {
                // Give the control a moment to fully initialize
                await Task.Delay(100);

                if (terminalControl.ConPTYTerm != null)
                {
                    // If process didn't start, try restarting the terminal
                    if (terminalControl.ConPTYTerm.Process == null || terminalControl.ConPTYTerm.Process.HasExited)
                    {
                        try
                        {
                            await terminalControl.RestartTerm();
                            await Task.Delay(500);
                        }
                        catch
                        {
                            // RestartTerm failed
                        }
                    }

                    // Apply theme with font settings - this triggers internal SetTheme
                    try
                    {
                        // Standard Campbell color scheme (Windows Terminal default)
                        var theme = new TerminalTheme
                        {
                            DefaultBackground = EasyTerminalControl.ColorToVal(Color.FromRgb(0x0C, 0x0C, 0x0C)),
                            DefaultForeground = EasyTerminalControl.ColorToVal(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                            DefaultSelectionBackground = EasyTerminalControl.ColorToVal(Color.FromRgb(0x26, 0x4F, 0x78)),
                            CursorStyle = CursorStyle.BlinkingBar,
                            // 16-color palette: Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta, DarkYellow, Gray,
                            //                   DarkGray, Blue, Green, Cyan, Red, Magenta, Yellow, White
                            ColorTable =
                            [
                                0x0C0C0C, // Black
                                0xDA3700, // DarkBlue (actually shows as blue due to BGR)
                                0x0EA113, // DarkGreen
                                0xDD963A, // DarkCyan
                                0x1F0FC5, // DarkRed
                                0x981788, // DarkMagenta
                                0x009CC1, // DarkYellow
                                0xCCCCCC, // Gray
                                0x767676, // DarkGray
                                0xFF783B, // Blue
                                0x0CC616, // Green
                                0xD6D661, // Cyan
                                0x5648E7, // Red
                                0x9E00B4, // Magenta
                                0xA5F1F9, // Yellow
                                0xF2F2F2  // White
                            ]
                        };
                        terminalControl.Theme = theme;
                    }
                    catch
                    {
                        // Theme update failed
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        };

        return terminalControl;
    }

    /// <summary>
    /// Build a docker exec command for running inside a container.
    /// </summary>
    private string BuildContainerCommand(string containerName, string workspaceDir, string command)
    {
        // For shell profiles (pwsh, cmd, bash, etc.), launch bash inside the container
        var commandExe = command.Split(' ')[0];
        if (IsShellCommand(commandExe))
        {
            return _containerService.BuildExecCommand(containerName, workspaceDir, "/bin/bash");
        }

        // For AI assistants and other commands, extract just the binary name
        // (the host path like %USERPROFILE%\.local\bin\claude.exe doesn't exist in the container)
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
    /// Build the original local command (non-containerized).
    /// </summary>
    private string BuildLocalCommand(string command, string workingDir)
    {
        // Check if the command executable exists (for custom commands like claude.exe)
        var commandExe = command.Split(' ')[0];
        var commandExists = _fileSystem.FileExists(commandExe) ||
                           _fileSystem.FileExists(Environment.ExpandEnvironmentVariables(commandExe));

        if (!commandExists && !IsBuiltInCommand(commandExe))
        {
            // Show warning on UI thread since this runs during terminal creation
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _dialogService.ShowWarning(
                    $"Command not found: {commandExe}\n\nFalling back to cmd.exe. Check your settings.",
                    "Terminal Warning");
            });
            command = "cmd.exe";
        }

        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return command;
        }

        // For cmd, use /K with cd
        if (command.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            return $"cmd.exe /K cd /d \"{workingDir}\" ";
        }

        // For PowerShell variants
        if (command.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            return $"{command} -NoExit -WorkingDirectory \"{workingDir}\" ";
        }

        // For other commands (AI assistants), run them from the directory using cmd,
        // with Parley session env and Claude Code channel flags when enabled
        var finalCommand = AppendAiSessionSetup(command, workingDir);
        return $"cmd.exe /K cd /d \"{workingDir}\" && {finalCommand}";
    }

    /// <summary>
    /// Prepares an AI command: Parley registration + session env (any AI CLI), and for Claude Code
    /// the channel servers to load (TerminalHost events and/or Parley push delivery).
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

            var envPrefix = string.Concat(env.Select(kv => $"set \"{kv.Key}={kv.Value}\" && "));
            return $"{envPrefix}{command}{channelFlags}";
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
        var config = _configService.Load();
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
    /// Resolves the path to the channel bridge executable (terminalhost-channel.exe).
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
        var exeName = OperatingSystem.IsWindows() ? "terminalhost-channel.exe" : "terminalhost-channel";
        var candidates = new[]
        {
            Path.Combine(appDir, exeName),
            Path.Combine(appDir, "terminalhost-channel", exeName),
            // Development: relative to the WPF project bin output
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
    /// Called for Claude Code sessions when channels are enabled.
    /// Uses global settings instead of per-project .mcp.json to avoid polluting every workspace.
    /// </summary>
    private void EnsureMcpJsonRegistered(string workingDir, string channelServerPath, ChannelSettings channelSettings)
    {
        try
        {
            var settingsPath = GetClaudeSettingsPath();
            if (settingsPath == null) return;

            var config = ReadClaudeSettings(settingsPath);
            var mcpServers = GetOrCreateMcpServers(config);

            if (mcpServers.ContainsKey("terminalhost"))
                return; // Already registered

            // Register the C# channel bridge executable directly (no runtime needed)
            mcpServers["terminalhost"] = new Dictionary<string, object>
            {
                ["command"] = channelServerPath.Replace("\\", "/")
            };

            config["mcpServers"] = mcpServers;
            WriteClaudeSettings(settingsPath, config);
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
    private string? GetClaudeSettingsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeDir = Path.Combine(home, ".claude");
        if (!Directory.Exists(claudeDir))
            return null;
        return Path.Combine(home, ".claude.json");
    }

    /// <summary>
    /// Reads and parses the Claude settings.json file, returning an empty dictionary if it doesn't exist.
    /// </summary>
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

    /// <summary>
    /// Extracts or creates the mcpServers dictionary from a Claude settings config.
    /// </summary>
    private static Dictionary<string, object> GetOrCreateMcpServers(Dictionary<string, object> config)
    {
        if (config.TryGetValue("mcpServers", out var serversObj) && serversObj is System.Text.Json.JsonElement serversElement)
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(serversElement.GetRawText())
                   ?? new Dictionary<string, object>();
        }
        return new Dictionary<string, object>();
    }

    /// <summary>
    /// Writes the Claude settings config back to disk.
    /// </summary>
    private void WriteClaudeSettings(string settingsPath, Dictionary<string, object> config)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        _fileSystem.WriteAllText(settingsPath, json);
    }

    private static bool IsShellCommand(string command) =>
        IsBuiltInCommand(command);

    private static bool IsBuiltInCommand(string command)
    {
        var builtIns = new[]
        {
            "cmd", "cmd.exe",
            "pwsh", "pwsh.exe",
            "powershell", "powershell.exe",
            "bash", "bash.exe",
            "wsl", "wsl.exe"
        };

        return builtIns.Any(b => b.Equals(command, StringComparison.OrdinalIgnoreCase));
    }
}
