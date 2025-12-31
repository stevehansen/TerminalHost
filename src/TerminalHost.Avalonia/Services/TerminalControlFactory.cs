using Avalonia.Threading;
using TerminalHost.Controls;
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

    public TerminalControlFactory(
        IFileSystem fileSystem,
        IDialogService dialogService,
        ISystemInfoService systemInfoService,
        IConfigurationService configurationService)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _systemInfoService = systemInfoService;
        _configurationService = configurationService;
    }

    public async Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = GetCommand(profile);
        var startupCommand = profile.StartupCommand;

        // Verify command exists
        if (!IsValidCommand(command))
        {
            await ShowCommandWarningAsync(command);
            command = _systemInfoService.GetDefaultShell();
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
    /// Sends a startup command to the terminal after a delay.
    /// This allows the shell to fully initialize and print its prompt before the command is sent.
    /// </summary>
    private static async Task SendStartupCommandAsync(ITerminalControl control, string command)
    {
        // Wait for shell to fully initialize and print prompt
        // 1500ms gives enough time for zsh to load configs and display the prompt
        await Task.Delay(1500);

        // Send the command with a newline to execute it
        control.WriteToTerminal(command + "\r");
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
}
