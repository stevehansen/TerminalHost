using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Global settings for containerized workspaces (Docker).
/// </summary>
public class ContainerSettings
{
    /// <summary>
    /// Whether containerized workspaces are enabled globally.
    /// Per-directory settings can override this.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Path to the docker executable.
    /// </summary>
    [JsonPropertyName("dockerPath")]
    public string DockerPath { get; set; } = "docker";

    /// <summary>
    /// Docker image name for workspace containers.
    /// </summary>
    [JsonPropertyName("imageName")]
    public string ImageName { get; set; } = "terminalhost-workspace";

    /// <summary>
    /// Docker image tag.
    /// </summary>
    [JsonPropertyName("imageTag")]
    public string ImageTag { get; set; } = "latest";

    /// <summary>
    /// Whether to mount ~/.ssh as readonly inside containers.
    /// </summary>
    [JsonPropertyName("mountSsh")]
    public bool MountSsh { get; set; } = false;

    /// <summary>
    /// Pass --dangerously-skip-permissions to Claude Code inside containers.
    /// Since the container IS the sandbox, permission checks are redundant.
    /// Default: true.
    /// </summary>
    [JsonPropertyName("autoApproveInContainer")]
    public bool AutoApproveInContainer { get; set; } = true;

    /// <summary>
    /// Readonly reference volumes (shared source code libraries).
    /// Mounted at /refs/{name} inside the container.
    /// </summary>
    [JsonPropertyName("referenceVolumes")]
    public List<ReferenceVolume> ReferenceVolumes { get; set; } = [];

    /// <summary>
    /// Extra bind mounts (advanced).
    /// </summary>
    [JsonPropertyName("extraMounts")]
    public List<ExtraMount> ExtraMounts { get; set; } = [];

    /// <summary>
    /// Additional docker run arguments.
    /// </summary>
    [JsonPropertyName("extraDockerArgs")]
    public List<string> ExtraDockerArgs { get; set; } = [];

    /// <summary>
    /// Docker network mode: bridge, host, or none.
    /// </summary>
    [JsonPropertyName("networkMode")]
    public string NetworkMode { get; set; } = "bridge";

    /// <summary>
    /// Environment variables to set inside the container.
    /// </summary>
    [JsonPropertyName("envVars")]
    public Dictionary<string, string> EnvVars { get; set; } = new()
    {
        ["TERM"] = "xterm-256color"
    };

    /// <summary>
    /// Whether to stop all running containers when TerminalHost exits.
    /// Default: true. Set to false to leave containers running for faster reattach.
    /// </summary>
    [JsonPropertyName("stopContainersOnExit")]
    public bool StopContainersOnExit { get; set; } = true;
}

/// <summary>
/// A readonly reference volume for mounting shared source code.
/// </summary>
public class ReferenceVolume
{
    /// <summary>
    /// Absolute path on the host (e.g., P:\Vidyano.Service).
    /// </summary>
    [JsonPropertyName("hostPath")]
    public string HostPath { get; set; } = "";

    /// <summary>
    /// Display name, also used as the mount folder under /refs/.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// An extra bind mount for advanced use cases.
/// </summary>
public class ExtraMount
{
    [JsonPropertyName("hostPath")]
    public string HostPath { get; set; } = "";

    [JsonPropertyName("containerPath")]
    public string ContainerPath { get; set; } = "";

    [JsonPropertyName("readonly")]
    public bool Readonly { get; set; } = false;
}

/// <summary>
/// State of a workspace container.
/// </summary>
public enum ContainerState
{
    NotFound,
    Running,
    Stopped
}

/// <summary>
/// Information about a workspace container.
/// </summary>
public class ContainerInfo
{
    public string Name { get; set; } = "";
    public string WorkspaceDir { get; set; } = "";
    public ContainerState State { get; set; }
    public DateTime? CreatedAt { get; set; }
}
