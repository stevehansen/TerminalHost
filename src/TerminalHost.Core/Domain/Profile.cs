using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

public class Profile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Optional startup command to run after the main command.
    /// Used to start AI CLI inside a shell.
    /// </summary>
    [JsonPropertyName("startupCommand")]
    public string? StartupCommand { get; set; }

    [JsonPropertyName("workingDir")]
    public string WorkingDir { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; }

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    /// <summary>
    /// When set, this terminal session runs inside a Docker container.
    /// Not serialized — set at runtime by MainViewModel when container is enabled.
    /// </summary>
    [JsonIgnore]
    public string? ContainerName { get; set; }

    public string GetExpandedWorkingDir()
    {
        return Environment.ExpandEnvironmentVariables(WorkingDir);
    }
}
