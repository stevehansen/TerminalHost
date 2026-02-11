namespace TerminalHost.Core.Domain;

/// <summary>
/// Speech recognition engine to use for voice commands.
/// </summary>
public enum VoiceRecognitionEngine
{
    /// <summary>
    /// Windows built-in System.Speech.Recognition (zero setup, constrained grammar).
    /// </summary>
    WindowsSpeech,

    /// <summary>
    /// OpenAI Whisper via whisper.cpp (open vocabulary, multi-language, requires model download).
    /// </summary>
    Whisper
}

/// <summary>
/// Whisper model size options. Larger models are more accurate but slower and use more memory.
/// </summary>
public enum WhisperModelSize
{
    /// <summary>~75 MB, fastest, lower accuracy.</summary>
    Tiny,
    /// <summary>~142 MB, good balance for simple commands.</summary>
    Base,
    /// <summary>~466 MB, excellent accuracy for mixed-language use (recommended).</summary>
    Small,
    /// <summary>~1.5 GB, high accuracy, slower on CPU.</summary>
    Medium,
    /// <summary>~3 GB, highest accuracy, requires significant RAM.</summary>
    LargeV3
}

/// <summary>
/// Voice activation mode for triggering speech recognition.
/// </summary>
public enum VoiceActivationMode
{
    /// <summary>
    /// Hold the activation key to listen, release to process.
    /// </summary>
    PushToTalk,

    /// <summary>
    /// Press once to start listening, press again to stop.
    /// </summary>
    Toggle
}

/// <summary>
/// State machine for the voice command flow.
/// Idle → Listening → Processing → Preview → (Executed | Cancelled | SendToAi)
/// </summary>
public enum VoiceFlowState
{
    /// <summary>
    /// Not active. Bar is hidden.
    /// </summary>
    Idle,

    /// <summary>
    /// Microphone is active, capturing speech. Bar shows "Listening...".
    /// </summary>
    Listening,

    /// <summary>
    /// Speech captured, running through recognition/matching. Bar shows transcript.
    /// </summary>
    Processing,

    /// <summary>
    /// Command matched, showing preview with countdown before execution.
    /// User can cancel (Escape), pick an alternative, or send to AI.
    /// </summary>
    Preview,

    /// <summary>
    /// No match found. Bar shows transcript with "Send to AI" option.
    /// </summary>
    NoMatch,

    /// <summary>
    /// Command was executed. Bar dismisses shortly after.
    /// </summary>
    Executed
}

/// <summary>
/// A speakable command entry that maps a voice phrase to an executable action.
/// Built from the command palette and quick commands.
/// </summary>
public class VoiceCommandEntry
{
    /// <summary>
    /// Palette command ID or quick command ID (e.g., "git-changes", "qc-commit").
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Display name of the command (e.g., "Git Changes").
    /// </summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Keyboard shortcut string if available (e.g., "Alt+G").
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// Primary voice phrase (e.g., "git changes").
    /// </summary>
    public required string PrimaryPhrase { get; init; }

    /// <summary>
    /// Alternative phrases that also trigger this command (e.g., ["git status", "show changes"]).
    /// </summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>
    /// Action to execute (same delegate as palette command).
    /// </summary>
    public required Action Execute { get; init; }

    /// <summary>
    /// Category for grouping in "what can I say" (e.g., "Git", "Navigation", "Quick Command").
    /// </summary>
    public string Category { get; init; } = "General";

    /// <summary>
    /// All speakable phrases (primary + aliases) for building speech grammar.
    /// </summary>
    public IEnumerable<string> AllPhrases
    {
        get
        {
            yield return PrimaryPhrase;
            foreach (var alias in Aliases)
                yield return alias;
        }
    }
}

/// <summary>
/// A matched command with confidence score from speech recognition.
/// </summary>
public record VoiceCommandMatch(VoiceCommandEntry Command, float Confidence)
{
    /// <summary>
    /// Countdown duration in seconds based on confidence level.
    /// Higher confidence = shorter wait. Lower confidence = more time to cancel.
    /// </summary>
    public int CountdownSeconds => Confidence switch
    {
        >= 0.95f => 2,
        >= 0.8f => 3,
        >= 0.6f => 5,
        _ => 7
    };
}

/// <summary>
/// Detected intent from the voice transcript (meta-commands).
/// </summary>
public enum VoiceIntent
{
    /// <summary>
    /// Normal command matching — try to match a palette/quick command.
    /// </summary>
    Command,

    /// <summary>
    /// User explicitly wants to send text to the AI terminal.
    /// Triggered by prefixes like "send to claude", "tell AI", "instruct AI",
    /// or suffixes like "send to AI".
    /// </summary>
    SendToAi,

    /// <summary>
    /// User said "yes", "confirm", "go" — confirm the current pending action.
    /// </summary>
    Confirm,

    /// <summary>
    /// User said "no", "cancel", "stop", "nevermind" — cancel the current action.
    /// </summary>
    Cancel
}

/// <summary>
/// Result of attempting to match a transcript to a command.
/// </summary>
public class VoiceCommandResult
{
    /// <summary>
    /// The raw transcript from speech recognition.
    /// </summary>
    public required string Transcript { get; init; }

    /// <summary>
    /// The detected intent (command match, send-to-AI, confirm, cancel).
    /// </summary>
    public VoiceIntent Intent { get; init; } = VoiceIntent.Command;

    /// <summary>
    /// For SendToAi intent, the extracted message text (with prefix stripped).
    /// </summary>
    public string? AiMessage { get; init; }

    /// <summary>
    /// Whether a command was matched with sufficient confidence.
    /// </summary>
    public bool IsMatch => Intent == VoiceIntent.Command && BestMatch is not null && BestMatch.Confidence >= ConfidenceThreshold;

    /// <summary>
    /// The best matching command (may be below threshold).
    /// </summary>
    public VoiceCommandMatch? BestMatch { get; init; }

    /// <summary>
    /// Alternative matches for disambiguation (top 3).
    /// </summary>
    public List<VoiceCommandMatch> Alternatives { get; init; } = [];

    /// <summary>
    /// The confidence threshold used for this match.
    /// </summary>
    public float ConfidenceThreshold { get; init; } = 0.8f;
}

/// <summary>
/// Event args raised when speech recognition produces a result.
/// </summary>
public class VoiceCommandRecognizedEventArgs : EventArgs
{
    /// <summary>
    /// The match result containing transcript, matched command, and alternatives.
    /// </summary>
    public required VoiceCommandResult Result { get; init; }
}

/// <summary>
/// Event args raised when a voice command error occurs.
/// </summary>
public class VoiceCommandErrorEventArgs : EventArgs
{
    /// <summary>
    /// Error message describing what went wrong.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Whether this is a fatal error (e.g., no microphone access) vs transient.
    /// </summary>
    public bool IsFatal { get; init; }
}
