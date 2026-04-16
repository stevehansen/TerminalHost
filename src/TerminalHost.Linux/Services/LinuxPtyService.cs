using System.Collections.Generic;
using System.IO;
using PtySharp;
using PtySharp.Linux;
using TerminalHost.Posix.Services;

namespace TerminalHost.Linux.Services;

/// <summary>
/// Linux PTY implementation using PtySharp native PTY library.
/// </summary>
public class LinuxPtyService : PosixPtyServiceBase<PtySession, LinuxPtySyscalls>
{
    protected override IEnumerable<string> GetPlatformPathDirectories(string homeDir)
        => ["/snap/bin"];

    protected override string DefaultShell
        => File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
}
