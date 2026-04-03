using System.Collections.Generic;
using System.IO;
using TerminalHost.Posix.Services;

namespace TerminalHost.Linux.Services;

/// <summary>
/// Linux implementation of system information service.
/// </summary>
public sealed class LinuxSystemInfoService : PosixSystemInfoServiceBase
{
    public override string GetApplicationDataPath()
    {
        var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Fallback if the special folder returns empty
        if (string.IsNullOrEmpty(configDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configDir = Path.Combine(home, ".config");
        }

        return EnsureDirectory(Path.Combine(configDir, "TerminalHost"));
    }

    protected override IEnumerable<string> GetPreferredShells()
        => ["/bin/bash"];

    protected override IReadOnlyList<string> GetFallbackFonts()
        =>
        [
            "DejaVu Sans Mono", "Liberation Mono", "Ubuntu Mono",
            "Noto Sans Mono", "Fira Code", "JetBrains Mono",
            "Cascadia Code", "Source Code Pro", "Hack",
            "Inconsolata", "Droid Sans Mono", "monospace"
        ];
}
