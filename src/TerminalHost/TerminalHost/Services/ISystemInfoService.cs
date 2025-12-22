namespace TerminalHost.Services;

/// <summary>
/// Abstraction for system information.
/// Replaces direct Environment and font enumeration calls.
/// </summary>
public interface ISystemInfoService
{
    /// <summary>
    /// Gets the application data directory path.
    /// macOS: ~/Library/Application Support/TerminalHost
    /// </summary>
    string GetApplicationDataPath();

    /// <summary>
    /// Gets the user's home directory.
    /// </summary>
    string GetUserHomePath();

    /// <summary>
    /// Gets the temporary directory path.
    /// </summary>
    string GetTempPath();

    /// <summary>
    /// Gets installed system font family names.
    /// </summary>
    IEnumerable<string> GetInstalledFontFamilies();

    /// <summary>
    /// Gets the default shell command.
    /// </summary>
    string GetDefaultShell();

    /// <summary>
    /// Checks if a font family is installed.
    /// </summary>
    bool IsFontInstalled(string fontFamilyName);
}
