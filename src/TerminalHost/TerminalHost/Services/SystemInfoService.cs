using System.IO;
using Avalonia.Media;

namespace TerminalHost.Services;

internal sealed class SystemInfoService : ISystemInfoService
{
    private const string AppName = "TerminalHost";

    public string GetApplicationDataPath()
    {
        // macOS: ~/Library/Application Support/TerminalHost
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", AppName);
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
        return FontManager.Current.SystemFonts.Select(f => f.Name);
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
        return FontManager.Current.SystemFonts
            .Any(f => f.Name.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase));
    }
}
