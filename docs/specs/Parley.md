# Parley Integration (Inter-Session Messaging)

> **Status**: Completed
> **Last updated**: 2026-09-24
> **Parley project**: https://github.com/stevehansen/parley (dotnet tool `HC.Parley`, command `parley`)

## Why it was extracted

TerminalHost used to host an in-process collab MCP (`terminalhost-collab`: `CollabService` + `McpHandler` behind `POST /api/mcp`). It had three structural problems:

1. **Polling only.** Plain MCP notifications never reach the model, so a session only saw new messages when it called a tool. Claude Code *channel* notifications do wake an idle session, but they are only honoured for **stdio** servers — an HTTP endpoint inside TerminalHost could never push.
2. **Tied to the app.** Sessions started outside TerminalHost, or while it was closed, had no hub; state lived and died with the UI process.
3. **Session naming heuristics.** HTTP clients arrive anonymous, so TerminalHost guessed names and later renamed sessions by correlating hook events (`LiveSessionTracker.TryFixCollabSessionName`), which was fragile.

Parley fixes all three by splitting the feature into a machine-wide hub and a per-session stdio shim, and it is useful without TerminalHost.

## Architecture

```
Claude Code ──stdio──▶ parley mcp ──HTTP──▶ parley serve (hub, 127.0.0.1:19480)
 (session A)           (shim, one per         topics · messages · cursors · state.json
     ▲                  session)                  │
     └── notifications/claude/channel ◀── SSE ────┘
                                                  │ GET /api/topics|sessions|messages
TerminalHost ── HttpParleyService ────────────────┤ GET /api/events (SSE: message / changed)
```

- **Hub** — one per machine, auto-started by the first shim, persists to `%APPDATA%\Parley\state.json`.
- **Shim** — the MCP server Claude Code launches. Names the session from `PARLEY_SESSION` (else the working-directory folder), forwards tools, and turns messages on the session's topics into channel notifications.

## What TerminalHost still does

| Concern | Where |
|---|---|
| Settings (`settings.parley`: `enabled` = true, `hubUrl` = `http://127.0.0.1:19480`, `pushViaChannels` = false) | `Core/Domain/ParleySettings.cs`; UI in Ctrl+, → API & Webhooks → Parley (WPF + Avalonia) |
| Register `parley` as a user-scope stdio MCP server in `~/.claude.json` (`{"type":"stdio","command":"parley","args":["mcp"]}`) and in Codex (`codex mcp add parley -- parley mcp`) — only when enabled **and** the `parley` tool is found on PATH / `~/.dotnet/tools` | `Core/Services/ParleyLaunchIntegration.cs`, called from both `TerminalControlFactory`s for non-shell (AI) commands |
| Remove the obsolete `terminalhost-collab` entry (`~/.claude.json` entry whose `url` points at `/api/mcp`; Codex entry by name) — always, even when Parley is disabled | same |
| Name the session after the tab: `PARLEY_SESSION=<folder name>` for the AI process (plus `PARLEY_URL` when a non-default hub URL is configured) | same |
| Push delivery: when `pushViaChannels` is on, add `server:parley` to Claude Code's `--dangerously-load-development-channels` (merged with `server:terminalhost` when TerminalHost channels are enabled: `--dangerously-load-development-channels server:terminalhost server:parley`) | `BuildChannelFlags` in both factories |
| Observe the hub: topics (with subscriber connected state), sessions, recent messages; change feed via SSE with reconnect/backoff | `Core/Interfaces/IParleyService.cs`, `Core/Services/HttpParleyService.cs` |
| Claude Tasks panel (Ctrl+Shift+K): Parley topics, subscribers (blue = connected), recent-message feed | `ClaudeTasksPanelViewModel` (WPF + Avalonia) |
| `/api/collab/topics`, `/api/collab/sessions` for Spark Canvas (same JSON shape as before; `subscriberDetails[].connected` added, `claudeSessionId` gone) | `ApiServer` |
| Setup wizard: optional "Parley" check (`parley --version`) | `SetupViewModel` (WPF + Avalonia) |

`HttpParleyService` never throws for a disabled or unreachable hub: queries return empty lists and `IsAvailable` is false. While its watcher runs and the feed is down, queries short-circuit so UI/API polling doesn't wait on a dead port. TerminalHost does not start the hub itself; the first shim does.

## Push via channels

Channels are a Claude Code research preview: they need a claude.ai login (and on Team/Enterprise an admin must allow them). Parley is not an approved channel plugin, so it always uses the development flag, even if TerminalHost's own channel uses `--channels`. Without push everything still works pull-based (tool results list unread counts; `read_messages` can long-poll).

## Migration from the built-in collab

- `ICollabService`, `CollabService`, `McpHandler`, `CollabModels`, `McpModels` and `POST /api/mcp` are gone. The `TerminalHost.Channel` bridge no longer proxies tools; it answers `initialize`, `ping` and an empty `tools/list` locally and only pushes TerminalHost events.
- Old collab state (`collab-state.json`) is not migrated; Parley starts empty.
- Install Parley: Settings → Parley → **Install Parley** (runs `dotnet tool install -g HC.Parley`; **Update Parley** runs `dotnet tool update -g HC.Parley`), or run it manually. The next AI tab launch registers the server and removes `terminalhost-collab`.
- Agents no longer need `set_session_name`; the shim names the session.

## Known limitations

- Duplicate tabs of the same folder share one Parley session name.
- Containerized workspaces: the stdio shim needs `parley` inside the image, and the hub listens on host loopback only, so containerized sessions can't join host topics yet.
- AI commands wrapped in a shell (`wrapCustomInShell`) skip registration and the session env, as they already skipped channel flags.
