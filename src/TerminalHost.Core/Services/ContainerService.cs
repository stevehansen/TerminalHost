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

    public async Task<string> EnsureContainerRunningAsync(string workspaceDir)
    {
        var containerName = GetContainerName(workspaceDir);
        var state = await GetContainerStateAsync(workspaceDir);

        switch (state)
        {
            case ContainerState.Running:
                return containerName;

            case ContainerState.Stopped:
                await StartContainerAsync(containerName);
                return containerName;

            case ContainerState.NotFound:
                // Ensure image exists
                if (!await IsImageBuiltAsync())
                {
                    var built = await BuildImageAsync();
                    if (!built)
                        throw new InvalidOperationException("Failed to build Docker image. Check Docker Desktop is running.");
                }

                await CreateContainerAsync(workspaceDir, containerName);
                return containerName;

            default:
                throw new InvalidOperationException($"Unexpected container state: {state}");
        }
    }

    public bool IsAutoApproveEnabled
    {
        get
        {
            var config = _configService.Load();
            return config.Settings.Container.AutoApproveInContainer;
        }
    }

    public string BuildExecCommand(string containerName, string? command = null, string? extraArgs = null)
    {
        var dockerPath = GetDockerPath();
        var cmd = command ?? "/bin/bash";
        if (!string.IsNullOrEmpty(extraArgs))
            cmd = $"{cmd} {extraArgs}";
        return $"\"{dockerPath}\" exec -it -w /workspace {containerName} {cmd}";
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

            results.Add(new ContainerInfo
            {
                Name = name,
                State = state
            });
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
            _fileSystem.WriteAllText(dockerfilePath, DefaultDockerfile);
        }

        // Always write the host proxy script (it's small and may be updated between versions)
        var proxyPath = Path.Combine(containerDir, "host-proxy.sh");
        _fileSystem.WriteAllText(proxyPath, HostProxyScript);
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

        // Working directory
        args.Append(" -w /workspace");

        // Project directory (read-write)
        args.Append($" -v \"{workspaceDir}:/workspace\"");

        // Claude Code config directories (read-write for sharing with host)
        var claudeDir = Path.Combine(userProfile, ".claude");
        var claudeJson = Path.Combine(userProfile, ".claude.json");

        if (_fileSystem.DirectoryExists(claudeDir))
            args.Append($" -v \"{claudeDir}:/root/.claude\"");
        if (_fileSystem.FileExists(claudeJson))
            args.Append($" -v \"{claudeJson}:/root/.claude.json\"");

        // Overlay mount: map the host's project session directory to where the container
        // will look for it. Claude Code stores sessions under ~/.claude/projects/{encoded-path}/
        // where {encoded-path} replaces path separators with dashes.
        // Host: P:\HC → P--HC, Container: /workspace → -workspace
        // By mounting P--HC at -workspace, sessions/memory/tasks all land in the right place.
        var hostProjectKey = EncodeClaudeProjectPath(workspaceDir);
        var containerProjectKey = EncodeClaudeProjectPath("/workspace");
        var hostProjectDir = Path.Combine(claudeDir, "projects", hostProjectKey);

        // Create the host project directory if it doesn't exist yet
        // (first-time container for a project that hasn't been opened with Claude before)
        if (!_fileSystem.DirectoryExists(hostProjectDir))
            Directory.CreateDirectory(hostProjectDir);

        // Specific mount overrides the broader ~/.claude mount (Docker mount precedence)
        args.Append($" -v \"{hostProjectDir}:/root/.claude/projects/{containerProjectKey}\"");

        // Git config (readonly)
        var gitConfig = Path.Combine(userProfile, ".gitconfig");
        if (_fileSystem.FileExists(gitConfig))
            args.Append($" -v \"{gitConfig}:/root/.gitconfig:ro\"");

        // SSH keys (optional, readonly)
        if (cs.MountSsh)
        {
            var sshDir = Path.Combine(userProfile, ".ssh");
            if (_fileSystem.DirectoryExists(sshDir))
                args.Append($" -v \"{sshDir}:/root/.ssh:ro\"");
        }

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

        // Network mode
        if (!string.IsNullOrEmpty(cs.NetworkMode) && cs.NetworkMode != "bridge")
            args.Append($" --network {cs.NetworkMode}");

        // Extra docker args
        foreach (var arg in cs.ExtraDockerArgs)
            args.Append($" {arg}");

        // Image and command (sleep infinity to keep container alive)
        args.Append($" {cs.ImageName}:{cs.ImageTag} sleep infinity");

        var (exitCode, _, error) = await _processService.RunAsync(
            dockerPath, args.ToString(),
            timeout: TimeSpan.FromSeconds(30));

        if (exitCode != 0)
            throw new InvalidOperationException($"Failed to create container: {error}");
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

    private string GetDockerPath()
    {
        var config = _configService.Load();
        return config.Settings.Container.DockerPath;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

    /// <summary>
    /// Encodes a path the same way Claude Code does for ~/.claude/projects/ directory names.
    /// Path separators (\, /, :) are replaced with dashes.
    /// Examples: "P:\HC" → "P--HC", "/workspace" → "-workspace"
    /// </summary>
    internal static string EncodeClaudeProjectPath(string path)
    {
        // Claude Code replaces all path separator characters with dashes
        return path
            .Replace(':', '-')
            .Replace('\\', '-')
            .Replace('/', '-');
    }

    private const string DefaultDockerfile = """
        FROM ubuntu:24.04

        ENV DEBIAN_FRONTEND=noninteractive
        ENV TZ=UTC

        # System essentials
        RUN apt-get update && apt-get install -y \
            build-essential git curl wget unzip ca-certificates \
            libssl-dev zlib1g-dev libffi-dev vim tree jq ripgrep \
            && rm -rf /var/lib/apt/lists/*

        # Node.js 22 via NVM
        ENV NVM_DIR=/root/.nvm
        RUN curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.7/install.sh | bash \
            && . "$NVM_DIR/nvm.sh" && nvm install 22 && nvm use 22

        # Bun (for statusline and fast npm scripts)
        RUN curl -fsSL https://bun.sh/install | bash
        ENV BUN_INSTALL=/root/.bun
        ENV PATH="$BUN_INSTALL/bin:$PATH"

        # Python 3
        RUN apt-get update && apt-get install -y python3 python3-pip python3-venv \
            && rm -rf /var/lib/apt/lists/*

        # .NET SDKs (8, 9, 10)
        RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
            && chmod +x /tmp/dotnet-install.sh \
            && /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
            && /tmp/dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet \
            && /tmp/dotnet-install.sh --channel 10.0 --quality preview --install-dir /usr/share/dotnet \
            && ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet \
            && rm /tmp/dotnet-install.sh
        ENV DOTNET_ROOT=/usr/share/dotnet
        ENV PATH="$DOTNET_ROOT:$PATH"

        # Claude Code
        RUN curl -fsSL https://claude.ai/install.sh | bash

        # Host proxy: forwards hook calls from container to the TerminalHost REST API on the host.
        # The TERMINALHOST_API env var is set at container creation time by TerminalHost.
        COPY host-proxy.sh /usr/local/bin/host.exe
        RUN chmod +x /usr/local/bin/host.exe

        # Shell prompt
        RUN echo 'PS1="[container] \\w\\$ "' >> /root/.bashrc

        # Source NVM in bash
        RUN echo '. "$NVM_DIR/nvm.sh"' >> /root/.bashrc

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
        #   TERMINALHOST_HOST_WORKSPACE   — host path for /workspace (e.g., P:\HC)
        #   TERMINALHOST_HOST_USERPROFILE — host path for /root     (e.g., C:\Users\steve)
        #   TERMINALHOST_REF_<name>       — host path for /refs/<name>

        API_URL="${TERMINALHOST_API:-http://host.docker.internal:19280}"
        HOST_WS="${TERMINALHOST_HOST_WORKSPACE}"
        HOST_UP="${TERMINALHOST_HOST_USERPROFILE}"

        translate_paths() {
            local data="$1"

            # Translate /workspace → host workspace path (e.g., P:\HC)
            # JSON-escape backslashes: P:\HC → P:\\HC
            if [ -n "$HOST_WS" ]; then
                local escaped
                escaped=$(printf '%s' "$HOST_WS" | sed 's/\\/\\\\/g')
                data=$(printf '%s' "$data" | sed "s|/workspace|${escaped}|g")
            fi

            # Translate /root → host user profile (e.g., C:\Users\steve)
            # This covers /root/.claude/... paths in hook data
            if [ -n "$HOST_UP" ]; then
                local escaped
                escaped=$(printf '%s' "$HOST_UP" | sed 's/\\/\\\\/g')
                data=$(printf '%s' "$data" | sed "s|/root|${escaped}|g")
            fi

            # Translate /refs/<name> → host reference volume paths
            # Reads TERMINALHOST_REF_* env vars
            while IFS='=' read -r key value; do
                if [[ "$key" == TERMINALHOST_REF_* ]]; then
                    local ref_name="${key#TERMINALHOST_REF_}"
                    # Restore dots/hyphens from underscores (best-effort)
                    local escaped
                    escaped=$(printf '%s' "$value" | sed 's/\\/\\\\/g')
                    data=$(printf '%s' "$data" | sed "s|/refs/${ref_name}|${escaped}|g")
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
        echo "  /workspace -> ${HOST_WS:-<not set>}"
        echo "  /root      -> ${HOST_UP:-<not set>}"
        env | grep '^TERMINALHOST_REF_' | while IFS='=' read -r key value; do
            ref_name="${key#TERMINALHOST_REF_}"
            echo "  /refs/${ref_name} -> ${value}"
        done
        echo ""
        echo "API endpoint: $API_URL"
        echo "Use --hook <type> to forward hook events to TerminalHost."
        """;
}
