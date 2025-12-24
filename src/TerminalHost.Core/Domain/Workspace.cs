using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a workspace entry in the sidebar, which corresponds to a project directory.
/// </summary>
public class Workspace
{
    /// <summary>
    /// Unique identifier for the workspace.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for the workspace (defaults to folder name).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Full path to the workspace directory.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Which section the workspace belongs to: "main" or "playground".
    /// </summary>
    [JsonPropertyName("section")]
    public string Section { get; set; } = "main";

    /// <summary>
    /// Whether the workspace tree is expanded in the sidebar.
    /// </summary>
    [JsonPropertyName("isExpanded")]
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Optional custom icon for the workspace.
    /// </summary>
    [JsonPropertyName("customIcon")]
    public string? CustomIcon { get; set; }

    /// <summary>
    /// Order index for sorting workspaces in the sidebar.
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}
