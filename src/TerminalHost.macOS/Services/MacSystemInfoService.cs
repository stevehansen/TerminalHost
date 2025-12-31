using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.macOS.Services;

/// <summary>
/// macOS implementation of system information service.
/// </summary>
public sealed class MacSystemInfoService : ISystemInfoService
{
    private const string AppName = "TerminalHost";
    private List<string>? _cachedFontFamilies;

    public string GetApplicationDataPath()
    {
        // macOS: ~/Library/Application Support/TerminalHost
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appSupport = Path.Combine(home, "Library", "Application Support", AppName);

        // Ensure directory exists
        if (!Directory.Exists(appSupport))
        {
            Directory.CreateDirectory(appSupport);
        }

        return appSupport;
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
            // Use system_profiler to get font list
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/system_profiler",
                Arguments = "SPFontsDataType",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse the output - each font family is listed after "Full Name:"
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("Full Name:"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            var fontName = parts[1].Trim();
                            if (!string.IsNullOrEmpty(fontName) && !_cachedFontFamilies.Contains(fontName))
                            {
                                _cachedFontFamilies.Add(fontName);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // If system_profiler fails, provide common macOS fonts
            _cachedFontFamilies = GetCommonMacFonts();
        }

        // If we got nothing, provide common fonts
        if (_cachedFontFamilies.Count == 0)
        {
            _cachedFontFamilies = GetCommonMacFonts();
        }

        return _cachedFontFamilies;
    }

    private static List<string> GetCommonMacFonts()
    {
        return new List<string>
        {
            "SF Mono",
            "Menlo",
            "Monaco",
            "Courier New",
            "Courier",
            "JetBrains Mono",
            "Fira Code",
            "Source Code Pro",
            "Cascadia Code",
            "Cascadia Mono",
            "SF Pro",
            "SF Pro Display",
            "SF Pro Text",
            "Helvetica",
            "Helvetica Neue",
            "Arial",
            "Times New Roman"
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

        // macOS default is zsh
        if (File.Exists("/bin/zsh"))
            return "/bin/zsh";

        // Fallback to bash
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
