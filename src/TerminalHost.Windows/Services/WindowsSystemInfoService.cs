using System.Drawing.Text;
using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of system information service.
/// </summary>
public sealed class WindowsSystemInfoService : ISystemInfoService
{
    private const string AppName = "TerminalHost";

    public string GetApplicationDataPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, AppName);
        EnsureDirectory(path);
        return path;
    }

    public string GetUserHomePath()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string GetTempPath()
        => Path.GetTempPath();

    public string GetDefaultShell()
    {
        // Prefer PowerShell Core, fall back to Windows PowerShell
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

    public string GetDefaultCustomCommand()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "bin", "claude.exe");
    }

    public IEnumerable<string> GetInstalledFontFamilies()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Select(f => f.Name).ToList();
    }

    public bool IsFontInstalled(string fontFamilyName)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f =>
            f.Name.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindInPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, executable);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}
