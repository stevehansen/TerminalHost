using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using EasyWindowsTerminalControl;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class TerminalControlFactory
{
    public EasyTerminalControl CreateTerminalControl(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = string.IsNullOrWhiteSpace(profile.Command) ? "cmd.exe" : profile.Command;

        Debug.WriteLine($"[TerminalControlFactory] Creating terminal for: {profile.Name}");
        Debug.WriteLine($"[TerminalControlFactory] Working dir: {workingDir}");
        Debug.WriteLine($"[TerminalControlFactory] Command: {command}");

        // Build a startup command that changes to working directory first, then runs the command
        string startupCommand;

        // Check if the command executable exists (for custom commands like claude.exe)
        var commandExe = command.Split(' ')[0];
        var commandExists = File.Exists(commandExe) ||
                           File.Exists(Environment.ExpandEnvironmentVariables(commandExe));

        if (!commandExists && !IsBuiltInCommand(commandExe))
        {
            Debug.WriteLine($"[TerminalControlFactory] Command not found: {commandExe}, falling back to cmd.exe");
            command = "cmd.exe";
        }

        if (string.IsNullOrWhiteSpace(workingDir))
        {
            // Just use the command directly
            startupCommand = command;
        }
        else
        {
            // For cmd, use /K with cd
            if (command.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            {
                startupCommand = $"cmd.exe /K cd /d \"{workingDir}\"";
            }
            // For PowerShell variants
            else if (command.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
                     command.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
                     command.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                     command.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            {
                startupCommand = $"{command} -NoExit -WorkingDirectory \"{workingDir}\"";
            }
            else
            {
                // For other commands, run them from the directory using cmd
                startupCommand = $"cmd.exe /K cd /d \"{workingDir}\" && {command}";
            }
        }

        Debug.WriteLine($"[TerminalControlFactory] Startup command: {startupCommand}");

        // Create the terminal control with configured command line
        var terminalControl = new EasyTerminalControl
        {
            StartupCommandLine = startupCommand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            MinHeight = 100,
            MinWidth = 100
        };

        Debug.WriteLine($"[TerminalControlFactory] EasyTerminalControl created successfully");

        return terminalControl;
    }

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
