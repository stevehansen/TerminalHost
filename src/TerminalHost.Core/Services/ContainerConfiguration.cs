using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;

namespace TerminalHost.Core.Services;

/// <summary>
/// Default <see cref="IContainerConfiguration"/> built on top of
/// <see cref="IConfigurationService"/>. Caches one <see cref="AppConfiguration"/>
/// per Reload generation plus per-workdir resolved snapshots.
/// </summary>
public sealed class ContainerConfiguration : IContainerConfiguration
{
    private readonly IConfigurationService _configService;
    private readonly object _loadLock = new();
    private volatile AppConfiguration? _cachedConfig;
    private ConcurrentDictionary<string, ResolvedContainerSettings> _resolved = new();

    public ContainerConfiguration(IConfigurationService configService)
    {
        _configService = configService;
    }

    public ContainerSettings Global => LoadIfNeeded().Settings.Container;

    public ResolvedContainerSettings For(string workspaceDir)
    {
        var key = DirectorySettingsStore.NormalizeKey(workspaceDir);
        var resolved = _resolved;
        if (resolved.TryGetValue(key, out var existing))
            return existing;

        var config = LoadIfNeeded();
        return resolved.GetOrAdd(key, k => Resolve(k, config));
    }

    public void Reload()
    {
        lock (_loadLock)
        {
            _cachedConfig = null;
            _resolved = new ConcurrentDictionary<string, ResolvedContainerSettings>();
        }
    }

    private AppConfiguration LoadIfNeeded()
    {
        var cached = _cachedConfig;
        if (cached != null) return cached;

        lock (_loadLock)
        {
            if (_cachedConfig != null) return _cachedConfig;
            _cachedConfig = _configService.Load();
            return _cachedConfig;
        }
    }

    private static ResolvedContainerSettings Resolve(string normalizedKey, AppConfiguration config)
    {
        var global = config.Settings.Container;
        DirectorySettings? dir = null;
        if (normalizedKey.Length > 0)
            config.DirectorySettings.TryGetValue(normalizedKey, out dir);

        var enabled = dir?.ContainerEnabled ?? global.Enabled;
        var refVols = dir?.ContainerReferenceVolumes ?? global.ReferenceVolumes;

        return new ResolvedContainerSettings(
            WorkspaceDir: normalizedKey,
            Enabled: enabled,
            DockerPath: global.DockerPath,
            ImageName: global.ImageName,
            ImageTag: global.ImageTag,
            MountSsh: global.MountSsh,
            MountGhCli: global.MountGhCli,
            AutoApproveInContainer: global.AutoApproveInContainer,
            NetworkMode: global.NetworkMode,
            StopContainersOnExit: global.StopContainersOnExit,
            ReferenceVolumes: refVols.ToList().AsReadOnly(),
            ExtraMounts: global.ExtraMounts.ToList().AsReadOnly(),
            ExtraDockerArgs: global.ExtraDockerArgs.ToList().AsReadOnly(),
            EnvVars: new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(global.EnvVars)));
    }
}
