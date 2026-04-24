namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for audio capture from microphone.
/// Platform-specific implementations use native audio APIs.
/// </summary>
public interface IAudioCaptureService
{
    /// <summary>
    /// Whether audio capture is available on this platform (microphone accessible).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether the service is currently recording audio.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Start recording audio from the default input device.
    /// Audio is captured at 16kHz, 16-bit, mono (Whisper format).
    /// </summary>
    void StartRecording();

    /// <summary>
    /// Stop recording and return the captured audio buffer.
    /// </summary>
    /// <returns>PCM audio data in 16kHz, 16-bit, mono format.</returns>
    byte[] StopRecording();

    /// <summary>
    /// Raised when audio data is available during recording.
    /// Used for real-time processing or voice activity detection.
    /// </summary>
    event EventHandler<AudioDataEventArgs>? DataAvailable;

    /// <summary>
    /// Raised when recording stops (either manually or due to silence detection).
    /// </summary>
    event EventHandler? RecordingStopped;
}

/// <summary>
/// Event arguments for audio data availability.
/// </summary>
public class AudioDataEventArgs : EventArgs
{
    /// <summary>
    /// The audio data buffer (PCM 16-bit samples).
    /// </summary>
    public required byte[] Buffer { get; init; }

    /// <summary>
    /// Number of bytes actually recorded in the buffer.
    /// </summary>
    public required int BytesRecorded { get; init; }
}
