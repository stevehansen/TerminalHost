using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Posix.Services;

/// <summary>
/// Shared base for macOS and Linux system information services.
/// Font enumeration returns a static fallback list — the Avalonia decorator
/// overrides this with <c>FontManager</c> for accurate runtime results.
/// </summary>
public abstract class PosixSystemInfoServiceBase : ISystemInfoService
{
    private const string AppName = "TerminalHost";

    public abstract string GetApplicationDataPath();

    /// <summary>
    /// Returns the preferred shells in order. First existing shell wins.
    /// </summary>
    protected abstract IEnumerable<string> GetPreferredShells();

    /// <summary>
    /// Returns common font families for this platform as a fallback.
    /// </summary>
    protected abstract IReadOnlyList<string> GetFallbackFonts();

    public string GetUserHomePath()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string GetTempPath()
        => Path.GetTempPath();

    public string GetDefaultShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;

        foreach (var candidate in GetPreferredShells())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "/bin/sh";
    }

    public string GetDefaultCustomCommand()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var possiblePaths = new[]
        {
            Path.Combine(home, ".claude", "local", "claude"),
            Path.Combine(home, ".local", "bin", "claude"),
            "/usr/local/bin/claude",
            "/opt/homebrew/bin/claude",
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "claude";
    }

    public IEnumerable<string> GetInstalledFontFamilies()
        => GetFallbackFonts();

    public bool IsFontInstalled(string fontFamilyName)
        => GetFallbackFonts().Any(f => f.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase));

    protected static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }
}
