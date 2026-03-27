using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Linux.Services;

/// <summary>
/// Linux implementation of system information service.
/// </summary>
public sealed class LinuxSystemInfoService : ISystemInfoService
{
    private const string AppName = "TerminalHost";
    private List<string>? _cachedFontFamilies;

    public string GetApplicationDataPath()
    {
        // Linux (XDG Base Directory): ~/.config/TerminalHost
        // Environment.GetFolderPath(SpecialFolder.ApplicationData) returns ~/.config on Linux .NET
        var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Fallback if the special folder returns empty (unlikely but defensive)
        if (string.IsNullOrEmpty(configDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configDir = Path.Combine(home, ".config");
        }

        var appDir = Path.Combine(configDir, AppName);

        // Ensure directory exists
        if (!Directory.Exists(appDir))
        {
            Directory.CreateDirectory(appDir);
        }

        return appDir;
    }

    public string GetUserHomePath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string GetTempPath()
    {
        return Path.GetTempPath();
    }

    public IEnumerable<string> GetInstalledFontFamilies()
    {
        if (_cachedFontFamilies != null)
            return _cachedFontFamilies;

        _cachedFontFamilies = new List<string>();

        try
        {
            // Use fc-list to get monospace/fixed-width font families
            var startInfo = new ProcessStartInfo
            {
                FileName = "fc-list",
                Arguments = ":spacing=100 family",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse fc-list output: each line is a font family name (may contain commas for aliases)
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    // fc-list may return "Family1,Family2" for fonts with aliases
                    var families = line.Split(',');
                    foreach (var family in families)
                    {
                        var fontName = family.Trim();
                        if (!string.IsNullOrEmpty(fontName) && !_cachedFontFamilies.Contains(fontName))
                        {
                            _cachedFontFamilies.Add(fontName);
                        }
                    }
                }

                _cachedFontFamilies.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // If fc-list is not available, provide common Linux monospace fonts
            _cachedFontFamilies = GetCommonLinuxFonts();
        }

        // If we got nothing, provide common fonts
        if (_cachedFontFamilies.Count == 0)
        {
            _cachedFontFamilies = GetCommonLinuxFonts();
        }

        return _cachedFontFamilies;
    }

    private static List<string> GetCommonLinuxFonts()
    {
        return new List<string>
        {
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Ubuntu Mono",
            "Noto Sans Mono",
            "Fira Code",
            "JetBrains Mono",
            "Cascadia Code",
            "Source Code Pro",
            "Hack",
            "Inconsolata",
            "Droid Sans Mono",
            "monospace"
        };
    }

    public string GetDefaultShell()
    {
        // Check SHELL environment variable first
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
        {
            return shell;
        }

        // Linux default fallback: bash then sh
        if (File.Exists("/bin/bash"))
            return "/bin/bash";

        return "/bin/sh";
    }

    public bool IsFontInstalled(string fontFamilyName)
    {
        var fonts = GetInstalledFontFamilies();
        return fonts.Any(f => f.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase));
    }
}