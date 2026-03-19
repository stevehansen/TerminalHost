using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Manages Docker containers for workspace isolation.
/// </summary>
public interface IContainerService
{
    /// <summary>
    /// Check if Docker is available and running.
    /// </summary>
    Task<bool> IsDockerAvailableAsync();

    /// <summary>
    /// Check if the workspace image has been built.
    /// </summary>
    Task<bool> IsImageBuiltAsync();

    /// <summary>
    /// Build or rebuild the workspace image from Dockerfile.
    /// </summary>
    Task<bool> BuildImageAsync(Action<string>? onOutput = null);

    /// <summary>
    /// Get the current state of a workspace's container.
    /// </summary>
    Task<ContainerState> GetContainerStateAsync(string workspaceDir);

    /// <summary>
    /// Ensure a container is running for the given workspace (create or start as needed).
    /// Returns the container name.
    /// </summary>
    Task<string> EnsureContainerRunningAsync(string workspaceDir);

    /// <summary>
    /// Build the docker exec command for launching an interactive session.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="command">The command to run inside the container (e.g., "claude", "/bin/bash").</param>
    /// <param name="extraArgs">Extra arguments to append to the command (e.g., "--dangerously-skip-permissions").</param>
    string BuildExecCommand(string containerName, string? command = null, string? extraArgs = null);

    /// <summary>
    /// Whether auto-approve (--dangerously-skip-permissions) is enabled for containerized AI agents.
    /// </summary>
    bool IsAutoApproveEnabled { get; }

    /// <summary>
    /// Stop a workspace's container.
    /// </summary>
    Task StopContainerAsync(string workspaceDir);

    /// <summary>
    /// Remove a workspace's container.
    /// </summary>
    Task RemoveContainerAsync(string workspaceDir);

    /// <summary>
    /// List all TerminalHost containers with their state.
    /// </summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync();

    /// <summary>
    /// Remove all stopped TerminalHost containers.
    /// </summary>
    Task<int> CleanStoppedContainersAsync();

    /// <summary>
    /// Get the container name for a workspace directory.
    /// </summary>
    string GetContainerName(string workspaceDir);

    /// <summary>
    /// Whether containerization is enabled for a given workspace directory.
    /// Checks per-directory override, then global setting.
    /// </summary>
    bool IsEnabledForDirectory(string workspaceDir);

    /// <summary>
    /// Ensure the Dockerfile exists in the config directory.
    /// Creates the default one if missing.
    /// </summary>
    void EnsureDockerfileExists();
}
