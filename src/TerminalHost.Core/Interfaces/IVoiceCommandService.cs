using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for speech recognition and voice command execution.
/// Platform-specific implementations use native speech APIs.
/// </summary>
public interface IVoiceCommandService
{
    /// <summary>
    /// Whether voice commands are available on this platform (speech API present, microphone accessible).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether the service is currently listening for speech input.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// Start listening for voice commands.
    /// </summary>
    void StartListening();

    /// <summary>
    /// Stop listening for voice commands.
    /// </summary>
    void StopListening();

    /// <summary>
    /// Rebuild the speech recognition grammar from the current set of commands.
    /// Call this when the command palette or quick commands change.
    /// </summary>
    /// <param name="commands">All speakable commands to recognize.</param>
    void UpdateGrammar(IReadOnlyList<VoiceCommandEntry> commands);

    /// <summary>
    /// Raised when speech is recognized and matched (or partially matched) to a command.
    /// </summary>
    event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;

    /// <summary>
    /// Raised when a voice command error occurs (microphone issues, engine failures).
    /// </summary>
    event EventHandler<VoiceCommandErrorEventArgs>? Error;

    /// <summary>
    /// Raised when the listening state changes (started/stopped).
    /// </summary>
    event EventHandler? ListeningStateChanged;
}
