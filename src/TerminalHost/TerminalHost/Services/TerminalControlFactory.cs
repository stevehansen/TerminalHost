using System.IO;
using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class TerminalControlFactory
{
    public EasyTerminalControl CreateTerminalControl(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = string.IsNullOrWhiteSpace(profile.Command) ? "cmd.exe" : profile.Command;


        // Build a startup command that changes to working directory first, then runs the command
        string startupCommand;

        // Check if the command executable exists (for custom commands like claude.exe)
        var commandExe = command.Split(' ')[0];
        var commandExists = File.Exists(commandExe) ||
                           File.Exists(Environment.ExpandEnvironmentVariables(commandExe));

        if (!commandExists && !IsBuiltInCommand(commandExe))
        {
            Console.WriteLine($"[TerminalControlFactory] Warning: Command not found: {commandExe}, falling back to cmd.exe");
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


        // Create the terminal control with configured command line
        var terminalControl = new EasyTerminalControl
        {
            StartupCommandLine = startupCommand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            // Font must be set before initialization or SetTheme() called after
            // Fallback chain: Cascadia Code NF -> other Nerd Fonts
            FontFamilyWhenSettingTheme = new System.Windows.Media.FontFamily(string.Join(", ", AppConstants.NerdFontNames)),
            FontSizeWhenSettingTheme = 12,
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
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TerminalControlFactory] Error: RestartTerm failed for {profile.Name}: {ex.Message}");
                        }
                    }

                    // Apply theme with font settings - this triggers internal SetTheme
                    try
                    {
                        // Standard Campbell color scheme (Windows Terminal default)
                        var theme = new TerminalTheme
                        {
                            DefaultBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0x0C, 0x0C, 0x0C)),
                            DefaultForeground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                            DefaultSelectionBackground = EasyTerminalControl.ColorToVal(System.Windows.Media.Color.FromRgb(0x26, 0x4F, 0x78)),
                            CursorStyle = CursorStyle.BlinkingBar,
                            // 16-color palette: Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta, DarkYellow, Gray,
                            //                   DarkGray, Blue, Green, Cyan, Red, Magenta, Yellow, White
                            ColorTable = new uint[]
                            {
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
                            }
                        };
                        terminalControl.Theme = theme;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TerminalControlFactory] Error: Theme update failed for {profile.Name}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[TerminalControlFactory] Error: ConPTYTerm is null for {profile.Name}");
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        };

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
