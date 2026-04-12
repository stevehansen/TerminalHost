using System.IO;
using System.Security.Cryptography;
using System.Text;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Manages Docker containers for workspace isolation.
/// Shells out to the docker CLI for all operations.
/// </summary>
public class ContainerService : IContainerService
{
    private const string ContainerPrefix = "terminalhost-ws";

    private readonly IConfigurationService _configService;
    private readonly IProcessService _processService;
    private readonly IFileSystem _fileSystem;
    private readonly string _configDirectory;

    public ContainerService(
        IConfigurationService configService,
        IProcessService processService,
        IFileSystem fileSystem,
        string? configDirectory = null)
    {
        _configService = configService;
        _processService = processService;
        _fileSystem = fileSystem;
        _configDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TerminalHost");
    }

    public bool IsEnabledForDirectory(string workspaceDir)
    {
        var config = _configService.Load();
        var normalizedPath = NormalizePath(workspaceDir);

        // Check per-directory override
        if (config.DirectorySettings.TryGetValue(normalizedPath, out var dirSettings)
            && dirSettings.ContainerEnabled.HasValue)
        {
            return dirSettings.ContainerEnabled.Value;
        }

        // Fall back to global setting
        return config.Settings.Container.Enabled;
    }

    public async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            var dockerPath = GetDockerPath();
            var (exitCode, _, _) = await _processService.RunAsync(dockerPath, "info", timeout: TimeSpan.FromSeconds(10));
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsImageBuiltAsync()
    {
        var config = _configService.Load();
        var cs = config.Settings.Container;
        var dockerPath = GetDockerPath();
        var (exitCode, _, _) = await _processService.RunAsync(
            dockerPath, $"image inspect {cs.ImageName}:{cs.ImageTag}",
            timeout: TimeSpan.FromSeconds(10));
        return exitCode == 0;
    }

    public async Task<bool> BuildImageAsync(Action<string>? onOutput = null)
    {
        EnsureDockerfileExists();

        var config = _configService.Load();
        var cs = config.Settings.Container;
        var dockerPath = GetDockerPath();
        var dockerfileDir = Path.Combine(_configDirectory, "container");
        var dockerfilePath = Path.Combine(dockerfileDir, "Dockerfile");

        var args = $"build -t {cs.ImageName}:{cs.ImageTag} -f \"{dockerfilePath}\" \"{dockerfileDir}\"";

        var (exitCode, output, error) = await _processService.RunStreamingAsync(
            dockerPath, args,
            timeout: TimeSpan.FromMinutes(10),
            onOutput: onOutput);

        return exitCode == 0;
    }

    public async Task<ContainerState> GetContainerStateAsync(string workspaceDir)
    {
        var containerName = GetContainerName(workspaceDir);
        var dockerPath = GetDockerPath();

        var (exitCode, output, _) = await _processService.RunAsync(
            dockerPath, $"inspect -f \"{{{{.State.Running}}}}\" {containerName}",
            timeout: TimeSpan.FromSeconds(10));

        if (exitCode != 0)
            return ContainerState.NotFound;

        return output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            ? ContainerState.Running
            : ContainerState.Stopped;
    }

    public async Task<ContainerEnsureResult> EnsureContainerRunningAsync(string workspaceDir)
    {
        // On macOS, export Claude Code credentials from Keychain before container start.
        // Must happen before any container operation since the file is bind-mounted.
        if (OperatingSystem.IsMacOS())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            await ExportMacOsCredentialsAsync(userProfile);
        }

        var containerName = GetContainerName(workspaceDir);
        var state = await GetContainerStateAsync(workspaceDir);
        var action = ContainerEnsureAction.AlreadyRunning;
        var configStale = false;
        var imageStale = false;

        switch (state)
        {
            case ContainerState.Running:
                configStale = await IsContainerConfigStaleAsync(workspaceDir);
                imageStale = await IsContainerImageStaleAsync(workspaceDir);
                break;

            case ContainerState.Stopped:
                await StartContainerAsync(containerName);
                action = ContainerEnsureAction.Started;
                configStale = await IsContainerConfigStaleAsync(workspaceDir);
                imageStale = await IsContainerImageStaleAsync(workspaceDir);
                break;

            case ContainerState.NotFound:
                // Ensure image exists
                if (!await IsImageBuiltAsync())
                {
                    var built = await BuildImageAsync();
                    if (!built)
                        throw new InvalidOperationException("Failed to build Docker image. Check Docker Desktop is running.");
                    action = ContainerEnsureAction.ImageBuilt;
                }
                else
                {
                    action = ContainerEnsureAction.Created;
                }

                await CreateContainerAsync(workspaceDir, containerName);
                break;

            default:
                throw new InvalidOperationException($"Unexpected container state: {state}");
        }

        // Fix workspace ownership — Windows bind mounts appear as root:root inside the
        // container, causing EACCES when developer user tries to write (e.g., npm install).
        // Only runs on fresh create/start to avoid unnecessary overhead on already-running containers.
        if (action is ContainerEnsureAction.Created or ContainerEnsureAction.ImageBuilt or ContainerEnsureAction.Started)
            await InitWorkspaceOwnershipAsync(containerName, workspaceDir);

        // Mark worktree workspace as safe for git (the .git file overlay is handled via mount).
        if (action is ContainerEnsureAction.Created or ContainerEnsureAction.ImageBuilt or ContainerEnsureAction.Started)
            await InitWorktreeGitLinkAsync(containerName, workspaceDir);

        // Copy GPG keys from staging mount to container-local dir with correct ownership.
        // Windows mounts appear as root-owned; GPG refuses "unsafe ownership on homedir".
        await InitGpgKeysAsync(containerName);

        // Copy GitHub CLI config from staging mount so gh uses the host's auth session.
        await InitGhCliAsync(containerName);

