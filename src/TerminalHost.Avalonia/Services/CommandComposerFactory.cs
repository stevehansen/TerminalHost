using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Per-OS construction of <see cref="ICommandComposer"/>.
/// </summary>
public static class CommandComposerFactory
{
    public static ICommandComposer ForCurrentOs()
    {
#if WINDOWS
        return new TerminalHost.Core.Services.WindowsCommandComposer();
#elif MACOS || LINUX
        return new TerminalHost.Posix.Services.PosixCommandComposer();
#else
        throw new System.PlatformNotSupportedException();
#endif
    }
}
