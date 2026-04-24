using System;
using System.Collections.Generic;
using System.IO;
using TerminalHost.Posix.Services;

namespace TerminalHost.macOS.Services;

/// <summary>
/// macOS implementation of system information service.
/// </summary>
public sealed class MacSystemInfoService : PosixSystemInfoServiceBase
{
    public override string GetApplicationDataPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return EnsureDirectory(Path.Combine(home, "Library", "Application Support", "TerminalHost"));
    }

    protected override IEnumerable<string> GetPreferredShells()
        => ["/bin/zsh", "/bin/bash"];

    protected override IReadOnlyList<string> GetFallbackFonts()
        =>
        [
            "SF Mono", "Menlo", "Monaco", "Courier New", "Courier",
            "JetBrains Mono", "Fira Code", "Source Code Pro",
            "Cascadia Code", "Cascadia Mono",
            "SF Pro", "SF Pro Display", "SF Pro Text",
            "Helvetica", "Helvetica Neue", "Arial", "Times New Roman"
        ];
}
