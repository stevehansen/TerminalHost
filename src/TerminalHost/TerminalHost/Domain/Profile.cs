using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

public class Profile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("workingDir")]
    public string WorkingDir { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; }

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    public string GetExpandedWorkingDir()
    {
        return Environment.ExpandEnvironmentVariables(WorkingDir);
    }
}