        return new ContainerEnsureResult
        {
            ContainerName = containerName,
            Action = action,
            IsConfigStale = configStale,
            IsImageStale = imageStale
        };
    }

    public bool IsAutoApproveEnabled
    {
        get
        {
            var config = _configService.Load();
            return config.Settings.Container.AutoApproveInContainer;
        }
    }

    public string BuildExecCommand(string containerName, string workspaceDir, string? command = null, string? extraArgs = null)
    {
        var dockerPath = GetDockerPath();
        var containerWs = GetContainerWorkspacePath(workspaceDir);
        var cmd = command ?? "/bin/bash";
        if (!string.IsNullOrEmpty(extraArgs))
            cmd = $"{cmd} {extraArgs}";
        return $"\"{dockerPath}\" exec -it -w {containerWs} {containerName} {cmd}";
    }

    public async Task StopContainerAsync(string workspaceDir)
    {
        var containerName = GetContainerName(workspaceDir);
        var dockerPath = GetDockerPath();
        await _processService.RunAsync(dockerPath, $"stop {containerName}", timeout: TimeSpan.FromSeconds(30));
    }

    public async Task RemoveContainerAsync(string workspaceDir)
    {
        var containerName = GetContainerName(workspaceDir);
        var dockerPath = GetDockerPath();
        // Stop first if running
        await _processService.RunAsync(dockerPath, $"stop {containerName}", timeout: TimeSpan.FromSeconds(30));
        await _processService.RunAsync(dockerPath, $"rm {containerName}", timeout: TimeSpan.FromSeconds(10));
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync()
    {
        var dockerPath = GetDockerPath();
        var (exitCode, output, _) = await _processService.RunAsync(
            dockerPath,
            $"ps -a --filter \"name={ContainerPrefix}-\" --format \"{{{{.Names}}}}\\t{{{{.State}}}}\\t{{{{.CreatedAt}}}}\"",
            timeout: TimeSpan.FromSeconds(10));

        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return [];

        var results = new List<ContainerInfo>();
        var currentDockerfileHash = ComputeDockerfileHash();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            var name = parts[0].Trim();
            var stateStr = parts[1].Trim().ToLowerInvariant();

            var state = stateStr switch
            {
                "running" => ContainerState.Running,
                _ => ContainerState.Stopped
            };

            var info = new ContainerInfo { Name = name, State = state };

            // Fetch labels to determine workspace dir and staleness
            var labels = await GetContainerLabelsAsync(name);
            if (labels.TryGetValue("terminalhost.workspace-dir", out var wsDir))
                info.WorkspaceDir = wsDir;

            // Check image staleness (global — doesn't need workspace dir)
            if (labels.TryGetValue("terminalhost.dockerfile-hash", out var storedImgHash))
                info.IsImageStale = storedImgHash != currentDockerfileHash;
            else
                info.IsImageStale = true; // pre-dates versioning

            // Check config staleness (needs workspace dir)
            if (!string.IsNullOrEmpty(info.WorkspaceDir))
            {
                if (labels.TryGetValue("terminalhost.config-hash", out var storedCfgHash))
                    info.IsConfigStale = storedCfgHash != ComputeConfigHash(info.WorkspaceDir);
                else
                    info.IsConfigStale = true;
            }

            results.Add(info);
        }

        return results;
    }

    public async Task<int> CleanStoppedContainersAsync()
    {
        var containers = await ListContainersAsync();
        var stopped = containers.Where(c => c.State == ContainerState.Stopped).ToList();
        var dockerPath = GetDockerPath();

        foreach (var container in stopped)
        {
            await _processService.RunAsync(dockerPath, $"rm {container.Name}", timeout: TimeSpan.FromSeconds(10));
        }

        return stopped.Count;
    }

    public string GetContainerName(string workspaceDir)
    {
        var normalized = NormalizePath(workspaceDir);
        var projectName = Path.GetFileName(normalized).ToLowerInvariant();

        // Sanitize project name for Docker container naming (only allow [a-z0-9_.-])
        projectName = new string(projectName.Select(c =>
            char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' ? c : '-').ToArray());

        if (string.IsNullOrEmpty(projectName))
            projectName = "workspace";

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
        var hashStr = Convert.ToHexString(hash)[..8].ToLowerInvariant();

        return $"{ContainerPrefix}-{projectName}-{hashStr}";
    }

    public void EnsureDockerfileExists()
    {
        var containerDir = Path.Combine(_configDirectory, "container");
        if (!_fileSystem.DirectoryExists(containerDir))
            Directory.CreateDirectory(containerDir);

        var dockerfilePath = Path.Combine(containerDir, "Dockerfile");
        if (!_fileSystem.FileExists(dockerfilePath))
        {
            _fileSystem.WriteAllText(dockerfilePath, $"{DockerfileHashPrefix}{ComputeDockerfileHash()}\n{DefaultDockerfile}");
        }

        // Always write scripts with Unix line endings (LF) — Windows CRLF breaks
        // the shebang line in Linux containers (#!/bin/bash\r → "file not found").
        var proxyPath = Path.Combine(containerDir, "host-proxy.sh");
        _fileSystem.WriteAllText(proxyPath, HostProxyScript.ReplaceLineEndings("\n"));

        var clipboardPath = Path.Combine(containerDir, "clipboard-shim.sh");
        _fileSystem.WriteAllText(clipboardPath, ClipboardShimScript.ReplaceLineEndings("\n"));
    }

    public DockerfileStatus CheckDockerfileStatus()
    {
        var dockerfilePath = Path.Combine(_configDirectory, "container", "Dockerfile");
        if (!_fileSystem.FileExists(dockerfilePath))
            return DockerfileStatus.Missing;

        var content = _fileSystem.ReadAllText(dockerfilePath);
        if (!content.StartsWith(DockerfileHashPrefix))
            return DockerfileStatus.UserModified;

        var newlineIdx = content.IndexOf('\n');
        if (newlineIdx < 0)
            return DockerfileStatus.UserModified;

        var existingHash = content[DockerfileHashPrefix.Length..newlineIdx].Trim();
        return existingHash == ComputeDockerfileHash()
            ? DockerfileStatus.UpToDate
            : DockerfileStatus.Stale;
    }

    public void UpdateDockerfileToLatest()
    {
        var containerDir = Path.Combine(_configDirectory, "container");
        if (!_fileSystem.DirectoryExists(containerDir))
            Directory.CreateDirectory(containerDir);

        var dockerfilePath = Path.Combine(containerDir, "Dockerfile");
        _fileSystem.WriteAllText(dockerfilePath, $"{DockerfileHashPrefix}{ComputeDockerfileHash()}\n{DefaultDockerfile}");
    }

    public async Task<bool> IsContainerConfigStaleAsync(string workspaceDir)
    {
        var containerName = GetContainerName(workspaceDir);
        var labels = await GetContainerLabelsAsync(containerName);

        // No label means container predates versioning — treat as stale
        if (!labels.TryGetValue("terminalhost.config-hash", out var storedHash))
            return true;

        return storedHash != ComputeConfigHash(workspaceDir);
    }

    public async Task<bool> IsContainerImageStaleAsync(string workspaceDir)
    {
        var containerName = GetContainerName(workspaceDir);
        var labels = await GetContainerLabelsAsync(containerName);

        if (!labels.TryGetValue("terminalhost.dockerfile-hash", out var storedHash))
            return true;

        return storedHash != ComputeDockerfileHash();
    }

    private const string DockerfileHashPrefix = "# terminalhost-dockerfile-hash:";

    internal static string ComputeDockerfileHash()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(DefaultDockerfile));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    internal string ComputeConfigHash(string workspaceDir)
    {
        var config = _configService.Load();
        var cs = config.Settings.Container;
        var refVols = GetReferenceVolumes(workspaceDir, config);

        var sb = new StringBuilder();
        sb.Append(cs.MountSsh);
        sb.Append(cs.MountGhCli);
        sb.Append(cs.NetworkMode);
        sb.Append(cs.AutoApproveInContainer);
        sb.Append(string.Join(",", cs.EnvVars.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        sb.Append(string.Join(",", refVols.OrderBy(v => v.Name).Select(v => $"{v.HostPath}:{v.Name}")));
        sb.Append(string.Join(",", cs.ExtraMounts.OrderBy(m => m.HostPath).Select(m => $"{m.HostPath}:{m.ContainerPath}:{m.Readonly}")));
        sb.Append(string.Join(",", cs.ExtraDockerArgs.OrderBy(a => a)));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private async Task<Dictionary<string, string>> GetContainerLabelsAsync(string containerName)
    {
        try
        {
            var dockerPath = GetDockerPath();
            var (exitCode, output, _) = await _processService.RunAsync(
                dockerPath,
                $"inspect -f \"{{{{json .Config.Labels}}}}\" {containerName}",
                timeout: TimeSpan.FromSeconds(10));

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return new Dictionary<string, string>();

            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(output.Trim())
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private async Task CreateContainerAsync(string workspaceDir, string containerName)
    {
        var config = _configService.Load();
        var cs = config.Settings.Container;
        var dockerPath = GetDockerPath();

        var args = new StringBuilder();
        args.Append($"run -d --name {containerName}");

        // Environment variables
        foreach (var (key, value) in cs.EnvVars)
        {
            args.Append($" -e \"{key}={value}\"");
        }

        // Forward all CLAUDE_CODE_* environment variables from the host
        foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(k => k.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase)))
        {
            args.Append($" -e \"{key}={Environment.GetEnvironmentVariable(key)}\"");
        }

        // TerminalHost API URL for host proxy communication
        // host.docker.internal resolves to the host machine from inside Docker Desktop containers
        var apiPort = config.Settings.Api.Port;
        args.Append($" -e \"TERMINALHOST_API=http://host.docker.internal:{apiPort}\"");

        // Path mapping env vars for host proxy to translate container paths → host paths.
        // Without these, hook payloads contain /workspace paths that mean nothing to TerminalHost.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        args.Append($" -e \"TERMINALHOST_HOST_WORKSPACE={workspaceDir}\"");
        args.Append($" -e \"TERMINALHOST_HOST_USERPROFILE={userProfile}\"");

        // Reference volume path mappings: TERMINALHOST_REF_<name>=<hostPath>
        var referenceVolumes = GetReferenceVolumes(workspaceDir, config);
        foreach (var vol in referenceVolumes)
        {
            if (_fileSystem.DirectoryExists(vol.HostPath))
            {
                // Sanitize name for env var (replace dots/hyphens with underscores)
                var envName = vol.Name.Replace('.', '_').Replace('-', '_');
                args.Append($" -e \"TERMINALHOST_REF_{envName}={vol.HostPath}\"");
            }
        }

        // Container workspace path preserves the folder name (e.g., /workspace/IT4C)
        // so tools like npm and Claude Code see the correct project name.
        var containerWs = GetContainerWorkspacePath(workspaceDir);

        // Working directory
        args.Append($" -w {containerWs}");

        // Container workspace path env var for host proxy path translation
        args.Append($" -e \"TERMINALHOST_CONTAINER_WORKSPACE={containerWs}\"");

        // Project directory (read-write) — mounted under /workspace/{folderName}
        args.Append($" -v \"{workspaceDir}:{containerWs}\"");

        // Git worktree support: a worktree's .git is a file containing "gitdir: <path>"
        // pointing to the main repo's .git/worktrees/<name>. That path is outside the
        // mounted workspace, so git commands fail inside the container.
        // Fix: mount the main .git directory and overlay the .git file with a container-side path.
        var dotGitPath = Path.Combine(workspaceDir, ".git");
        if (_fileSystem.FileExists(dotGitPath))
        {
            var gitFileContent = _fileSystem.ReadAllText(dotGitPath).Trim();
            if (gitFileContent.StartsWith("gitdir:"))
            {
                var gitDir = gitFileContent.Substring(7).Trim();
                if (!Path.IsPathRooted(gitDir))
                    gitDir = Path.GetFullPath(Path.Combine(workspaceDir, gitDir));

                // gitDir is e.g. /path/to/main-repo/.git/worktrees/<name>
                // The main .git directory is two levels up from worktrees/<name>
                var mainGitDir = Path.GetFullPath(Path.Combine(gitDir, "..", ".."));
                if (_fileSystem.DirectoryExists(mainGitDir))
                {
                    // Mount the main .git directory so all objects/refs/worktree data is accessible
                    args.Append($" -v \"{mainGitDir}:/workspace/.git-main\"");

                    // Create a temporary .git file with the container-side gitdir path
                    // and mount it as a file overlay over the workspace's .git file.
                    // This avoids modifying the host's .git file through the bind mount.
                    var relativePath = Path.GetRelativePath(mainGitDir, gitDir).Replace('\\', '/');
                    var containerGitDir = $"/workspace/.git-main/{relativePath}";
                    var overlayGitFile = Path.Combine(_configDirectory, $"container-worktree-{containerName}");
                    File.WriteAllText(overlayGitFile, $"gitdir: {containerGitDir}\n");
                    args.Append($" -v \"{overlayGitFile}:{containerWs}/.git\"");
                }
            }
        }

        // Overlay .mcp.json with localhost → host.docker.internal so MCP servers
        // registered by TerminalHost resolve correctly from inside the container.
        var containerMcpJson = GenerateContainerMcpJson(workspaceDir);
        if (containerMcpJson != null)
            args.Append($" -v \"{containerMcpJson}:{containerWs}/.mcp.json:ro\"");

        // Claude Code config directories (read-write for sharing with host)
        var claudeDir = Path.Combine(userProfile, ".claude");
        var claudeJson = Path.Combine(userProfile, ".claude.json");

        if (_fileSystem.DirectoryExists(claudeDir))
            args.Append($" -v \"{claudeDir}:{ContainerHome}/.claude\"");

        if (_fileSystem.FileExists(claudeJson))
        {
            // Generate overlay with localhost → host.docker.internal for MCP server URLs,
            // and convert eidet stdio MCP to HTTP (host binary can't run in container)
            var containerClaudeJson = GenerateContainerClaudeJson(claudeJson, workspaceDir);
            args.Append($" -v \"{containerClaudeJson ?? claudeJson}:{ContainerHome}/.claude.json\"");
        }

        // Overlay mount: map the host's project session directory to where the container
        // will look for it. Claude Code stores sessions under ~/.claude/projects/{encoded-path}/
        // where {encoded-path} replaces path separators with dashes.
        // Host: P:\HC → P--HC, Container: /workspace/HC → -workspace-HC
        // By mounting P--HC at -workspace-HC, sessions/memory/tasks all land in the right place.
        var hostProjectKey = EncodeClaudeProjectPath(workspaceDir);
        var containerProjectKey = EncodeClaudeProjectPath(containerWs);
        var hostProjectDir = Path.Combine(claudeDir, "projects", hostProjectKey);

        // Create the host project directory if it doesn't exist yet
        // (first-time container for a project that hasn't been opened with Claude before)
        if (!_fileSystem.DirectoryExists(hostProjectDir))
            Directory.CreateDirectory(hostProjectDir);

        // Specific mount overrides the broader ~/.claude mount (Docker mount precedence)
        args.Append($" -v \"{hostProjectDir}:{ContainerHome}/.claude/projects/{containerProjectKey}\"");

        // Generate a container-specific CLAUDE.md that overlays ~/.claude/CLAUDE.md.
        // This preserves the host's global instructions and appends container context
        // (environment, tools, reference volumes). File mount overrides the dir mount.
        var containerClaudeMd = GenerateContainerClaudeMd(
            claudeDir, workspaceDir, referenceVolumes.Where(v => _fileSystem.DirectoryExists(v.HostPath)).ToList());
        if (containerClaudeMd != null)
            args.Append($" -v \"{containerClaudeMd}:{ContainerHome}/.claude/CLAUDE.md:ro\"");

        // Generate container-specific settings.local.json overlay for sandbox seccomp paths.
        // Claude Code can't find NVM's global packages, so we point it to the fixed copy.
        var containerSettings = GenerateContainerSettings();
        if (containerSettings != null)
            args.Append($" -v \"{containerSettings}:{ContainerHome}/.claude/settings.local.json:ro\"");

        // Generate container-specific plugin overlays — known_marketplaces.json and
        // installed_plugins.json contain Windows absolute paths that are invalid in Linux.
        // We convert them to container paths and mount as file overlays.
        var pluginOverlays = GenerateContainerPluginOverlays(claudeDir);
        foreach (var (hostPath, containerPath) in pluginOverlays)
            args.Append($" -v \"{hostPath}:{containerPath}:ro\"");

        // Git config (readonly)
        var gitConfig = Path.Combine(userProfile, ".gitconfig");
        if (_fileSystem.FileExists(gitConfig))
            args.Append($" -v \"{gitConfig}:{ContainerHome}/.gitconfig:ro\"");

        // GPG keys for commit signing — mount to staging path, then copy with correct
        // ownership after start. Direct mount fails because Windows ownership appears as
        // root, and GPG refuses "unsafe ownership on homedir".
        var gnupgDir = Path.Combine(userProfile, ".gnupg");
        if (_fileSystem.DirectoryExists(gnupgDir))
            args.Append($" -v \"{gnupgDir}:/mnt/gnupg-host:ro\"");

        // SSH keys (optional, readonly)
        if (cs.MountSsh)
        {
            var sshDir = Path.Combine(userProfile, ".ssh");
            if (_fileSystem.DirectoryExists(sshDir))
                args.Append($" -v \"{sshDir}:{ContainerHome}/.ssh:ro\"");
        }

        // Share host ~/.config for tools that store settings there (e.g., ccstatusline)
        var hostConfigDir = Path.Combine(userProfile, ".config");
        if (_fileSystem.DirectoryExists(hostConfigDir))
            args.Append($" -v \"{hostConfigDir}:{ContainerHome}/.config\"");

        // Reference volumes (readonly) — reuse the list from env var setup above
        foreach (var vol in referenceVolumes)
        {
            if (_fileSystem.DirectoryExists(vol.HostPath))
                args.Append($" -v \"{vol.HostPath}:/refs/{vol.Name}:ro\"");
        }

        // Extra mounts
        foreach (var mount in cs.ExtraMounts)
        {
            var roFlag = mount.Readonly ? ":ro" : "";
            args.Append($" -v \"{mount.HostPath}:{mount.ContainerPath}{roFlag}\"");
        }

        // Allow bubblewrap (bwrap) to create user namespaces inside the container.
        // Ubuntu 24.04 restricts unprivileged user namespaces via AppArmor, causing
        // "No permissions to create new namespace" errors in Claude Code's Bash sandbox.
        args.Append(" --security-opt apparmor=unconfined");

        // Network mode
        if (!string.IsNullOrEmpty(cs.NetworkMode) && cs.NetworkMode != "bridge")
            args.Append($" --network {cs.NetworkMode}");

        // Extra docker args
        foreach (var arg in cs.ExtraDockerArgs)
            args.Append($" {arg}");

        // Versioning labels for staleness detection
        args.Append($" --label terminalhost.config-hash={ComputeConfigHash(workspaceDir)}");
        args.Append($" --label terminalhost.dockerfile-hash={ComputeDockerfileHash()}");
        args.Append($" --label terminalhost.workspace-dir={workspaceDir}");

        // Image and command (sleep infinity to keep container alive)
        args.Append($" {cs.ImageName}:{cs.ImageTag} sleep infinity");

        var (exitCode, _, error) = await _processService.RunAsync(
            dockerPath, args.ToString(),
            timeout: TimeSpan.FromSeconds(30));

        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to create container: {error}");
    }

    /// <summary>
    /// Marks the workspace as a safe directory for git inside the container.
    /// Required for worktrees where the .git directory is mounted separately.
    /// </summary>
    private async Task InitWorktreeGitLinkAsync(string containerName, string workspaceDir)
    {
        try
        {
            var dotGitPath = Path.Combine(workspaceDir, ".git");
            if (!_fileSystem.FileExists(dotGitPath))
                return;

            var gitFileContent = _fileSystem.ReadAllText(dotGitPath).Trim();
            if (!gitFileContent.StartsWith("gitdir:"))
                return;

            var dockerPath = GetDockerPath();
            var containerWs = GetContainerWorkspacePath(workspaceDir);

            // Mark the workspace as safe for git (ownership may differ due to bind mounts)
            await _processService.RunAsync(
                dockerPath,
                $"exec {containerName} git config --global --add safe.directory {containerWs}",
                timeout: TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best-effort
        }
    }

    /// <summary>
    /// Fixes ownership of workspace contents so the developer user can write.
    /// Windows bind mounts appear as root:root inside the container, causing EACCES errors
    /// for tools like npm that need to create/modify files (e.g., node_modules).
    /// Runs chown as root, skips if no root-owned files are found.
    /// </summary>
    private async Task InitWorkspaceOwnershipAsync(string containerName, string workspaceDir)
    {
        try
        {
            var dockerPath = GetDockerPath();
            var containerWs = GetContainerWorkspacePath(workspaceDir);

            // Quick check: are there any root-owned files? (depth-limited for speed)
            var (checkExit, output, _) = await _processService.RunAsync(
                dockerPath,
                $"exec {containerName} find {containerWs} -maxdepth 3 -user root -not -path {containerWs} -print -quit",
                timeout: TimeSpan.FromSeconds(5));

            if (checkExit != 0 || string.IsNullOrWhiteSpace(output?.Trim()))
                return; // No root-owned files found

            // Fix ownership recursively. Must run as root since developer can't chown root-owned files.
            // On Docker Desktop VirtioFS this makes files appear developer-owned (satisfies permission checks).
            // On Docker Engine/WSL2 this genuinely fixes the filesystem permissions.
            await _processService.RunAsync(
                dockerPath,
                $"exec -u root {containerName} chown -R developer:developer {containerWs}",
                timeout: TimeSpan.FromSeconds(120));
        }
        catch
        {
            // Workspace ownership fix is best-effort — some Docker configurations
            // may not support chown on bind mounts (e.g., read-only mounts).
        }
    }

    /// <summary>
    /// Copies GPG keys from the staging mount (/mnt/gnupg-host) into the container's
    /// home directory with correct ownership and permissions. Skips if no keys mounted.
    /// </summary>
    private async Task InitGpgKeysAsync(string containerName)
    {
        try
        {
            var dockerPath = GetDockerPath();

            // Check if the staging mount exists
            var (checkExit, _, _) = await _processService.RunAsync(
                dockerPath, $"exec {containerName} test -d /mnt/gnupg-host",
                timeout: TimeSpan.FromSeconds(5));

            if (checkExit != 0)
                return; // No GPG keys mounted

            // Copy keys, fix ownership/permissions, remove all stale host artifacts.
            // Must clean: agent sockets, *.lock files, public-keys.d/*.lock, and
            // dotlock files (.#lk0x*) — all contain host PIDs that cause "waiting for lock" hangs.
            // Also kill any stale gpg-agent inherited from the copy.
            await _processService.RunAsync(
                dockerPath,
                $"exec {containerName} sh -c \"" +
                $"rm -rf {ContainerHome}/.gnupg && " +
                $"cp -r /mnt/gnupg-host {ContainerHome}/.gnupg && " +
                $"chmod 700 {ContainerHome}/.gnupg && " +
                $"find {ContainerHome}/.gnupg -type f -exec chmod 600 {{}} \\; && " +
                $"rm -f {ContainerHome}/.gnupg/S.gpg-agent* && " +
                $"find {ContainerHome}/.gnupg -name '*.lock' -delete && " +
                $"find {ContainerHome}/.gnupg -name '.#lk*' -delete && " +
                $"gpgconf --kill gpg-agent 2>/dev/null; true" +
                $"\"",
                timeout: TimeSpan.FromSeconds(10));
        }
        catch
        {
            // GPG key setup is best-effort
        }
    }

    /// <summary>
    /// Extracts the GitHub CLI auth token from the host (via `gh auth token`) and writes
    /// it into the container's ~/.config/gh/hosts.yml. This is necessary because Windows
    /// stores the token in Credential Manager (keyring), not in the hosts.yml file, so
    /// simply mounting the config directory doesn't transfer the auth session.
    /// </summary>
    private async Task InitGhCliAsync(string containerName)
    {
        try
        {
            var config = _configService.Load();
            if (!config.Settings.Container.MountGhCli)
                return;

            // Extract token and username from host's gh CLI.
            // gh auth token prints the token regardless of storage backend (keyring, file, env).
            var (tokenExit, token, _) = await _processService.RunAsync(
                "gh", "auth token",
                timeout: TimeSpan.FromSeconds(10));

            if (tokenExit != 0 || string.IsNullOrWhiteSpace(token))
                return; // gh not installed or not authenticated on host

            token = token.Trim();

            // Get the username
            var (userExit, user, _) = await _processService.RunAsync(
                "gh", "api user --jq .login",
                timeout: TimeSpan.FromSeconds(10));

            var username = userExit == 0 && !string.IsNullOrWhiteSpace(user)
                ? user.Trim()
                : "";

            // Get git protocol preference from host config
            var ghConfigDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GitHub CLI");
            var hostsPath = Path.Combine(ghConfigDir, "hosts.yml");
            var gitProtocol = "https";
            if (_fileSystem.FileExists(hostsPath))
            {
                var hostsContent = _fileSystem.ReadAllText(hostsPath);
                if (hostsContent.Contains("git_protocol: ssh"))
                    gitProtocol = "ssh";
            }

            // Build hosts.yml with the actual token embedded (not keyring reference).
            // YAML format that gh expects on Linux.
            var hostsYaml = $"github.com:\\n" +
                            $"    git_protocol: {gitProtocol}\\n" +
                            $"    oauth_token: {token}\\n";
            if (!string.IsNullOrEmpty(username))
            {
                hostsYaml += $"    users:\\n" +
                             $"        {username}:\\n" +
                             $"            oauth_token: {token}\\n" +
                             $"    user: {username}\\n";
            }

            var dockerPath = GetDockerPath();
            await _processService.RunAsync(
                dockerPath,
                $"exec -u root {containerName} sh -c \"" +
                $"mkdir -p {ContainerHome}/.config/gh && " +
                $"printf '{hostsYaml}' > {ContainerHome}/.config/gh/hosts.yml && " +
                $"chown -R developer:developer {ContainerHome}/.config/gh" +
                $"\"",
                timeout: TimeSpan.FromSeconds(10));
        }
        catch
        {
            // gh CLI config setup is best-effort
        }
    }

    private async Task StartContainerAsync(string containerName)
    {
        var dockerPath = GetDockerPath();
        var (exitCode, _, error) = await _processService.RunAsync(
            dockerPath, $"start {containerName}",
            timeout: TimeSpan.FromSeconds(15));

        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to start container: {error}");
    }

    /// <summary>
    /// Generates a container settings.local.json with sandbox seccomp paths.
    /// </summary>
    private string? GenerateContainerSettings()
    {
        try
        {
            var containerDir = Path.Combine(_configDirectory, "container");
            if (!_fileSystem.DirectoryExists(containerDir))
                Directory.CreateDirectory(containerDir);

            // Merge with host's settings.local.json if it exists
            var hostSettings = new Dictionary<string, object>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var hostSettingsPath = Path.Combine(userProfile, ".claude", "settings.local.json");
            if (_fileSystem.FileExists(hostSettingsPath))
            {
                var existing = _fileSystem.ReadAllText(hostSettingsPath);
                hostSettings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(existing)
                    ?? new Dictionary<string, object>();
            }

            // Add sandbox seccomp paths pointing to the fixed copy in the container
            hostSettings["sandbox.seccomp.bpfPath"] = "/usr/local/share/sandbox-seccomp/bpf";
            hostSettings["sandbox.seccomp.applyPath"] = "/usr/local/share/sandbox-seccomp/apply";

            var json = System.Text.Json.JsonSerializer.Serialize(hostSettings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            var settingsPath = Path.Combine(containerDir, "settings.local.json");
            _fileSystem.WriteAllText(settingsPath, json);
            return settingsPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a container-specific .mcp.json overlay that replaces localhost URLs
    /// with host.docker.internal so HTTP MCP servers resolve correctly from inside the container.
    /// </summary>
    private string? GenerateContainerMcpJson(string workspaceDir)
    {
        try
        {
            var mcpJsonPath = Path.Combine(workspaceDir, ".mcp.json");
            if (!_fileSystem.FileExists(mcpJsonPath))
                return null;

            var content = _fileSystem.ReadAllText(mcpJsonPath);

            // Replace localhost/127.0.0.1 with host.docker.internal in URLs
            var converted = content
                .Replace("http://localhost:", "http://host.docker.internal:")
                .Replace("http://127.0.0.1:", "http://host.docker.internal:")
                .Replace("https://localhost:", "https://host.docker.internal:")
                .Replace("https://127.0.0.1:", "https://host.docker.internal:");

            if (converted == content)
                return null; // No changes needed

            var containerDir = Path.Combine(_configDirectory, "container");
            if (!_fileSystem.DirectoryExists(containerDir))
                Directory.CreateDirectory(containerDir);

            // Include workspace folder name in overlay filename for multi-project support
            var folderName = Path.GetFileName(workspaceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var overlayPath = Path.Combine(containerDir, $"mcp-{folderName}.json");
            _fileSystem.WriteAllText(overlayPath, converted);
            return overlayPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a container-specific .claude.json overlay that replaces localhost URLs
    /// with host.docker.internal, converts Windows paths, and converts stdio MCP servers
    /// (like eidet) to HTTP-based entries since host binaries can't run in the container.
    /// </summary>
    private string? GenerateContainerClaudeJson(string hostClaudeJsonPath, string workspaceDir)
    {
        try
        {
            var containerDir = Path.Combine(_configDirectory, "container");
            if (!_fileSystem.DirectoryExists(containerDir))
                Directory.CreateDirectory(containerDir);

            var content = _fileSystem.ReadAllText(hostClaudeJsonPath);

            // Replace localhost/127.0.0.1 with host.docker.internal in URLs
            var converted = content
                .Replace("http://localhost:", "http://host.docker.internal:")
                .Replace("http://127.0.0.1:", "http://host.docker.internal:")
                .Replace("https://localhost:", "https://host.docker.internal:")
                .Replace("https://127.0.0.1:", "https://host.docker.internal:");

            // Parse JSON for structural transformations
            var node = System.Text.Json.Nodes.JsonNode.Parse(converted);
            if (node != null)
            {
                // Convert eidet stdio MCP entry → HTTP URL entry
                // The host's eidet dotnet tool can't run in the container, but
                // the Eidet service on the host exposes MCP at POST /mcp
                ConvertEidetMcpToHttp(node, workspaceDir);

                // Convert Windows paths for other stdio MCP servers
                ConvertWindowsPathsInJson(node);
                converted = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }

            var folderName = Path.GetFileName(workspaceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var overlayPath = Path.Combine(containerDir, $"claude-{folderName}.json");
            _fileSystem.WriteAllText(overlayPath, converted);
            return overlayPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates container-specific overlays for plugin JSON files whose paths reference
    /// the Windows host filesystem. Converts Windows user-home paths to container paths.
    /// Returns a list of (hostOverlayPath, containerMountPath) tuples.
    /// </summary>
    private List<(string HostPath, string ContainerPath)> GenerateContainerPluginOverlays(string claudeDir)
    {
        var overlays = new List<(string, string)>();
        try
        {
            var containerDir = Path.Combine(_configDirectory, "container");
            if (!_fileSystem.DirectoryExists(containerDir))
                Directory.CreateDirectory(containerDir);

            var pluginsDir = Path.Combine(claudeDir, "plugins");
            if (!_fileSystem.DirectoryExists(pluginsDir))
                return overlays;

            // Files that contain absolute Windows paths needing conversion
            var filesToOverlay = new[] { "known_marketplaces.json", "installed_plugins.json" };

            foreach (var fileName in filesToOverlay)
            {
                var hostFilePath = Path.Combine(pluginsDir, fileName);
                if (!_fileSystem.FileExists(hostFilePath))
                    continue;

                var content = _fileSystem.ReadAllText(hostFilePath);

                // Parse JSON, walk all string values, convert Windows paths to container paths
                var node = System.Text.Json.Nodes.JsonNode.Parse(content);
                if (node == null) continue;

                ConvertWindowsPathsInJson(node);

                var converted = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var overlayPath = Path.Combine(containerDir, $"plugins-{fileName}");
                _fileSystem.WriteAllText(overlayPath, converted);
                overlays.Add((overlayPath, $"{ContainerHome}/.claude/plugins/{fileName}"));
            }
        }
        catch
        {
            // Don't fail container creation over plugin path conversion
        }
        return overlays;
    }

    /// <summary>
    /// Converts eidet stdio MCP entry to HTTP URL entry in the JSON tree.
    /// The host's eidet dotnet tool can't run in the container, but the Eidet service exposes
    /// MCP at POST /mcp. The repo query parameter carries the host path's repoId
    /// so memories land in the correct repo regardless of the container mount path.
    /// </summary>
    private void ConvertEidetMcpToHttp(System.Text.Json.Nodes.JsonNode root, string workspaceDir)
    {
        var servers = root["mcpServers"]?.AsObject();
        if (servers is null || !servers.ContainsKey("eidet")) return;

        var eidetEntry = servers["eidet"]?.AsObject();
        if (eidetEntry is null || !eidetEntry.ContainsKey("command")) return;

        // It's a stdio entry — convert to HTTP
        var config = _configService.Load();
        var eidetUrl = config.Settings.Memory.EidetUrl.TrimEnd('/');
        var hostUrl = eidetUrl
            .Replace("http://localhost:", "http://host.docker.internal:")
            .Replace("http://127.0.0.1:", "http://host.docker.internal:");

        var repoId = RepoIdNormalizer.Normalize(workspaceDir);

        servers.Remove("eidet");
        servers["eidet"] = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "http",
            ["url"] = $"{hostUrl}/mcp?repo={Uri.EscapeDataString(repoId)}",
        };
    }

    /// <summary>
    /// Recursively walks a JSON tree and converts Windows absolute paths to container paths.
    /// </summary>
    private static void ConvertWindowsPathsInJson(System.Text.Json.Nodes.JsonNode node)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var prop in obj.ToList())
            {
                if (prop.Value is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var str))
                {
                    var converted = ConvertWindowsPathToContainer(str);
                    if (converted != str)
                        obj[prop.Key] = converted;
                }
                else if (prop.Value != null)
                {
                    ConvertWindowsPathsInJson(prop.Value);
                }
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var str))
                {
                    var converted = ConvertWindowsPathToContainer(str);
                    if (converted != str)
                        arr[i] = converted;
                }
                else if (arr[i] != null)
                {
                    ConvertWindowsPathsInJson(arr[i]!);
                }
            }
        }
    }

    /// <summary>
    /// Converts a Windows path containing .claude to the equivalent container path.
    /// E.g., C:\Users\steve\.claude\plugins\... → /home/developer/.claude/plugins/...
    /// </summary>
    private static string ConvertWindowsPathToContainer(string value)
    {
        // Only convert Windows absolute paths (drive letter + colon + backslash)
        if (value.Length < 3 || !char.IsLetter(value[0]) || value[1] != ':' || value[2] != '\\')
            return value;

        // Find .claude\ segment and replace everything before it with container home
        var idx = value.IndexOf("\\.claude\\", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var remainder = value.Substring(idx + 8); // skip \.claude\
            return ContainerHome + "/.claude/" + remainder.Replace('\\', '/');
        }

        return value;
    }

    /// <summary>
    /// Generates a container-specific CLAUDE.md that includes the host's global instructions
    /// plus container context. Written to a host temp file and overlay-mounted into the container.
    /// </summary>
    private string? GenerateContainerClaudeMd(string claudeDir, string workspaceDir, List<ReferenceVolume> mountedVolumes)
    {
        try
        {
            var sb = new StringBuilder();

            // Preserve the host's global CLAUDE.md content
            var hostClaudeMd = Path.Combine(claudeDir, "CLAUDE.md");
            if (_fileSystem.FileExists(hostClaudeMd))
            {
                sb.AppendLine(_fileSystem.ReadAllText(hostClaudeMd).TrimEnd());
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            // Container context section
            sb.AppendLine("# Container Environment");
            sb.AppendLine();
            var containerWs = GetContainerWorkspacePath(workspaceDir);
            sb.AppendLine("You are running inside a Docker container managed by TerminalHost.");
            sb.AppendLine($"The project workspace is mounted at `{containerWs}` from the host.");
            sb.AppendLine();

            // Installed tools
            sb.AppendLine("## Pre-installed Tools");
            sb.AppendLine();
            sb.AppendLine("- **Node.js 22** (via NVM) — `node`, `npm`, `npx`");
            sb.AppendLine("- **Bun** — `bun`, `bunx`");
            sb.AppendLine("- **.NET 8/9/10** — `dotnet`");
            sb.AppendLine("- **Python 3** — `python3`, `pip3`");
            sb.AppendLine("- **Build tools** — `git`, `curl`, `wget`, `jq`, `ripgrep`, `tree`, `vim`");
            sb.AppendLine();

            // Reference volumes
            if (mountedVolumes.Count > 0)
            {
                sb.AppendLine("## Reference Libraries (readonly)");
                sb.AppendLine();
                sb.AppendLine("The following host directories are mounted read-only for reference.");
                sb.AppendLine("Use the container path when reading files.");
                sb.AppendLine();
                foreach (var vol in mountedVolumes)
                {
                    sb.AppendLine($"- `/refs/{vol.Name}` ← host: `{vol.HostPath}`");
                }
                sb.AppendLine();
            }

            // Path mapping note
            sb.AppendLine("## Path Mapping");
            sb.AppendLine();
            sb.AppendLine($"- `{containerWs}` ← host: `{workspaceDir}`");
            sb.AppendLine($"- `~` = `{ContainerHome}` (non-root user: `developer`)");
            sb.AppendLine();

            // Write to a per-workspace file (each workspace has different refs/paths)
            var containerDir = Path.Combine(_configDirectory, "container");
            if (!_fileSystem.DirectoryExists(containerDir))
                Directory.CreateDirectory(containerDir);

            var normalizedDir = NormalizePath(workspaceDir);
            var dirHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDir)))[..8].ToLowerInvariant();
            var claudeMdPath = Path.Combine(containerDir, $"CLAUDE-{dirHash}.md");
            _fileSystem.WriteAllText(claudeMdPath, sb.ToString());
            return claudeMdPath;
        }
        catch
        {
            return null;
        }
    }

    private List<ReferenceVolume> GetReferenceVolumes(string workspaceDir, AppConfiguration config)
    {
        var normalizedPath = NormalizePath(workspaceDir);

        // Check per-directory override
        if (config.DirectorySettings.TryGetValue(normalizedPath, out var dirSettings)
            && dirSettings.ContainerReferenceVolumes != null)
        {
            return dirSettings.ContainerReferenceVolumes;
        }

        // Fall back to global
        return config.Settings.Container.ReferenceVolumes;
    }

    /// <summary>
    /// Exports Claude Code credentials from macOS Keychain to ~/.claude/.credentials.json.
    /// On macOS, credentials are stored in the Keychain and not available as a file,
    /// so the container can't access them via the bind mount.
    /// </summary>
    private async Task ExportMacOsCredentialsAsync(string userProfile)
    {
        try
        {
            var credentialsPath = Path.Combine(userProfile, ".claude", ".credentials.json");

            // Export from Keychain
            var (exitCode, output, _) = await _processService.RunAsync(
                "security", "find-generic-password -s \"Claude Code-credentials\" -w",
                timeout: TimeSpan.FromSeconds(10));

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Ensure .claude directory exists
                var claudeDir = Path.Combine(userProfile, ".claude");
                if (!_fileSystem.DirectoryExists(claudeDir))
                    Directory.CreateDirectory(claudeDir);

                _fileSystem.WriteAllText(credentialsPath, output.Trim());

                // chmod 600 — owner read/write only
                await _processService.RunAsync("chmod", $"600 \"{credentialsPath}\"",
                    timeout: TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            // Best-effort: if Keychain access fails, container will prompt for login
        }
    }

    private string GetDockerPath()
    {
        var config = _configService.Load();
        var path = config.Settings.Container.DockerPath;

        // On macOS, GUI apps don't inherit the shell's PATH.
        // If the configured path is just "docker" (no full path), resolve to common locations.
        if (OperatingSystem.IsMacOS() && path == "docker")
        {
            var candidates = new[]
            {
                "/usr/local/bin/docker",
                "/opt/homebrew/bin/docker",
                "/Applications/Docker.app/Contents/Resources/bin/docker",
            };
            foreach (var candidate in candidates)
            {
                if (_fileSystem.FileExists(candidate))
                    return candidate;
            }
        }

        return path;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

    /// <summary>
    /// Encodes a path the same way Claude Code does for ~/.claude/projects/ directory names.
    /// Path separators (\, /, :) are replaced with dashes.
    /// Examples: "P:\HC" → "P--HC", "/workspace/HC" → "-workspace-HC"
    /// </summary>
    public static string EncodeClaudeProjectPath(string path)
    {
        // Claude Code replaces path separators, dots, and underscores with dashes
        return path
            .Replace(':', '-')
            .Replace('\\', '-')
            .Replace('/', '-')
            .Replace('.', '-')
            .Replace('_', '-');
    }

    /// <summary>
    /// Home directory for the non-root container user.
    /// Must match the user created in the Dockerfile.
    /// </summary>
    internal const string ContainerHome = "/home/developer";

    /// <summary>
    /// Get the container-side workspace path for a host directory.
    /// Preserves the folder name so tools (npm, Claude Code) see the correct project name.
    /// E.g., "P:\IT4C" → "/workspace/IT4C", "P:\MyProject" → "/workspace/MyProject"
    /// </summary>
    internal static string GetContainerWorkspacePath(string workspaceDir)
    {
        var trimmed = workspaceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmed);

        // Fallback for drive roots like "P:\" where GetFileName returns ""
        if (string.IsNullOrEmpty(folderName))
            folderName = trimmed.Replace(":", "").Replace("\\", "").Replace("/", "");

        return $"/workspace/{folderName}";
    }

    private const string DefaultDockerfile = """
        FROM ubuntu:24.04

        ENV DEBIAN_FRONTEND=noninteractive
        ENV TZ=Europe/Brussels

        # System essentials (as root)
        RUN apt-get update && apt-get install -y \
            build-essential git curl wget unzip ca-certificates gnupg \
            libicu-dev libssl-dev zlib1g-dev libffi-dev vim tree jq ripgrep \
            python3 python3-pip python3-venv \
            bubblewrap socat \
            && rm -rf /var/lib/apt/lists/*

        # GitHub CLI (system-wide, as root)
        RUN (curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg) \
            && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
            && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
                > /etc/apt/sources.list.d/github-cli.list \
            && apt-get update && apt-get install -y gh && rm -rf /var/lib/apt/lists/*

        # .NET SDKs (system-wide, as root)
        RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
            && chmod +x /tmp/dotnet-install.sh \
            && /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
            && /tmp/dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet \
            && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
            && ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet \
            && rm /tmp/dotnet-install.sh
        ENV DOTNET_ROOT=/usr/share/dotnet
        ENV PATH="$DOTNET_ROOT:$PATH"

        # Host proxy (as root, before switching user)
        COPY host-proxy.sh /usr/local/bin/host.exe
        RUN chmod +x /usr/local/bin/host.exe

        # Clipboard bridge — shim that proxies clipboard ops to TerminalHost's REST API.
        # Symlinked as xclip/xsel/wl-paste/wl-copy so any tool using standard Linux
        # clipboard commands transparently reads/writes the host clipboard.
        COPY clipboard-shim.sh /usr/local/bin/clipboard-shim
        RUN chmod +x /usr/local/bin/clipboard-shim \
            && ln -sf clipboard-shim /usr/local/bin/xclip \
            && ln -sf clipboard-shim /usr/local/bin/xsel \
            && ln -sf clipboard-shim /usr/local/bin/wl-paste \
            && ln -sf clipboard-shim /usr/local/bin/wl-copy \
            && ln -sf clipboard-shim /usr/local/bin/pbcopy \
            && ln -sf clipboard-shim /usr/local/bin/pbpaste

        # Git: trust all mounted directories (as root, in system config so it
        # survives the readonly ~/.gitconfig mount from host)
        RUN git config --system --add safe.directory '*'

        # Override Windows-specific git config that comes from the host's
        # readonly .gitconfig mount. GIT_CONFIG_COUNT env vars have the
        # HIGHEST precedence, above all file-based git config.
        # 0: gpg.program — host path (C:\Program Files\...) doesn't exist in Linux
        # 1: core.autocrlf — Windows CRLF files mounted into Linux cause phantom diffs
        ENV GIT_CONFIG_COUNT=2
        ENV GIT_CONFIG_KEY_0=gpg.program
        ENV GIT_CONFIG_VALUE_0=gpg
        ENV GIT_CONFIG_KEY_1=core.autocrlf
        ENV GIT_CONFIG_VALUE_1=true

        # Create non-root user (required for Claude Code --dangerously-skip-permissions)
        RUN useradd -m -s /bin/bash developer \
            && mkdir -p /workspace && chown developer:developer /workspace
        USER developer

        # Node.js 22 via NVM (as developer)
        ENV NVM_DIR=/home/developer/.nvm
        RUN curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.7/install.sh | bash \
            && . "$NVM_DIR/nvm.sh" && nvm install 22 && nvm use 22

        # Bun (as developer)
        RUN curl -fsSL https://bun.sh/install | bash
        ENV BUN_INSTALL=/home/developer/.bun
        ENV PATH="$BUN_INSTALL/bin:$PATH"

        # Claude Code (as developer — needs node via NVM)
        ENV PATH="/home/developer/.local/bin:$PATH"
        RUN . "$NVM_DIR/nvm.sh" && curl -fsSL https://claude.ai/install.sh | bash

        # Claude Code sandbox dependencies (seccomp filter for bwrap)
        # Install package then copy seccomp filters to a fixed path — Claude Code
        # uses its own node runtime and can't find NVM's global packages.
        RUN . "$NVM_DIR/nvm.sh" && npm install -g @anthropic-ai/sandbox-runtime \
            && SECCOMP_SRC="$(npm root -g)/@anthropic-ai/sandbox-runtime/vendor/seccomp" \
            && mkdir -p /usr/local/share/sandbox-seccomp \
            && cp -r "$SECCOMP_SRC/"* /usr/local/share/sandbox-seccomp/ 2>/dev/null || true

        # .NET global tools
        RUN dotnet tool install --global HC.Dev \
            && dotnet tool install --global HC.SafeCommands \
            && dotnet tool install --global dotnet-outdated-tool \
            && dotnet tool install --global SqlInliner \
            && dotnet tool install --global AsicSharp.Cli
        ENV PATH="$PATH:/home/developer/.dotnet/tools"

        # Shell prompt + NVM in bash
        RUN echo 'PS1="[container] \\w\\$ "' >> /home/developer/.bashrc \
            && echo '. "$NVM_DIR/nvm.sh"' >> /home/developer/.bashrc

        WORKDIR /workspace
        CMD ["/bin/bash"]
        """;

    private const string HostProxyScript = """
        #!/bin/bash
        # host.exe proxy: forwards commands from inside the container to the
        # TerminalHost REST API running on the host machine.
        #
        # Path translation: container paths are rewritten to host paths so that
        # TerminalHost can resolve files correctly. The mapping env vars are set
        # automatically by TerminalHost when creating the container:
        #   TERMINALHOST_CONTAINER_WORKSPACE — container path (e.g., /workspace/IT4C)
        #   TERMINALHOST_HOST_WORKSPACE      — host path for workspace (e.g., P:\IT4C)
        #   TERMINALHOST_HOST_USERPROFILE    — host path for /home/developer (e.g., C:\Users\steve)
        #   TERMINALHOST_REF_<name>          — host path for /refs/<name>

        API_URL="${TERMINALHOST_API:-http://host.docker.internal:19280}"
        CONTAINER_WS="${TERMINALHOST_CONTAINER_WORKSPACE:-/workspace}"
        HOST_WS="${TERMINALHOST_HOST_WORKSPACE}"
        HOST_UP="${TERMINALHOST_HOST_USERPROFILE}"

        # Convert Windows backslash paths to forward slashes for JSON safety.
        # Windows accepts both P:\HC and P:/HC, but only P:/HC is valid JSON.
        fwd_path() {
            printf '%s' "${1//\\//}"
        }

        translate_paths() {
            local data="$1"

            # Translate container workspace path → host workspace path (forward slashes)
            # e.g., /workspace/IT4C → P:/IT4C
            if [ -n "$HOST_WS" ]; then
                local safe
                safe=$(fwd_path "$HOST_WS")
                data="${data//${CONTAINER_WS}/${safe}}"
            fi

            # Translate /home/developer → host user profile (forward slashes)
            # e.g., /home/developer/.claude → C:/Users/steve/.claude
            if [ -n "$HOST_UP" ]; then
                local safe
                safe=$(fwd_path "$HOST_UP")
                data="${data//\/home\/developer/${safe}}"
            fi

            # Translate /refs/<name> → host reference volume paths (forward slashes)
            while IFS='=' read -r key value; do
                if [[ "$key" == TERMINALHOST_REF_* ]]; then
                    local ref_name="${key#TERMINALHOST_REF_}"
                    local safe
                    safe=$(fwd_path "$value")
                    data="${data//\/refs\/${ref_name}/${safe}}"
                fi
            done < <(env)

            printf '%s' "$data"
        }

        if [ "$1" = "--hook" ] && [ -n "$2" ]; then
            # Read hook payload from stdin (Claude Code pipes JSON)
            PAYLOAD=$(cat)

            # Translate container paths to host paths
            PAYLOAD=$(translate_paths "$PAYLOAD")

            curl -s -X POST \
                -H "Content-Type: application/json" \
                -d "$PAYLOAD" \
                "$API_URL/api/hooks/$2" \
                > /dev/null 2>&1
            exit 0
        fi

        # For non-hook invocations, print a helpful message
        echo "host.exe proxy: running inside a container."
        echo "Path mappings:"
        echo "  ${CONTAINER_WS} -> ${HOST_WS:-<not set>}"
        echo "  /home/developer -> ${HOST_UP:-<not set>}"
        env | grep '^TERMINALHOST_REF_' | while IFS='=' read -r key value; do
            ref_name="${key#TERMINALHOST_REF_}"
            echo "  /refs/${ref_name} -> ${value}"
        done
        echo ""
        echo "API endpoint: $API_URL"
        echo "Use --hook <type> to forward hook events to TerminalHost."
        """;

    /// <summary>
    /// Clipboard shim script that bridges Linux clipboard commands to the host
    /// via TerminalHost's REST API. Installed as xclip, xsel, wl-paste, wl-copy,
    /// pbcopy, pbpaste so any tool using standard clipboard commands works transparently.
    /// </summary>
    private const string ClipboardShimScript = """
        #!/bin/bash
        # Clipboard bridge: proxies clipboard operations to TerminalHost's REST API
        # on the host machine. Symlinked as xclip/xsel/wl-paste/wl-copy/pbcopy/pbpaste.
        #
        # Supports:
        #   - Text read/write (xclip -o, echo | xclip, xsel --output, wl-paste, etc.)
        #   - Image read (xclip -t image/png -o, wl-paste --type image/png)
        #   - Detects invocation name to determine behavior

        API_URL="${TERMINALHOST_API:-http://host.docker.internal:19280}"
        PROG="$(basename "$0")"

        # Parse arguments to determine operation (read vs write) and type (text vs image)
        MODE=""
        TYPE="text"
        SELECTION="clipboard"
        RAW_TARGET=""

        case "$PROG" in
            pbpaste|wl-paste)
                MODE="read"
                ;;
            pbcopy|wl-copy)
                MODE="write"
                ;;
        esac

        # Parse arguments for xclip/xsel/wl-paste/wl-copy
        while [ $# -gt 0 ]; do
            case "$1" in
                -o|--output|-out)
                    MODE="read"
                    ;;
                -i|--input|-in)
                    MODE="write"
                    ;;
                -selection)
                    shift
                    SELECTION="${1:-clipboard}"
                    ;;
                --clipboard|-b)
                    SELECTION="clipboard"
                    ;;
                --primary|-p)
                    SELECTION="primary"
                    ;;
                -t|-target|--type|-type)
                    shift
                    RAW_TARGET="${1:-}"
                    case "$RAW_TARGET" in
                        image/png|image/*) TYPE="image" ;;
                        TARGETS) TYPE="targets" ;;
                        *) TYPE="text" ;;
                    esac
                    ;;
            esac
            shift
        done

        # xclip defaults: no -o means write; with -o means read
        if [ -z "$MODE" ]; then
            if [ "$PROG" = "xclip" ] || [ "$PROG" = "xsel" ]; then
                # If stdin is a pipe/redirect, assume write; otherwise read
                if [ ! -t 0 ]; then
                    MODE="write"
                else
                    MODE="read"
                fi
            else
                MODE="read"
            fi
        fi

        # Only support clipboard selection (not primary/secondary)
        if [ "$SELECTION" != "clipboard" ]; then
            exit 0
        fi

        if [ "$MODE" = "read" ]; then
            if [ "$TYPE" = "targets" ]; then
                # Return available clipboard targets — Claude Code checks this first
                # to see if image/png is available before trying to read it.
                STATUS=$(curl -sf -o /dev/null -w "%{http_code}" "$API_URL/api/clipboard/image" 2>/dev/null)
                if [ "$STATUS" = "200" ]; then
                    printf 'TARGETS\nimage/png\ntext/plain;charset=utf-8\ntext/plain\nUTF8_STRING\nSTRING\n'
                else
                    printf 'TARGETS\ntext/plain;charset=utf-8\ntext/plain\nUTF8_STRING\nSTRING\n'
                fi
            elif [ "$TYPE" = "image" ]; then
                # Read image as raw PNG from host clipboard
                curl -sf "$API_URL/api/clipboard/image" 2>/dev/null
            else
                # Read text from host clipboard
                RESULT=$(curl -sf "$API_URL/api/clipboard/text" 2>/dev/null)
                if [ -n "$RESULT" ]; then
                    printf '%s' "$RESULT" | jq -rj '.text // empty' 2>/dev/null
                fi
            fi
        else
            if [ "$TYPE" = "image" ]; then
                # Write image (raw PNG from stdin) to host clipboard
                curl -sf -X POST \
                    -H "Content-Type: image/png" \
                    --data-binary @- \
                    "$API_URL/api/clipboard/image" \
                    > /dev/null 2>&1
            else
                # Read text from stdin, send to host clipboard
                INPUT=$(cat)
                if [ -n "$INPUT" ]; then
                    ESCAPED=$(printf '%s' "$INPUT" | jq -Rs '.')
                    curl -sf -X POST \
                        -H "Content-Type: application/json" \
                        -d "{\"text\":$ESCAPED}" \
                        "$API_URL/api/clipboard/text" \
                        > /dev/null 2>&1
                fi
            fi
        fi
        """;
}
