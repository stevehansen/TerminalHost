using System.Runtime.InteropServices;
using TerminalHost.Core.Interfaces;
using TerminalHost.Posix.Services;

namespace TerminalHost.Services;

/// <summary>
/// Windows sound notification service for the Avalonia host.
/// System sounds are routed through Win32 <c>MessageBeep</c>; file playback is a no-op
/// (the Avalonia host targets net8.0 cross-TFM, so <c>System.Media.SoundPlayer</c> isn't
/// available without an extra package — sound polish can come later).
/// </summary>
internal sealed class WindowsAvaloniaSoundService : PosixSoundServiceBase
{
    public WindowsAvaloniaSoundService(IConfigurationService configurationService)
        : base(configurationService)
    {
    }

    protected override void PlaySystemSound(string soundName)
    {
        // Map the same names the cross-platform SoundSettings uses to MessageBeep types.
        var type = soundName.ToLowerInvariant() switch
        {
            "asterisk" => 0x00000040u,    // MB_ICONASTERISK
            "beep" => 0xFFFFFFFFu,        // (UINT)-1 — default beep
            "exclamation" => 0x00000030u, // MB_ICONEXCLAMATION
            "hand" => 0x00000010u,        // MB_ICONHAND
            "question" => 0x00000020u,    // MB_ICONQUESTION
            _ => 0xFFFFFFFFu,
        };

        MessageBeep(type);
    }

    protected override void PlayFile(string filePath)
    {
        // No-op for now: see class summary.
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool MessageBeep(uint uType);
}
