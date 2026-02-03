namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Type of sound notification to play.
/// </summary>
public enum SoundType
{
    Success,
    Error,
    Warning,
    Info,
    InputWaiting
}

/// <summary>
/// Service for playing sound notifications.
/// </summary>
public interface ISoundService
{
    /// <summary>
    /// Play a sound notification of the specified type.
    /// Respects the "only when unfocused" setting.
    /// </summary>
    /// <param name="soundType">Type of sound to play.</param>
    void Play(SoundType soundType);

    /// <summary>
    /// Play a specific sound by name or file path.
    /// </summary>
    /// <param name="soundNameOrPath">System sound name or path to .wav file.</param>
    /// <param name="respectFocusSetting">Whether to respect the "only when unfocused" setting.</param>
    void PlaySound(string soundNameOrPath, bool respectFocusSetting = true);

    /// <summary>
    /// Check if the application window is currently focused.
    /// </summary>
    bool IsAppFocused { get; }

    /// <summary>
    /// Update the focused state of the application.
    /// Called by the main window when focus changes.
    /// </summary>
    void SetAppFocused(bool focused);
}
