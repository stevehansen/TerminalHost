using System;
using System.Collections.Generic;
using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Windows implementation of <see cref="ISystemInfoService"/> for the Avalonia host.
/// Font enumeration returns a fallback list — <see cref="AvaloniaSystemInfoDecorator"/>
/// overrides it with Avalonia's <c>FontManager</c> at runtime.
/// </summary>
internal sealed class WindowsAvaloniaSystemInfoService : ISystemInfoService
{
    private const string AppName = "TerminalHost";

    private static readonly string[] FallbackFonts =
    [
        "Cascadia Code", "Cascadia Mono", "Consolas",
        "Cascadia Code NF", "JetBrains Mono", "Fira Code",
        "Source Code Pro", "Courier New", "Segoe UI", "Arial",
    ];

    public string GetApplicationDataPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, AppName);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    public string GetUserHomePath()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string GetTempPath() => Path.GetTempPath();

    public string GetDefaultShell()
    {
        var pwsh = FindInPath("pwsh.exe");
        if (pwsh != null)
            return pwsh;

        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(powershell))
            return powershell;

        return "cmd.exe";
    }

    public IEnumerable<string> GetInstalledFontFamilies() => FallbackFonts;

    public bool IsFontInstalled(string fontFamilyName)
    {
        foreach (var f in FallbackFonts)
        {
            if (f.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? FindInPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var fullPath = Path.Combine(dir, executable);
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }
}
