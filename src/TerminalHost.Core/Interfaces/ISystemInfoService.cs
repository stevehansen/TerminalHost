using System.Collections.Generic;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for system information.
/// Replaces direct Environment and font enumeration calls.
/// </summary>
public interface ISystemInfoService
{
    /// <summary>
    /// Gets the application data directory path.
    /// Windows: %APPDATA%\TerminalHost
    /// macOS: ~/Library/Application Support/TerminalHost
    /// Linux: ~/.config/TerminalHost
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
    /// Windows: pwsh.exe or powershell.exe
    /// macOS/Linux: /bin/zsh or /bin/bash
    /// </summary>
    string GetDefaultShell();

    /// <summary>
    /// Checks if a font family is installed.
    /// </summary>
    bool IsFontInstalled(string fontFamilyName);
}
