using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Cached, merged, normalized view of container configuration. Reads pass through
/// a single in-memory snapshot of <see cref="AppConfiguration"/>; <see cref="Reload"/>
/// is the only way to invalidate it.
/// </summary>
public interface IContainerConfiguration
{
    /// <summary>
    /// Returns a frozen, fully-resolved snapshot of container settings for
    /// <paramref name="workspaceDir"/>. Per-directory overrides are merged on top of
    /// the global <see cref="ContainerSettings"/>. Thread-safe. First call after
    /// construction or <see cref="Reload"/> triggers a single
    /// <see cref="IConfigurationService.Load"/>; subsequent calls hit cached state.
    /// </summary>
    ResolvedContainerSettings For(string workspaceDir);

    /// <summary>
    /// Global-only projection for reads that don't need a workspace directory
    /// (e.g. docker path, auto-approve, image build). Same cached
    /// <see cref="AppConfiguration"/>; reference-stable until <see cref="Reload"/>.
    /// </summary>
    ContainerSettings Global { get; }

    /// <summary>
    /// Invalidate the cached <see cref="AppConfiguration"/> and per-workdir snapshots.
    /// Call from any code path that mutates configuration (settings UI save,
    /// directory-settings updater, REST API config write).
    /// Eventual consistency: concurrent <see cref="For"/> calls in flight when
    /// <see cref="Reload"/> fires may still observe a snapshot built from the
    /// pre-reload <see cref="AppConfiguration"/>; subsequent calls see fresh state.
    /// </summary>
    void Reload();
}

/// <summary>Flat, immutable, per-workspace resolved container configuration.</summary>
public sealed record ResolvedContainerSettings(
    string WorkspaceDir,
    bool Enabled,
    string DockerPath,
    string ImageName,
    string ImageTag,
    bool MountSsh,
    bool MountGhCli,
    bool AutoApproveInContainer,
    string NetworkMode,
    bool StopContainersOnExit,
    IReadOnlyList<ReferenceVolume> ReferenceVolumes,
    IReadOnlyList<ExtraMount> ExtraMounts,
    IReadOnlyList<string> ExtraDockerArgs,
    IReadOnlyDictionary<string, string> EnvVars);
