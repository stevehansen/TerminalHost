# Containerized Workspaces (Docker)

TerminalHost can run AI coding agents inside Docker containers, providing filesystem isolation while preserving full access to project files, shared libraries, and agent configuration (memory, settings, sessions). Inspired by [code-container](https://github.com/kevinMEH/code-container).

## Problem

AI coding agents (Claude Code, Gemini CLI, etc.) run with the same permissions as the user. A misguided `rm -rf`, rogue `apt install`, or accidental system modification affects the host directly. Developers want:

- **Filesystem protection**: Agent can't damage anything outside mounted directories
- **Persistent agent state**: Memory, settings, sessions, and conversation history survive container restarts and are shared with the host (since TerminalHost reads these files for Tasks, Timeline, etc.)
- **Reference library access**: Readonly mounts for shared source code the agent may need to inspect (e.g., `P:\Vidyano.Service`, `P:\CronosCore`)
- **Zero-friction workflow**: Same `host .` experience — containerization is transparent to the user

## Design Principles

1. **Opt-in per workspace** — Containerization is a per-directory setting, not global. Some projects may not need it.
2. **Container-per-workspace** — Each project directory gets its own persistent Docker container (identified by path hash). Containers survive across TerminalHost restarts.
3. **Host filesystem is the source of truth** — Project files are bind-mounted read-write. The host sees all changes immediately and vice versa.
4. **Agent config is shared** — Claude Code's `~/.claude/` (memory, sessions, settings) is mounted read-write so TerminalHost's file watchers (Tasks, Timeline, Commands) continue to work.
5. **Reference volumes are readonly** — Shared libraries or external source code can be mounted readonly for agent inspection without modification risk.
6. **Windows-first** — Must work with Docker Desktop on Windows. Linux path translation handled automatically.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                     TerminalHost                          │
│                                                           │
│  ┌─────────────────┐    ┌──────────────────────────────┐ │
│  │ ContainerService │───▶│  Docker Desktop (Windows)    │ │
│  │                  │    │                              │ │
│  │ - build image    │    │  ┌────────────────────────┐  │ │
│  │ - create/start   │    │  │  terminalhost-ws-xxx   │  │ │
│  │ - exec sessions  │    │  │                        │  │ │
│  │ - stop/remove    │    │  │  /workspace  (rw)      │  │ │
│  │ - health check   │    │  │  /refs/Vidyano (ro)    │  │ │
│  └─────────────────┘    │  │  /refs/CronosCore (ro) │  │ │
│          │               │  │  /root/.claude (rw)    │  │ │
│          ▼               │  │  /root/.gitconfig (ro) │  │ │
│  ┌─────────────────┐    │  │                        │  │ │
│  │ TerminalControl  │    │  │  Claude Code / AI CLI  │  │ │
│  │ Factory          │    │  └────────────────────────┘  │ │
│  │                  │    └──────────────────────────────┘ │
│  │ Wraps command as │                                     │
│  │ docker exec -it  │                                     │
│  └─────────────────┘                                     │
└──────────────────────────────────────────────────────────┘
```

### Container Naming

Each container is uniquely identified by the workspace path:

```
terminalhost-ws-{project-name}-{sha1(normalized-path)[0:8]}
```

Example: `P:\HC` → `terminalhost-ws-hc-a3f2b1c9`

### Docker Image

A single shared image `terminalhost-workspace:latest` is built from a user-customizable Dockerfile stored at `%APPDATA%\TerminalHost\container\Dockerfile`.

Default Dockerfile contents:

```dockerfile
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

# Host proxy (forwards hook calls to TerminalHost on the host machine)
COPY host-proxy.sh /usr/local/bin/host.exe
RUN chmod +x /usr/local/bin/host.exe

# Shell prompt
RUN echo 'PS1="[container] \w\$ "' >> /root/.bashrc

WORKDIR /workspace
CMD ["/bin/bash"]
```

Users can customize this Dockerfile (add languages, tools, SDKs) and rebuild via command palette or settings.

### Host Proxy (`host.exe`)

Claude Code hooks and plugins reference `host.exe` for communication with TerminalHost (session tracking, file change notifications, etc.). Inside the container, `host.exe` is a lightweight bash script that forwards these calls to the TerminalHost REST API on the host machine via `host.docker.internal`.

```bash
#!/bin/bash
# Forwards hook events from container to TerminalHost REST API
API_URL="${TERMINALHOST_API:-http://host.docker.internal:19280}"

if [ "$1" = "--hook" ] && [ -n "$2" ]; then
    PAYLOAD=$(cat)
    curl -s -X POST -H "Content-Type: application/json" \
        -d "$PAYLOAD" "$API_URL/api/hooks/$2" > /dev/null 2>&1
    exit 0
fi
```

The `TERMINALHOST_API` environment variable is set automatically when the container is created, pointing to the host's API server. This requires the REST API to be enabled in settings (`Ctrl+,` → API & Webhooks → Enable API Server).

**How it works:**
1. Claude Code fires a hook (e.g., `host.exe --hook session-start`)
2. The proxy script reads the JSON payload from stdin
3. **Path translation**: The proxy rewrites all container paths to host paths before forwarding (see below)
4. It POSTs the translated payload to `http://host.docker.internal:{port}/api/hooks/{type}`
5. TerminalHost receives it and processes it (Timeline tracking, file change notifications, etc.)

### Path Translation

Inside the container, all paths use Linux conventions (`/workspace/src/file.cs`, `/root/.claude/...`). TerminalHost on the host expects Windows paths (`P:\HC\src\file.cs`, `C:\Users\steve\.claude\...`). The host proxy translates paths automatically using environment variables set at container creation:

| Container Path | Env Var | Host Path (example) |
|---------------|---------|---------------------|
| `/workspace` | `TERMINALHOST_HOST_WORKSPACE` | `P:\HC` |
| `/root` | `TERMINALHOST_HOST_USERPROFILE` | `C:\Users\steve` |
| `/refs/{name}` | `TERMINALHOST_REF_{name}` | `P:\Vidyano.Service` |

The proxy performs string replacement on the JSON payload before forwarding, so by the time TerminalHost receives hook data, all paths are in the host's format. This ensures:

- **Session tracking**: `cwd` in hook payloads resolves to the correct project directory
- **File change notifications**: File paths from `PostToolUse` hooks match host filesystem paths
- **Transcript paths**: Session JSONL paths resolve correctly for Timeline mode
- **Reference volumes**: Paths inside `/refs/` map back to the original host directories

## AI Agent Configuration Sharing

### Auto-Approve in Container (--dangerously-skip-permissions)

Since the container IS the sandbox, Claude Code's built-in permission system is redundant. When `autoApproveInContainer` is enabled (default: **true**), Claude Code launches with `--dangerously-skip-permissions`, allowing it to:

- Edit files without confirmation prompts
- Run shell commands without approval
- Use all tools freely

This is the primary reason to use containerization — the agent can work at full speed without interruption, while the container prevents any damage to the host filesystem.

### Shared Settings & Config

The host's `~/.claude/` directory is mounted read-write, which means the container's Claude Code inherits:

| Setting | Source File | Notes |
|---------|------------|-------|
| **Permissions** (allow/deny lists) | `~/.claude/settings.json` | Overridden by `--dangerously-skip-permissions` when auto-approve is on |
| **Hooks** | `~/.claude/settings.json` | Hooks referencing host binaries (e.g., `host.exe`) will fail silently inside the container. This is expected — hooks are host-side tooling. |
| **Statusline** | `~/.claude/settings.json` | Works if the statusline command is available in the container (Bun and Node.js are pre-installed) |
| **Plugins** | `~/.claude/plugins/` | Plugin code is shared via the `~/.claude` mount |
| **Memory** | `~/.claude/projects/*/memory/` | Read-write — agent memory persists across container restarts and is visible on host |
| **Sessions** | `~/.claude/projects/*/sessions-index.json` | Read-write — TerminalHost's Timeline and Tasks panels see container sessions |
| **Tasks** | `~/.claude/tasks/` | Read-write — TerminalHost's Claude Tasks panel works normally |
| **Commands** | `~/.claude/commands/*.md` | Shared slash commands available in container |

### Session Directory Overlay Mount

Claude Code stores sessions, memory, and tasks under `~/.claude/projects/{encoded-path}/` where the path is encoded by replacing separators with dashes (e.g., `P:\HC` → `P--HC`). Inside the container, the working directory is `/workspace`, which encodes to `-workspace` — a completely different folder.

Without intervention, container sessions would land in `~/.claude/projects/-workspace/` while TerminalHost looks for them in `~/.claude/projects/P--HC/`. Sessions, memory, and tasks would all be invisible.

**Solution: Overlapping Docker mounts.** When creating the container, an additional specific mount maps the host's project directory to the container's expected location:

```
-v "C:\Users\steve\.claude\projects\P--HC:/root/.claude/projects/-workspace"
```

Docker's mount precedence makes this specific path override the broader `~/.claude` mount. The result:

| What | Container writes to | Actually stored at |
|------|--------------------|--------------------|
| Sessions | `/root/.claude/projects/-workspace/sessions-index.json` | `~/.claude/projects/P--HC/sessions-index.json` |
| Memory | `/root/.claude/projects/-workspace/memory/` | `~/.claude/projects/P--HC/memory/` |
| JSONL transcripts | `/root/.claude/projects/-workspace/*.jsonl` | `~/.claude/projects/P--HC/*.jsonl` |

This means:
- Container sessions appear seamlessly in TerminalHost's Timeline and Session panels
- Agent memory written inside the container is visible when running locally (and vice versa)
- No special handling needed in `ClaudeSessionIndexService` — paths already match

### Project-Level `.claude/` Directory

The project's own `.claude/` directory (containing `CLAUDE.md`, project memory, local commands) is automatically available since it lives inside the project directory, which is mounted at `/workspace`.

## Mount System

### Automatic Mounts (always applied)

| Host Path | Container Path | Mode | Purpose |
|-----------|---------------|------|---------|
| `{project-dir}` | `/workspace` | **rw** | The project being worked on |
| `%USERPROFILE%\.claude` | `/root/.claude` | **rw** | Claude settings, memory, sessions, tasks, hooks, plugins |
| `%USERPROFILE%\.claude.json` | `/root/.claude.json` | **rw** | Claude global metadata (startup count, tips, etc.) |
| `%USERPROFILE%\.gitconfig` | `/root/.gitconfig` | **ro** | Git identity & settings |
| `%USERPROFILE%\.ssh` | `/root/.ssh` | **ro** | SSH keys for git operations (optional, off by default) |

### Reference Volumes (user-configured, readonly)

Shared source code libraries that the agent may need to inspect but should never modify:

```json
{
  "containerSettings": {
    "referenceVolumes": [
      { "hostPath": "P:\\Vidyano.Service", "name": "Vidyano.Service" },
      { "hostPath": "P:\\Vidyano", "name": "Vidyano" },
      { "hostPath": "P:\\CronosCore", "name": "CronosCore" }
    ]
  }
}
```

These mount as `/refs/{name}` (readonly) inside the container. The agent can browse, grep, and read — but not modify.

A `REFS.md` file is automatically generated at `/workspace/REFS.md` (gitignored) when the container starts, listing all reference volumes and their paths so the AI agent knows where to find them:

```markdown
# Reference Libraries (readonly)

The following shared libraries are mounted for inspection:

- `/refs/Vidyano.Service` — P:\Vidyano.Service
- `/refs/Vidyano` — P:\Vidyano
- `/refs/CronosCore` — P:\CronosCore
```

### Extra Read-Write Mounts (advanced)

For cases where additional writable directories are needed (e.g., shared NuGet cache, npm cache):

```json
{
  "containerSettings": {
    "extraMounts": [
      { "hostPath": "P:\\NuGetCache", "containerPath": "/root/.nuget", "readonly": false }
    ]
  }
}
```

## Configuration Schema

### Global Container Settings (`config.json`)

```json
{
  "containerSettings": {
    "enabled": false,
    "dockerPath": "docker",
    "imageName": "terminalhost-workspace",
    "imageTag": "latest",
    "mountSsh": false,
    "autoApproveInContainer": true,
    "referenceVolumes": [
      { "hostPath": "P:\\Vidyano.Service", "name": "Vidyano.Service" },
      { "hostPath": "P:\\Vidyano", "name": "Vidyano" },
      { "hostPath": "P:\\CronosCore", "name": "CronosCore" }
    ],
    "extraMounts": [],
    "extraDockerArgs": [],
    "networkMode": "bridge",
    "envVars": {
      "TERM": "xterm-256color"
    }
  }
}
```

### Per-Directory Override (`directorySettings`)

```json
{
  "directorySettings": {
    "p:\\hc": {
      "containerEnabled": true,
      "containerReferenceVolumes": [
        { "hostPath": "P:\\Vidyano.Service", "name": "Vidyano.Service" }
      ]
    },
    "p:\\fleet": {
      "containerEnabled": true
    },
    "p:\\personal-scripts": {
      "containerEnabled": false
    }
  }
}
```

- `containerEnabled`: Overrides the global `enabled` flag for this directory. If `null`/absent, uses global setting.
- `containerReferenceVolumes`: If set, overrides (not merges with) the global reference volumes for this directory.

## Terminal Launch Flow

### Without Container (current behavior)

```
TerminalControlFactory → cmd.exe /K cd /d "P:\HC" && claude.exe
```

### With Container

```
1. ContainerService.EnsureContainerRunning("P:\HC")
   ├── Check if container "terminalhost-ws-hc-a3f2b1c9" exists
   ├── If not: docker run -d --name ... -v mounts... image sleep infinity
   ├── If stopped: docker start ...
   └── Health check: docker exec ... echo ok

2. TerminalControlFactory → docker exec -it terminalhost-ws-hc-a3f2b1c9 /bin/bash
   (shell terminal)

3. TerminalControlFactory → docker exec -it -w /workspace terminalhost-ws-hc-a3f2b1c9 claude
   (custom terminal — AI agent)
```

Both the custom terminal and shell terminal `docker exec` into the same container. The run terminal (F5) also exec's into the same container if active.

### Path Translation (Windows → Linux)

Docker Desktop on Windows requires Linux-style paths for bind mounts:

| Windows Path | Docker Mount Path |
|-------------|-------------------|
| `P:\HC` | `/p/HC` or use Docker Desktop's path mapping |
| `C:\Users\steve\.claude` | `/c/Users/steve/.claude` |

The `ContainerService` handles this translation. Docker Desktop's WSL2 backend automatically translates `/c/...` style paths.

## Container Lifecycle

### States

```
┌──────────┐     docker run      ┌─────────┐
│ NotFound │ ──────────────────▶ │ Running │
└──────────┘                     └────┬────┘
                                      │
                              docker stop │ docker start
                                      ▼       ▲
                                 ┌────────┐   │
                                 │ Stopped │───┘
                                 └────────┘
                                      │
                              docker rm │
                                      ▼
                                 ┌─────────┐
                                 │ Removed │
                                 └─────────┘
```

### Session Counting

Before stopping a container, count active `docker exec` sessions:

```
docker top {container} | grep "docker exec" | wc -l
```

If other sessions are active (e.g., shell terminal still open), the container stays running. Only stop when the last terminal for that workspace closes.

### Tab Close Behavior

When a tab is closed:
1. All `docker exec` sessions for that container are terminated (terminals close naturally)
2. If no other TerminalHost tabs reference the same container, stop the container
3. Container is **not removed** — it persists for quick reattach on next `host .`

### Explicit Cleanup

Users can remove containers via:
- Command palette: "Container: Stop", "Container: Remove", "Container: Remove All Stopped"
- Settings UI: Container management section with list of containers and their state

## Service Interface

```csharp
public interface IContainerService
{
    /// Check if Docker is available and running
    Task<bool> IsDockerAvailableAsync();

    /// Check if the workspace image exists
    Task<bool> IsImageBuiltAsync();

    /// Build or rebuild the workspace image from Dockerfile
    Task<bool> BuildImageAsync(IProgress<string>? progress = null);

    /// Get the current state of a workspace's container
    Task<ContainerState> GetContainerStateAsync(string workspaceDir);

    /// Ensure a container is running for the given workspace (create/start as needed)
    Task<string> EnsureContainerRunningAsync(string workspaceDir);

    /// Get the docker exec command prefix for launching a session in the container
    string GetExecCommand(string containerName, string? workingDir = null);

    /// Stop a workspace's container
    Task StopContainerAsync(string workspaceDir);

    /// Remove a workspace's container
    Task RemoveContainerAsync(string workspaceDir);

    /// List all TerminalHost containers with their state
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync();

    /// Remove all stopped TerminalHost containers
    Task<int> CleanStoppedContainersAsync();

    /// Get the container name for a workspace directory
    string GetContainerName(string workspaceDir);
}

public enum ContainerState { NotFound, Running, Stopped }

public record ContainerInfo(
    string Name,
    string WorkspaceDir,
    ContainerState State,
    DateTime? CreatedAt,
    string? ImageId
);
```

## Settings UI

### Settings View (Ctrl+,) — "Container" Section

```
╔══════════════════════════════════════════════════════╗
║  CONTAINER                                           ║
║                                                      ║
║  Run AI agents inside Docker containers for           ║
║  filesystem isolation. Requires Docker Desktop.       ║
║                                                      ║
║  ☐ Enable containerized workspaces                   ║
║                                                      ║
║  Docker Path                                         ║
║  ┌──────────────────────────────────┐                ║
║  │ docker                           │                ║
║  └──────────────────────────────────┘                ║
║                                                      ║
║  ☐ Mount SSH keys (readonly)                         ║
║    Share ~/.ssh for git operations over SSH           ║
║                                                      ║
║  Network Mode                                        ║
║  ┌──────────────────────────────────┐                ║
║  │ bridge                     ▾     │                ║
║  └──────────────────────────────────┘                ║
║  Options: bridge, host, none                         ║
║                                                      ║
║  REFERENCE VOLUMES (READONLY)                        ║
║  Shared source code mounted at /refs/{name}          ║
║                                                      ║
║  ┌──────────────────────────────────────────────┐    ║
║  │ P:\Vidyano.Service  →  /refs/Vidyano.Service │ ✕  ║
║  │ P:\Vidyano          →  /refs/Vidyano         │ ✕  ║
║  │ P:\CronosCore       →  /refs/CronosCore      │ ✕  ║
║  └──────────────────────────────────────────────┘    ║
║  [+ Add Reference Volume]                            ║
║                                                      ║
║  DOCKER IMAGE                                        ║
║  Image: terminalhost-workspace:latest                ║
║  Status: Built (2.1 GB)  |  [Rebuild]  [Edit]       ║
║                                                      ║
║  ACTIVE CONTAINERS                                   ║
║  ┌──────────────────────────────────────────────┐    ║
║  │ ● hc-a3f2b1c9         P:\HC        Running  │ ■  ║
║  │ ○ fleet-b4c3d2e1      P:\Fleet     Stopped  │ ✕  ║
║  └──────────────────────────────────────────────┘    ║
║  [Clean Stopped]                                     ║
║                                                      ║
╚══════════════════════════════════════════════════════╝
```

### Per-Directory Toggle

In the terminal pair toolbar (next to the AI assistant dropdown), a container toggle icon:

```
[🐳 ▾]  — Click to toggle containerization for this workspace
           Dropdown: Enable / Disable / Use Global Setting
```

When containerized, the tab shows a subtle container indicator (e.g., small 🐳 badge or colored dot).

## Command Palette Commands

| Command | Action |
|---------|--------|
| Container: Toggle for Current Workspace | Enable/disable container for active tab |
| Container: Rebuild Image | Rebuild Docker image from Dockerfile |
| Container: Edit Dockerfile | Open Dockerfile in file viewer/editor |
| Container: Stop Current | Stop the active workspace's container |
| Container: Remove Current | Remove the active workspace's container |
| Container: List All | Show all containers with state |
| Container: Clean Stopped | Remove all stopped containers |
| Container: Open Shell | Open a new shell session in the current container |
| Container: Check Docker Status | Verify Docker Desktop is running |

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+D` | *(existing: Git Pull)* |
| — | Container toggle via command palette only (no dedicated shortcut) |

## File Structure

```
%APPDATA%\TerminalHost\
├── config.json                    # Main config (includes containerSettings)
└── container\
    ├── Dockerfile                 # User-customizable Dockerfile
    └── .dockerignore              # Build context ignore rules
```

### Code Structure

```
src/TerminalHost.Core/
├── Domain/
│   ├── ContainerSettings.cs       # Settings model
│   ├── ContainerInfo.cs           # Container state model
│   └── ReferenceVolume.cs         # Reference volume model
├── Interfaces/
│   └── IContainerService.cs       # Service interface
└── Services/
    └── ContainerService.cs        # Docker CLI wrapper (platform-agnostic)

src/TerminalHost/TerminalHost/
├── Services/
│   └── WindowsContainerService.cs # Windows path translation, Docker Desktop detection
└── Views/
    └── (Settings UI additions)
```

## Implementation Phases

### Phase 1: Core Infrastructure
- `ContainerSettings` domain model added to `AppConfiguration`
- `IContainerService` interface and `ContainerService` implementation
- Docker availability detection (check `docker info` succeeds)
- Image build from bundled Dockerfile
- Container create/start/stop/remove lifecycle
- Path translation (Windows → Linux mount paths)
- `TerminalControlFactory` integration: wrap commands as `docker exec -it` when container enabled

### Phase 2: Settings & UI
- Container section in Settings view
- Reference volume management (add/remove/reorder)
- Per-directory container toggle in toolbar
- Container status indicator on tabs
- Image rebuild button with progress toast
- Dockerfile editor (opens in built-in file viewer)
- Active container list in settings with stop/remove actions

### Phase 3: Command Palette & Polish
- All command palette commands registered
- `REFS.md` generation on container start
- Graceful handling of Docker Desktop not running (toast + guidance)
- Container cleanup on app exit (configurable: stop all / leave running)
- First-run experience: detect Docker, offer to build image, configure SSH mount

### Phase 4: Advanced Features
- Container health monitoring (periodic docker inspect)
- Resource limits (memory, CPU) via settings
- Multiple Dockerfile profiles (e.g., Node-focused, .NET-focused, Python-focused)
- Network isolation mode (`--network none`) for maximum security
- Container shell history persistence
- Automatic image rebuild when Dockerfile changes detected

## Edge Cases & Considerations

### Docker Desktop Not Running
- On tab open: show toast "Docker Desktop is not running. Start Docker Desktop or disable containerization for this workspace."
- Offer fallback: "Run without container" button in the toast.

### Container Already Running (from previous session)
- `EnsureContainerRunningAsync` checks state first — reuses running containers.
- If the container was created with different mounts (config changed), warn the user and offer to recreate.

### File Watchers Across Container Boundary
- TerminalHost's file watchers (ClaudeSessionIndexService, ClaudeTaskFileService) watch host paths, not container paths.
- Because `~/.claude` is bind-mounted, changes made by the agent inside the container are immediately visible on the host filesystem.
- FileSystemWatcher works across bind mounts on Windows — no changes needed.

### Windows Path Gotchas
- Docker Desktop on Windows uses WSL2 backend. Drive paths like `P:\HC` need translation.
- For drives beyond C:, Docker Desktop must have the drive shared in settings (Settings → Resources → File Sharing). The service should check this and warn if a drive isn't shared.
- Long paths (>260 chars) may need special handling.

### Git Inside Container
- `.gitconfig` is mounted readonly — the agent can use git normally (commit, push, etc.)
- `.ssh` mount (optional, readonly) enables SSH-based git remotes
- Git credential helpers that rely on Windows Credential Manager won't work inside the container. Recommend using SSH keys or configuring a credential cache inside the container.

### Performance
- Bind mounts on Docker Desktop (Windows) can be slow for large node_modules or .git directories.
- Consider recommending VirtioFS (Docker Desktop setting) for better performance.
- Reference volumes are readonly, which helps with caching.

### Multiple AI Assistants
- The container has Claude Code pre-installed. Other AI CLIs (Gemini, Codex) would also need to be in the image.
- The Dockerfile should include all enabled AI assistants, or users can customize.
- `AiAssistantService.GetAssistantForDirectory()` provides the command — `ContainerService` just needs to know the binary name inside the container (not the host path).

## Security Model

### What's Protected
- Host filesystem outside mounted directories
- System packages and configuration
- Other running processes and services

### What's NOT Protected
- Network access (unless `networkMode: "none"`)
- Mounted directories (project is rw, refs are ro)
- Secrets in mounted config files (API keys in `.claude.json`, SSH keys)
- Container runs as root (container root, not host root)

### Threat Model
The primary threat is **accidental damage** from AI agents (wrong `rm`, bad `apt install`, filesystem traversal). This is NOT designed to protect against **malicious** agents or prompt injection attacks that exfiltrate data over the network. For that, use `networkMode: "none"` (but this breaks most AI CLI tools that need API access).

## Comparison with code-container

| Feature | code-container | TerminalHost Containers |
|---------|---------------|------------------------|
| Platform | Linux/macOS/WSL | Windows (Docker Desktop) native |
| Integration | Standalone CLI | Built into TerminalHost UI |
| Per-project toggle | Always on | Opt-in per workspace |
| Reference volumes | Via MOUNTS.txt | First-class UI with named volumes |
| Multi-session | `ps ax` counting | Docker API session tracking |
| Image management | Single Dockerfile | Customizable with rebuild UI |
| Agent config sharing | Copies to central store | Direct bind-mount from host |
| Container lifecycle | CLI commands | UI + command palette + auto-management |
| File watchers | N/A | TerminalHost watches host paths through bind mounts |
