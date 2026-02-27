using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Visual style for the "waiting for input" indicator.
/// </summary>
public enum WaitingIndicatorStyle
{
    /// <summary>
    /// Pulsing/breathing animation (attention-grabbing).
    /// </summary>
    Pulsing,

    /// <summary>
    /// Solid color dot (subtle, different from completed).
    /// </summary>
    SolidColor,

    /// <summary>
    /// Hollow/outline dot (subtle distinction).
    /// </summary>
    Outline,

    /// <summary>
    /// Question mark icon instead of dot.
    /// </summary>
    QuestionMark
}

/// <summary>
/// Defines a pattern for detecting when a terminal is waiting for user input.
/// </summary>
public class InputPromptPattern
{
    /// <summary>
    /// Unique identifier for this pattern.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable name for this pattern.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Regular expression pattern to match against terminal output.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    /// <summary>
    /// Whether this pattern is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Priority for pattern matching (higher = checked first).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Description of what this pattern detects.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets default patterns for common AI assistants and terminal prompts.
    /// </summary>
    public static List<InputPromptPattern> GetDefaults() =>
    [
        // Claude Code patterns - waiting for user input
        // Note: Claude uses various formats, these catch common permission/question prompts
        new InputPromptPattern
        {
            Id = "claude-yesno",
            Name = "Yes/No Prompt",
            Pattern = @"\[Y/n\]|\[y/N\]|\(y/n\)|\(Y/n\)|\[yes/no\]|\(yes/no\)",
            Priority = 100,
            Description = "Yes/No confirmation prompts like [Y/n]"
        },
        new InputPromptPattern
        {
            Id = "claude-permission",
            Name = "Permission Request",
            Pattern = @"(?i)(may I|can I|should I|shall I|would you like (me )?to|do you want (me )?to).*\?",
            Priority = 95,
            Description = "Claude asking for permission to proceed"
        },
        new InputPromptPattern
        {
            Id = "claude-choice",
            Name = "Choice Selection",
            Pattern = @"(?i)(which|what|select|choose|pick|option)\s*[\(\[]?\d+[\)\]]?.*\?|^\s*\d+[\.\)]\s+\w+",
            Priority = 90,
            Description = "Multiple choice selection prompts"
        },
        new InputPromptPattern
        {
            Id = "input-waiting",
            Name = "Waiting for Input",
            Pattern = @"(?i)(waiting for|enter your|type your|provide|input)\s+(response|answer|choice|selection|input)",
            Priority = 88,
            Description = "Generic waiting for input indicators"
        },
        new InputPromptPattern
        {
            Id = "claude-continue",
            Name = "Continue Prompt",
            Pattern = @"(?i)(continue|proceed|go ahead|carry on)\s*\?",
            Priority = 85,
            Description = "Continue/proceed confirmation"
        },
        new InputPromptPattern
        {
            Id = "claude-approve",
            Name = "Approval Request",
            Pattern = @"(?i)(approve|confirm|allow|permit|accept|agree|okay).*\?",
            Priority = 80,
            Description = "Approval/confirmation requests"
        },
        new InputPromptPattern
        {
            Id = "escape-hint",
            Name = "Escape Key Hint",
            Pattern = @"(?i)(esc(ape)?\s+(to\s+)?(cancel|exit|abort|quit|close|back))|(press\s+esc)",
            Priority = 98,
            Description = "Hints about pressing Escape to cancel (common in interactive prompts)"
        },
        new InputPromptPattern
        {
            Id = "enter-hint",
            Name = "Enter Key Hint",
            Pattern = @"(?i)(press\s+(enter|return)\s+to\s+(continue|confirm|proceed|accept))|(enter\s+to\s+submit)",
            Priority = 97,
            Description = "Hints about pressing Enter to continue"
        },
        new InputPromptPattern
        {
            Id = "type-here",
            Name = "Type Here Prompt",
            Pattern = @"(?i)type here to",
            Priority = 96,
            Description = "Prompts asking the user to type something (e.g., 'type here to tell Claude what to do')"
        },

        // General terminal prompts
        new InputPromptPattern
        {
            Id = "password-prompt",
            Name = "Password Prompt",
            Pattern = @"(?i)password\s*:|passphrase\s*:|enter.*password|secret\s*:",
            Priority = 80,
            Description = "Password/secret input prompts"
        },
        new InputPromptPattern
        {
            Id = "press-key",
            Name = "Press Key",
            Pattern = @"(?i)press (any key|enter|return)|hit (any key|enter)",
            Priority = 75,
            Description = "Press any key to continue"
        },
        new InputPromptPattern
        {
            Id = "input-colon",
            Name = "Input Colon",
            Pattern = @"(?i)(enter|input|type|provide|specify).*:\s*$",
            Priority = 70,
            Description = "Prompts ending with colon after input verb"
        },
        new InputPromptPattern
        {
            Id = "question-ending",
            Name = "Question Ending",
            Pattern = @"\?\s*$",
            Priority = 50,
            Description = "Lines ending with question mark",
            Enabled = false // Disabled by default - too many false positives
        },

        // NPM/Node patterns
        new InputPromptPattern
        {
            Id = "npm-init",
            Name = "NPM Init Prompt",
            Pattern = @"(?i)^(name|version|description|entry point|test command|git repository|keywords|author|license)\s*[:>]",
            Priority = 60,
            Description = "npm init interactive prompts"
        },

        // Git patterns
        new InputPromptPattern
        {
            Id = "git-interactive",
            Name = "Git Interactive",
            Pattern = @"(?i)^(pick|reword|edit|squash|fixup|drop|rebase|Stage this hunk|Discard this hunk)",
            Priority = 65,
            Description = "Git interactive rebase/staging"
        }
    ];
}

/// <summary>
/// Settings for input prompt detection.
/// </summary>
public class InputPromptSettings
{
    /// <summary>
    /// Whether input prompt detection is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Visual style for the waiting indicator.
    /// </summary>
    [JsonPropertyName("indicatorStyle")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WaitingIndicatorStyle IndicatorStyle { get; set; } = WaitingIndicatorStyle.Pulsing;

    /// <summary>
    /// Color for the waiting indicator (hex color code).
    /// Default is amber/orange to signal "attention needed" - distinct from green (completed) and yellow (active spinner).
    /// </summary>
    [JsonPropertyName("indicatorColor")]
    public string IndicatorColor { get; set; } = "#F59E0B"; // Amber-500

    /// <summary>
    /// Number of terminal lines to check for input prompts (from the end).
    /// </summary>
    [JsonPropertyName("lookbackLines")]
    public int LookbackLines { get; set; } = 5;

    /// <summary>
    /// Minimum idle time (in milliseconds) before checking for input prompts.
    /// Prevents false positives during rapid output.
    /// </summary>
    [JsonPropertyName("minIdleTimeMs")]
    public int MinIdleTimeMs { get; set; } = 500;

    /// <summary>
    /// Custom patterns to add to the default set.
    /// </summary>
    [JsonPropertyName("customPatterns")]
    public List<InputPromptPattern> CustomPatterns { get; set; } = [];

    /// <summary>
    /// Pattern IDs to disable from the default set.
    /// </summary>
    [JsonPropertyName("disabledPatternIds")]
    public List<string> DisabledPatternIds { get; set; } = [];
}
