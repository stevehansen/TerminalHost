using System.IO;
using System.Media;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Service for playing sound notifications on Windows.
/// </summary>
public sealed class SoundService : ISoundService
{
    private readonly IConfigurationService _configurationService;
    private volatile bool _isAppFocused = true;

    public bool IsAppFocused => _isAppFocused;

    public SoundService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public void SetAppFocused(bool focused)
    {
        _isAppFocused = focused;
    }

    private SoundSettings GetSoundSettings()
    {
        return _configurationService.Load().Settings.Sounds;
    }

    public void Play(SoundType soundType)
    {
        var settings = GetSoundSettings();
        if (!settings.Enabled) return;

        var soundName = soundType switch
        {
            SoundType.Success => settings.SuccessSound,
            SoundType.Error => settings.ErrorSound,
            SoundType.Warning => settings.WarningSound,
            SoundType.Info => settings.InfoSound,
            SoundType.InputWaiting => settings.InputWaitingSound,
            _ => ""
        };

        if (string.IsNullOrEmpty(soundName)) return;

        PlaySound(soundName, respectFocusSetting: true);
    }

    public void PlaySound(string soundNameOrPath, bool respectFocusSetting = true)
    {
        if (string.IsNullOrEmpty(soundNameOrPath)) return;

        var settings = GetSoundSettings();

        // Check focus setting
        if (respectFocusSetting && settings.OnlyWhenUnfocused && _isAppFocused)
        {
            return;
        }

        try
        {
            // Check if it's a file path
            if (soundNameOrPath.Contains(Path.DirectorySeparatorChar) ||
                soundNameOrPath.Contains(Path.AltDirectorySeparatorChar) ||
                soundNameOrPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                PlayWavFile(soundNameOrPath);
            }
            else
            {
                // It's a system sound name
                PlaySystemSound(soundNameOrPath);
            }
        }
        catch
        {
            // Silently fail - sound is not critical
        }
    }

    private void PlaySystemSound(string soundName)
    {
        var sound = soundName.ToLowerInvariant() switch
        {
            "asterisk" => SystemSounds.Asterisk,
            "beep" => SystemSounds.Beep,
            "exclamation" => SystemSounds.Exclamation,
            "hand" => SystemSounds.Hand,
            "question" => SystemSounds.Question,
            _ => null
        };

        sound?.Play();
    }

    private void PlayWavFile(string filePath)
    {
        // Expand environment variables
        var expandedPath = Environment.ExpandEnvironmentVariables(filePath);

        if (!File.Exists(expandedPath)) return;

        using var player = new SoundPlayer(expandedPath);
        player.Play();
    }
}
