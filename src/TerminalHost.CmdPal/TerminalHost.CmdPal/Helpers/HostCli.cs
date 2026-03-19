// Copyright (c) TerminalHost. All rights reserved.

using System.Diagnostics;

namespace TerminalHost.CmdPal.Helpers;

/// <summary>
/// Invokes the host.exe CLI to focus the TerminalHost window or open projects.
/// The CLI uses named pipe IPC internally — if TerminalHost is running, it sends
/// the command and the running instance handles it. If not running, it launches a new instance.
/// </summary>
internal static class HostCli
{
    private static string? _hostExePath;

    /// <summary>
    /// Bring the TerminalHost window to the foreground.
    /// Runs <c>host.exe</c> with no arguments.
    /// </summary>
    public static void FocusWindow()
    {
        Run(null);
    }

    /// <summary>
    /// Open a project folder in TerminalHost. If a tab for this directory
    /// already exists, it will be focused instead of creating a new one.
    /// </summary>
    public static void OpenProject(string path)
    {
        Run($"\"{path}\"");
    }

    private static void Run(string? arguments)
    {
        var exe = FindHostExe();
        if (exe == null)
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch
        {
            // Silently fail — the user can still open TerminalHost manually
        }
    }

    private static string? FindHostExe()
    {
        if (_hostExePath != null)
            return _hostExePath;

        // 1. Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "host.exe");
            if (File.Exists(candidate))
            {
                _hostExePath = candidate;
                return _hostExePath;
            }
        }

        // 2. Check common install locations
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var knownPaths = new[]
        {
            Path.Combine(localAppData, "TerminalHost", "host.exe"),
            Path.Combine(localAppData, "Programs", "TerminalHost", "host.exe"),
        };

        foreach (var path in knownPaths)
        {
            if (File.Exists(path))
            {
                _hostExePath = path;
                return _hostExePath;
            }
        }

        return null;
    }
}
