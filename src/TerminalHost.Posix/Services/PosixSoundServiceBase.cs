using System.Diagnostics;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Posix.Services;

/// <summary>
/// Shared base for macOS and Linux sound notification services.
/// Handles settings, focus tracking, and file-vs-system-sound dispatch.
/// Platform subclasses provide the sound map and playback commands.
/// </summary>
public abstract class PosixSoundServiceBase : ISoundService
{
    private volatile bool _isAppFocused = true;
    private SoundSettings _cachedSettings;

    public bool IsAppFocused => _isAppFocused;

    protected PosixSoundServiceBase(IConfigurationService configurationService)
    {
        _cachedSettings = configurationService.Load().Settings.Sounds;
    }

    public void SetAppFocused(bool focused)
    {
        _isAppFocused = focused;
    }

    public void RefreshCachedSettings(SoundSettings settings)
    {
        _cachedSettings = settings;
    }

    public void Play(SoundType soundType)
    {
        var settings = _cachedSettings;
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

        var settings = _cachedSettings;

        if (respectFocusSetting && settings.OnlyWhenUnfocused && _isAppFocused)
            return;

        try
        {
            if (IsFilePath(soundNameOrPath))
            {
                var expanded = Environment.ExpandEnvironmentVariables(soundNameOrPath);
                PlayFile(expanded);
            }
            else
            {
                PlaySystemSound(soundNameOrPath);
            }
        }
        catch
        {
            // Silently fail - sound is not critical
        }
    }

    /// <summary>
    /// Play a system sound by name (e.g. "Asterisk", "Hand").
    /// </summary>
    protected abstract void PlaySystemSound(string soundName);

    /// <summary>
    /// Play a sound file at the given path.
    /// </summary>
    protected abstract void PlayFile(string filePath);

    protected static bool IsFilePath(string value)
    {
        return value.Contains(Path.DirectorySeparatorChar) ||
               value.Contains(Path.AltDirectorySeparatorChar) ||
               value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    protected static void StartDetached(string command, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            var process = Process.Start(psi);
            // Fire and forget - don't wait for playback to finish
            process?.Dispose();
        }
        catch
        {
            // Silently fail
        }
    }

    protected static bool CanRun(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("which", command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
