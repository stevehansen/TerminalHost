using Avalonia.Threading;
using TerminalHost.Controls;
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

    public TerminalControlFactory(
        IFileSystem fileSystem,
        IDialogService dialogService,
        ISystemInfoService systemInfoService)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _systemInfoService = systemInfoService;
    }

    public async Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = GetCommand(profile);

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

        var control = new MacTerminalControl();

        await control.InitializeAsync(command, workingDir);

        return control;
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
