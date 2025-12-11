using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// A quick command that can be sent to a terminal with a button click.
/// </summary>
public class QuickCommand
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("target")]
    public QuickCommandTarget Target { get; set; } = QuickCommandTarget.Shell;

    [JsonPropertyName("appendNewline")]
    public bool AppendNewline { get; set; } = true;

    /// <summary>
    /// The newline character to use. Default is "\r" for shells.
    /// </summary>
    [JsonPropertyName("newlineChar")]
    public string NewlineChar { get; set; } = "\r";

    /// <summary>
    /// If true, uses the internal UserInput method instead of WriteToTerm.
    /// Use this for applications like Claude Code that need proper key event handling.
    /// </summary>
    [JsonPropertyName("useUserInput")]
    public bool UseUserInput { get; set; } = false;

    /// <summary>
    /// Keyboard shortcut for this command (e.g., "Ctrl+Shift+C", "Ctrl+Alt+P").
    /// Supports Ctrl, Alt, Shift modifiers with a single key.
    /// </summary>
    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; }

    /// <summary>
    /// Gets the display text for the button (icon if available, otherwise label).
    /// </summary>
    [JsonIgnore]
    public string DisplayText => string.IsNullOrEmpty(Icon) ? Label : Icon;

    /// <summary>
    /// Gets the tooltip text for the button, including shortcut if defined.
    /// </summary>
    [JsonIgnore]
    public string Tooltip => string.IsNullOrEmpty(Shortcut)
        ? $"{Label}: {Text}"
        : $"{Label}: {Text} ({Shortcut})";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuickCommandTarget
{
    Custom,
    Shell
}
