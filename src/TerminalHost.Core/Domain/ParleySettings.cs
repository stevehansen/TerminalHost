using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Settings for the Parley integration (inter-session pub/sub messaging for AI agents).
/// Parley itself is a standalone tool (dotnet tool <c>HC.Parley</c>); TerminalHost only
/// registers its MCP shim for launched sessions and observes the hub for the UI.
/// </summary>
public class ParleySettings
{
    public const string DefaultHubUrl = "http://127.0.0.1:19480";

    /// <summary>
    /// Whether the Parley integration is enabled: registers the <c>parley</c> MCP server
    /// for AI sessions and shows hub topics/messages in the UI.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Parley hub URL (the hub's own default is 127.0.0.1:19480).
    /// </summary>
    [JsonPropertyName("hubUrl")]
    public string HubUrl { get; set; } = DefaultHubUrl;

    /// <summary>
    /// Whether to launch Claude Code with <c>server:parley</c> in
    /// <c>--dangerously-load-development-channels</c> so messages are pushed into the session
    /// instead of polled. Requires the channels research preview and a claude.ai login.
    /// </summary>
    [JsonPropertyName("pushViaChannels")]
    public bool PushViaChannels { get; set; } = false;

    /// <summary>
    /// Ensure sensible values after deserialization.
    /// </summary>
    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(HubUrl)) HubUrl = DefaultHubUrl;
    }
}
