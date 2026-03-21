using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Settings for Claude Code channel integration.
/// Channels push TerminalHost events (git changes, terminal activity, user messages)
/// into running Claude Code sessions via the MCP channel protocol.
/// </summary>
public class ChannelSettings
{
    /// <summary>
    /// Whether the channel integration is enabled.
    /// When enabled, Claude Code is launched with the --dangerously-load-development-channels flag
    /// and the TerminalHost channel server is registered in .mcp.json.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Event filter patterns to forward to Claude Code sessions.
    /// Supports wildcards: "*" (all), "repo.*" (prefix), "repo.commit" (exact).
    /// Default: common events that are useful for AI context.
    /// </summary>
    [JsonPropertyName("eventFilters")]
    public List<string> EventFilters { get; set; } =
    [
        "repo.git_status_changed",
        "repo.branch_switched",
        "repo.commit",
        "channel.user_message"
    ];

    /// <summary>
    /// Path to the channel bridge executable (terminalhost-channel.exe).
    /// Default: auto-detected relative to the application directory.
    /// </summary>
    [JsonPropertyName("channelServerPath")]
    public string? ChannelServerPath { get; set; }

    /// <summary>
    /// Whether to use --dangerously-load-development-channels (required during research preview)
    /// or --channels (for approved plugins).
    /// </summary>
    [JsonPropertyName("useDevelopmentFlag")]
    public bool UseDevelopmentFlag { get; set; } = true;

    /// <summary>
    /// Whether to automatically register the channel server in .mcp.json for each project.
    /// </summary>
    [JsonPropertyName("autoRegisterMcp")]
    public bool AutoRegisterMcp { get; set; } = true;
}
