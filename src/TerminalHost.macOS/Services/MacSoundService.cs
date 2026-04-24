using TerminalHost.Core.Interfaces;
using TerminalHost.Posix.Services;

namespace TerminalHost.macOS.Services;

/// <summary>
/// Sound notification service for macOS.
/// Maps system sound names to /System/Library/Sounds/ files and plays via afplay.
/// </summary>
public sealed class MacSoundService : PosixSoundServiceBase
{
    private static readonly Dictionary<string, string> SoundMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["asterisk"] = "/System/Library/Sounds/Glass.aiff",
        ["beep"] = "/System/Library/Sounds/Tink.aiff",
        ["exclamation"] = "/System/Library/Sounds/Funk.aiff",
        ["hand"] = "/System/Library/Sounds/Basso.aiff",
        ["question"] = "/System/Library/Sounds/Purr.aiff",
    };

    public MacSoundService(IConfigurationService configurationService)
        : base(configurationService)
    {
    }

    protected override void PlaySystemSound(string soundName)
    {
        if (SoundMap.TryGetValue(soundName, out var path) && File.Exists(path))
            PlayFile(path);
    }

    protected override void PlayFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        StartDetached("afplay", filePath);
    }
}
