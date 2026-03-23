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
│  │  ~/.claude      (rw) ←──── ~/.claude
│  │  ~/.gitconfig   (ro) ←──── ~/.gitconfig
│  │  ~/.gnupg      (copy) ←──── ~/.gnupg
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
| `~/.claude/` | `~/.claude` | read-write | Settings, memory, sessions, tasks, plugins, commands |
| `~/.claude.json` | `~/.claude.json` | read-write | Claude Code metadata |
| `~/.gitconfig` | `~/.gitconfig` | readonly | Git identity (gpg.program overridden via env) |
| `~/.gnupg/` | `~/.gnupg` | copy | GPG keys for commit signing (copied with correct ownership) |
| `~/.ssh/` | `~/.ssh` | readonly | SSH keys (opt-in, off by default) |
| `~/.config/` | `~/.config` | read-write | Tool settings (ccstatusline, etc.) |
| Reference volumes | `/refs/{name}` | readonly | Shared source code for inspection |
| Generated CLAUDE.md | `~/.claude/CLAUDE.md` | readonly | Container context overlay (tools, refs, paths) |

**Note:** Container user is `developer` (non-root), so `~` = `/home/developer`.

## Session & Memory Sharing

Claude Code stores sessions under `~/.claude/projects/{encoded-path}/`. The path encoding differs between host and container:

- **Host**: `P:\HC` → `~/.claude/projects/P--HC/`
- **Container**: `/workspace` → `~/.claude/projects/-workspace/`

TerminalHost solves this with an **overlay mount** — Docker's mount precedence lets a specific path override the broader `~/.claude` mount:

```
-v "~/.claude/projects/P--HC:/home/developer/.claude/projects/-workspace"
```

This means sessions, memory, and transcripts written inside the container land in the correct host directory. TerminalHost's Timeline, Tasks, and Session panels see them seamlessly.

## Hook Communication

Claude Code hooks (session-start, file-changed, etc.) normally call `host.exe` on the host. Inside the container, a **proxy script** at `/usr/local/bin/host.exe` forwards hook events to TerminalHost's REST API:

1. Claude Code fires hook → proxy reads JSON from stdin
2. Proxy **translates paths** (e.g., `/workspace/src/file.cs` → `P:\HC\src\file.cs`)
3. Proxy POSTs to `http://host.docker.internal:{port}/api/hooks/{type}`

Path translation uses environment variables set at container creation:
- `TERMINALHOST_HOST_WORKSPACE` — maps `/workspace` to host project path
- `TERMINALHOST_HOST_USERPROFILE` — maps `/home/developer` to host user profile
- `TERMINALHOST_REF_{name}` — maps `/refs/{name}` to host reference volume

**Requires:** REST API enabled in Settings → API & Webhooks.

## Git & GPG Configuration

The host's `.gitconfig` is mounted readonly, but some settings contain Windows-specific paths (e.g., `gpg.program=C:\Program Files\Git\usr\bin\gpg.exe`). The container overrides these using `GIT_CONFIG_COUNT` environment variables (highest precedence in git config):

- **`gpg.program=gpg`** — replaces Windows GPG path with Linux binary
- **`core.autocrlf=true`** — prevents phantom diffs from CRLF/LF mismatch on Windows-mounted files
- **`safe.directory=*`** — trusts all mounted directories (set in `/etc/gitconfig`)

**GPG keys** are copied (not mounted) from `~/.gnupg` to the container with correct ownership and permissions. Direct bind-mounting fails because Windows mounts appear as root-owned and GPG rejects "unsafe ownership on homedir". The copy runs on every container start to stay in sync.

## Container-Specific CLAUDE.md

TerminalHost generates a `~/.claude/CLAUDE.md` overlay inside each container. This file is read automatically by Claude Code and includes:

- The host's global CLAUDE.md content (preserved)
- Container environment notice
- Pre-installed tools list
- Reference volume paths with host↔container mapping
- Working directory path mapping

The file is generated on the host at `%APPDATA%\TerminalHost\container\CLAUDE.md` and overlay-mounted on top of the `~/.claude/` directory mount. The host's original `~/.claude/CLAUDE.md` is not modified.

## Environment Variables

All `CLAUDE_CODE_*` environment variables from the host are automatically forwarded to the container. This includes feature flags like `CLAUDE_CODE_ENABLE_PROMPT_SUGGESTION` and `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS`.

## Dockerfile Versioning

TerminalHost embeds a hash in the first line of the generated Dockerfile (`# TerminalHost Dockerfile vN hash:XXXX`). When a new version of TerminalHost ships with an updated Dockerfile template, it compares the embedded hash against the user's current Dockerfile. If the hashes differ, the user is prompted to rebuild the image.

- **Automatic detection**: On startup or when opening a containerized workspace, TerminalHost checks whether the Dockerfile is stale.
- **Guided rebuild**: The command palette action "Container: Rebuild Image" automatically updates a stale Dockerfile to the latest template before building.
- **Manual edits preserved**: If the user has manually edited the Dockerfile and removed or changed the hash header, TerminalHost treats it as a custom Dockerfile and will not overwrite it. A toast notification informs the user that a newer template is available but their custom file was left untouched.
- **First-time build**: When no Dockerfile exists yet, TerminalHost shows a guided dialog explaining the container setup and offering to build the image immediately.

## Default Docker Image

The image (`terminalhost-workspace:latest`) includes:

- **Ubuntu 24.04** with build-essential, git, curl, jq, ripgrep, gnupg
- **Node.js 22** (via NVM) + **Bun**
- **Python 3** with pip and venv
- **.NET 8, 9, 10** SDKs
- **Claude Code** (pre-installed)
- **Claude Code sandbox** — bubblewrap, socat, @anthropic-ai/sandbox-runtime
- **.NET global tools** — HC.Dev (`dev`), dotnet-outdated, SqlInliner, AsicSharp.Cli

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
| Container: Recreate Current | Remove and recreate container (applies settings changes) |
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

**GPG signing fails**: Delete the old Dockerfile, remove the container, and rebuild. The new image includes `gnupg` and overrides `gpg.program`. GPG keys are automatically copied with correct ownership on container start.

**Phantom diffs (every file shows as modified)**: The image sets `core.autocrlf=true` via env vars. If using an old image, rebuild it. For existing repos, run `git config core.autocrlf true` inside the container.

**CLAUDE_CODE_* env vars not available**: These are set at container creation time. If you added new env vars after the container was created, remove and recreate the container.

**Settings changes require new container**: Mount paths and env vars are set at `docker run` time. After changing settings (SSH mount, reference volumes, etc.), remove the container and reopen the tab to recreate it.
