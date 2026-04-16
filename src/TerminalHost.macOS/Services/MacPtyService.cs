using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PtySharp;
using PtySharp.macOS;
using TerminalHost.Posix.Services;

namespace TerminalHost.macOS.Services;

/// <summary>
/// macOS PTY implementation using PtySharp native PTY library.
/// Resize is handled by PtySharp.macOS.PtySession which uses stty
/// to work around ARM64 variadic ioctl P/Invoke issues.
/// </summary>
public class MacPtyService : PosixPtyServiceBase<PtySession, MacOSPtySyscalls>
{
    protected override IEnumerable<string> GetPlatformPathDirectories(string homeDir) => [
        "/opt/homebrew/bin",
        "/opt/homebrew/sbin",
        "/opt/local/bin",
    ];

    protected override string DefaultShell
        => File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";

    /// <summary>
    /// Rejects macOS-specific invalid working directories (/Volumes/ mounts that may
    /// be ejected, .app/Contents/ bundles when launched via Finder) before falling
    /// back to $HOME or /tmp.
    /// </summary>
    protected override string BuildWorkingDir(string? requestedDir, string homeDir)
    {
        var invalidPatterns = new[]
        {
            "/Volumes/",
            ".app/Contents/",
        };

        if (!string.IsNullOrEmpty(requestedDir) && Directory.Exists(requestedDir))
        {
            var fullPath = Path.GetFullPath(requestedDir);
            var isInvalid = invalidPatterns.Any(pattern =>
                fullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (!isInvalid)
                return fullPath;
        }

        if (Directory.Exists(homeDir))
            return homeDir;

        return "/tmp";
    }
}
