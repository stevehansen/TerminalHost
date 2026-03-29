using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Origin of a Claude Code session.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionSource
{
    /// <summary>Standard host-local Claude Code session.</summary>
    Local,

    /// <summary>TerminalHost-managed Docker container (bind-mounted workspace).</summary>
    Container,

    /// <summary>VS Code devcontainer (code inside container, no host path mapping).</summary>
    DevContainer
}
