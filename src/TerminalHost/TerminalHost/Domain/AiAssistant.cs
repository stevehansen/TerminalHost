using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents an AI CLI assistant configuration.
/// </summary>
public class AiAssistant
{
    /// <summary>
    /// Unique identifier for this assistant (e.g., "claude", "gemini").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name shown in UI (e.g., "Claude Code", "Gemini CLI").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Command to run the assistant. Supports environment variables.
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>
    /// Icon/emoji displayed in UI.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    /// <summary>
    /// Command to detect if the assistant is installed (e.g., "claude --version").
    /// </summary>
    [JsonPropertyName("detectionCommand")]
    public string? DetectionCommand { get; set; }

    /// <summary>
    /// Whether this assistant is enabled and available for use.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether this is the default assistant for new projects.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// Returns the default set of AI assistants.
    /// </summary>
    public static List<AiAssistant> GetDefaults() =>
    [
        new AiAssistant
        {
            Id = "claude",
            Name = "Claude Code",
            Command = @"%USERPROFILE%\.local\bin\claude.exe",
            Icon = "Claude",
            DetectionCommand = "claude --version",
            Enabled = true,
            IsDefault = true
        },
        new AiAssistant
        {
            Id = "gemini",
            Name = "Gemini CLI",
            Command = "gemini",
            Icon = "Gemini",
            DetectionCommand = "gemini --version",
            Enabled = false,
            IsDefault = false
        },
        new AiAssistant
        {
            Id = "codex",
            Name = "OpenAI Codex",
            Command = "codex",
            Icon = "Codex",
            DetectionCommand = "codex --version",
            Enabled = false,
            IsDefault = false
        },
        new AiAssistant
        {
            Id = "copilot",
            Name = "GitHub Copilot",
            Command = "gh copilot",
            Icon = "Copilot",
            DetectionCommand = "gh copilot --version",
            Enabled = false,
            IsDefault = false
        }
    ];

    /// <summary>
    /// Creates a deep copy of this assistant.
    /// </summary>
    public AiAssistant Clone() => new()
    {
        Id = Id,
        Name = Name,
        Command = Command,
        Icon = Icon,
        DetectionCommand = DetectionCommand,
        Enabled = Enabled,
        IsDefault = IsDefault
    };
}
