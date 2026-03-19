# Containerized Workspaces

Run AI coding agents inside Docker containers for filesystem isolation, while keeping full access to your project files, shared libraries, and agent configuration.

## Why

AI agents like Claude Code run with your user permissions. A wrong `rm -rf`, accidental system modification, or rogue `apt install` affects your host directly. Containerized workspaces solve this by running the agent in an isolated Docker container where it can only touch what you explicitly mount.

**Key benefit:** With the container as the sandbox, Claude Code runs with `--dangerously-skip-permissions` by default — no more approval prompts. The agent works at full speed while the container prevents host damage.

## How It Works

```
┌─────────────────────────────────┐
│        TerminalHost (host)      │
│                                 │
│  Tab: P:\HC                     │
│  ┌────────────┬────────────┐    │
│  │ Claude Code│  Shell     │    │
│  │ docker exec│ docker exec│    │
│  │ -it claude │ -it bash   │    │
│  └─────┬──────┴─────┬──────┘    │
│        │            │           │
│  ┌─────▼────────────▼──────┐    │
│  │  Docker Container       │    │
│  │  terminalhost-ws-hc-... │    │
│  │                         │    │
│  │  /workspace     (rw) ←──── P:\HC
│  │  /refs/Vidyano  (ro) ←──── P:\Vidyano
│  │  /root/.claude  (rw) ←──── ~/.claude
│  │  /root/.gitconfig (ro)     │
│  └─────────────────────────┘    │
└─────────────────────────────────┘
```

Each workspace gets its own persistent Docker container. Both the custom terminal (Claude Code) and shell terminal `docker exec` into the same container. The container runs `sleep infinity` in the background — terminals attach/detach without killing it.

## Quick Start

1. **Enable Docker Desktop** on Windows
2. **Open Settings** (`Ctrl+,`) → Container section
3. **Enable containerized workspaces** (global toggle or per-project)
4. **Add reference volumes** (optional) — readonly mounts for shared libraries
5. **Restart the tab** — the container is created automatically on the next tab open

Or use the **command palette** (`Ctrl+Shift+P`):
- `Container: Toggle for Current Workspace`
- `Container: Rebuild Image`
- `Container: Check Docker Status`

## What Gets Mounted

| Mount | Container Path | Mode | Purpose |
|-------|---------------|------|---------|
| Project directory | `/workspace` | read-write | Your code — changes sync instantly both ways |
| `~/.claude/` | `/root/.claude` | read-write | Settings, memory, sessions, tasks, plugins, commands |
| `~/.claude.json` | `/root/.claude.json` | read-write | Claude Code metadata |
| `~/.gitconfig` | `/root/.gitconfig` | readonly | Git identity |
| `~/.ssh/` | `/root/.ssh` | readonly | SSH keys (opt-in, off by default) |
| Reference volumes | `/refs/{name}` | readonly | Shared source code for inspection |

## Session & Memory Sharing

Claude Code stores sessions under `~/.claude/projects/{encoded-path}/`. The path encoding differs between host and container:

- **Host**: `P:\HC` → `~/.claude/projects/P--HC/`
- **Container**: `/workspace` → `~/.claude/projects/-workspace/`

TerminalHost solves this with an **overlay mount** — Docker's mount precedence lets a specific path override the broader `~/.claude` mount:

```
-v "~/.claude/projects/P--HC:/root/.claude/projects/-workspace"
```

This means sessions, memory, and transcripts written inside the container land in the correct host directory. TerminalHost's Timeline, Tasks, and Session panels see them seamlessly.

## Hook Communication

Claude Code hooks (session-start, file-changed, etc.) normally call `host.exe` on the host. Inside the container, a **proxy script** at `/usr/local/bin/host.exe` forwards hook events to TerminalHost's REST API:

1. Claude Code fires hook → proxy reads JSON from stdin
2. Proxy **translates paths** (e.g., `/workspace/src/file.cs` → `P:\HC\src\file.cs`)
3. Proxy POSTs to `http://host.docker.internal:{port}/api/hooks/{type}`

Path translation uses environment variables set at container creation:
- `TERMINALHOST_HOST_WORKSPACE` — maps `/workspace` to host project path
- `TERMINALHOST_HOST_USERPROFILE` — maps `/root` to host user profile
- `TERMINALHOST_REF_{name}` — maps `/refs/{name}` to host reference volume

**Requires:** REST API enabled in Settings → API & Webhooks.

## Default Docker Image

The image (`terminalhost-workspace:latest`) includes:

- **Ubuntu 24.04** with build-essential, git, curl, jq, ripgrep
- **Node.js 22** (via NVM) + **Bun**
- **Python 3** with pip and venv
- **.NET 8, 9, 10** SDKs
- **Claude Code** (pre-installed)

Customize the Dockerfile at `%APPDATA%\TerminalHost\container\Dockerfile` and rebuild via command palette.

## Configuration

### Global Settings (config.json → settings.container)

```json
{
  "container": {
    "enabled": false,
    "autoApproveInContainer": true,
    "mountSsh": false,
    "referenceVolumes": [
      { "hostPath": "P:\\Vidyano.Service", "name": "Vidyano.Service" },
      { "hostPath": "P:\\Vidyano", "name": "Vidyano" },
      { "hostPath": "P:\\CronosCore", "name": "CronosCore" }
    ]
  }
}
```

### Per-Directory Override (directorySettings)

```json
{
  "directorySettings": {
    "p:\\hc": {
      "containerEnabled": true
    },
    "p:\\personal-scripts": {
      "containerEnabled": false
    }
  }
}
```

## What's Protected / What's Not

**Protected:**
- Host filesystem outside mounted directories
- System packages and OS configuration
- Other running processes

**Not protected:**
- Network access (agents can still call APIs, push to git, etc.)
- Files inside mounted directories (project is read-write)
- Secrets in mounted config files (API keys in `.claude.json`)

The threat model is **accidental damage**, not malicious agents. For network isolation, set `networkMode: "none"` (breaks most AI CLI tools that need API access).

## Command Palette Commands

| Command | Description |
|---------|-------------|
| Container: Toggle for Current Workspace | Enable/disable for active tab |
| Container: Rebuild Image | Rebuild from Dockerfile |
| Container: Stop Current | Stop the active container |
| Container: Remove Current | Remove the active container |
| Container: List All | Show all containers and their state |
| Container: Clean Stopped | Remove stopped containers |
| Container: Check Docker Status | Verify Docker Desktop is running |

## Troubleshooting

**Container won't start**: Check Docker Desktop is running. Use `Container: Check Docker Status` from the palette.

**Image build fails**: Check internet connectivity. The Dockerfile downloads runtimes from the internet. Edit the Dockerfile at `%APPDATA%\TerminalHost\container\Dockerfile`.

**Hooks not working**: Ensure REST API is enabled (`Ctrl+,` → API & Webhooks → Enable). The proxy needs the API server to forward events.

**Sessions don't appear in Timeline**: The overlay mount handles this. If you changed container settings, remove and recreate the container (`Container: Remove Current`, then reopen the tab).

**Drive not shared in Docker Desktop**: For drives beyond C:, ensure the drive is shared in Docker Desktop → Settings → Resources → File Sharing.
