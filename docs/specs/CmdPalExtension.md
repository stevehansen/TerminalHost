# PRD: PowerToys Command Palette Extension

> **Maintenance note:** This spec is the source of truth for the CmdPal extension. When modifying the extension project at `src/TerminalHost.CmdPal/`, update the corresponding sections in this document. The extension is a **separate solution** from the main TerminalHost app — it communicates exclusively via the REST API.

## Overview

PowerToys Command Palette provides an extensible quick-launcher with a **Dock** feature — a persistent toolbar docked to a screen edge. Extensions can contribute dock bands (live-updating widgets), searchable list pages, markdown content, and forms.

TerminalHost already exposes rich state via its REST API at `http://127.0.0.1:19280/`. A CmdPal extension consumes this API to surface workspace status, git info, and tasks directly in the Windows desktop toolbar — without needing to focus or Alt-Tab to the TerminalHost window.

## Problem Statement

- **Context switching**: Users must Alt-Tab to TerminalHost to check terminal activity, git status, or switch workspaces.
- **No desktop integration**: TerminalHost state is locked inside the app window. The Status Overlay helps but requires its own window management.
- **Workspace discovery**: With many open tabs, finding and switching to the right project requires opening TerminalHost and scanning the tab strip.
- **Quick peek gap**: Checking git status or task progress requires navigating to the relevant panel inside TerminalHost.

## Goals

1. **Dock band widget** showing active project, git branch, and terminal activity state — always visible at screen edge.
2. **Workspace switcher** via CmdPal search — type project name, press Enter to focus that tab.
3. **Git status quick peek** as rendered markdown — branch, changed files, recent commits.
4. **Task list** showing active tasks with status and Claude metadata.
5. **Zero-config for localhost** — works out of the box when TerminalHost's REST API is enabled.
6. **Graceful offline** — shows "Not connected" when TerminalHost is not running.

---

## Implementation Status

| Phase | Feature | Status |
|-------|---------|--------|
| 1 | Project scaffold (solution, csproj, manifest) | **Completed** |
| 1 | ApiClient + ApiModels (REST API consumer) | **Completed** |
| 1 | HostCli (host.exe launcher for focus/open) | **Completed** |
| 1 | Dock band with live status polling | **Completed** |
| 1 | WorkspacesPage (list + switch tabs) | **Completed** |
| 1 | FocusWindowCommand, SwitchTabCommand, OpenProjectCommand | **Completed** |
| 2 | GitStatusPage (markdown content) | **Completed** |
| 2 | TasksPage (list with status grouping) | **Completed** |
| 3 | TimelinePage (markdown AI session history) | Planned |
| 3 | Quick commit form (Adaptive Card) | Planned |
| 4 | SSE-based live updates (replace polling) | Future |

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                 PowerToys Command Palette                 │
│                                                          │
│  ┌─────────────────────────────────────────────────────┐ │
│  │              Dock (persistent toolbar)               │ │
│  │  [ Home ] [ ... ] [ TerminalHost: master | busy ]   │ │
│  └─────────────────────────────────────────────────────┘ │
│                                                          │
│  ┌─────────────────────────────────────────────────────┐ │
│  │           CmdPal Search / Pages                      │ │
│  │  > TerminalHost: Workspaces                         │ │
│  │  > TerminalHost: Git Status                         │ │
│  │  > TerminalHost: Tasks                              │ │
│  │  > TerminalHost: Focus Window                       │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
        │ (COM/WinRT out-of-process)
        ▼
┌──────────────────────────────────────────────────────────┐
│         TerminalHost.CmdPal Extension (.exe)             │
│                                                          │
│  TerminalHostCommandsProvider                            │
│    ├── TopLevelCommands() → [Workspaces, Git, Tasks,     │
│    │                          Focus, Open Project]       │
│    └── GetDockBands()    → [StatusDockBand]              │
│                                                          │
│  ApiClient (HttpClient)                                  │
│    └── http://127.0.0.1:19280/api/*                     │
│                                                          │
│  HostCli (Process.Start)                                 │
│    └── host.exe <path>                                   │
└──────────────────────────────────────────────────────────┘
        │ (HTTP localhost)          │ (CLI)
        ▼                          ▼
┌──────────────────────────────────────────────────────────┐
│              TerminalHost Desktop App                     │
│                                                          │
│  REST API (HttpListener :19280)                          │
│    GET /api/status, /api/repos, /api/repos/{i}/git,     │
│    GET /api/tasks, /api/timeline, /api/events (SSE)     │
│                                                          │
│  Single Instance IPC (Named Pipe)                        │
│    host.exe <path> → focus/open tab                      │
└──────────────────────────────────────────────────────────┘
```

## Project Structure

```
src/TerminalHost.CmdPal/
├── TerminalHost.CmdPal.sln
├── Directory.Build.props
├── Directory.Packages.props
├── nuget.config
└── TerminalHost.CmdPal/
    ├── TerminalHost.CmdPal.csproj
    ├── Package.appxmanifest
    ├── app.manifest
    ├── Program.cs                          # COM server entry point
    ├── TerminalHostExtension.cs            # IExtension implementation
    ├── TerminalHostCommandsProvider.cs     # CommandProvider (commands + dock)
    ├── Helpers/
    │   ├── ApiClient.cs                    # REST API consumer
    │   ├── ApiModels.cs                    # Response DTOs
    │   └── HostCli.cs                      # host.exe CLI invoker
    ├── Pages/
    │   ├── WorkspacesPage.cs               # ListPage: workspace switcher
    │   ├── GitStatusPage.cs                # ContentPage: markdown git status
    │   └── TasksPage.cs                    # ListPage: task list
    ├── Commands/
    │   ├── FocusWindowCommand.cs           # Focus TerminalHost window
    │   ├── SwitchTabCommand.cs             # Switch to specific tab
    │   └── OpenProjectCommand.cs           # Open folder in TerminalHost
    ├── Dock/
    │   └── StatusDockBand.cs               # Dock widget with live polling
    └── Assets/
        └── (extension icons)
```

## API Endpoints Used

| Endpoint | Used By | Purpose |
|----------|---------|---------|
| `GET /api/status` | StatusDockBand, ApiClient | Server availability + active tab index |
| `GET /api/repos` | StatusDockBand, WorkspacesPage | List all open tabs with git + activity |
| `GET /api/repos/{i}/git` | GitStatusPage | Detailed git status with file list |
| `GET /api/tasks` | TasksPage | Task list with status + Claude metadata |
| `GET /api/events` | Phase 4 (SSE) | Real-time event stream |

## Dock Band Specification

### Display Format

The dock band shows a single strip with the active project's status:

| State | Title | Subtitle |
|-------|-------|----------|
| Active tab (busy) | `TerminalHost` | `master \| 3 changed \| busy` |
| Active tab (idle) | `ProjectName` | `master \| clean` |
| Active tab (waiting) | `ProjectName` | `master \| waiting for input` |
| No active tab | `TerminalHost` | `No open tabs` |
| Offline | `TerminalHost` | `Not connected` |

### Behavior
- Polls `/api/repos` every 5 seconds
- Clicking the dock band opens the WorkspacesPage
- Icon changes based on activity state (Segoe Fluent icons)

## Page Specifications

### WorkspacesPage (ListPage)

Fetches `/api/repos` and displays each open tab as a searchable list item.

| Field | Content |
|-------|---------|
| Title | Directory name (e.g., "TerminalHost") |
| Subtitle | `branch \| N changed \| state` |
| Icon | Activity state icon |
| Tags | "Active" badge on current tab |
| Action | `host.exe <workingDir>` to focus tab |

### GitStatusPage (ContentPage/Markdown)

Fetches `/api/repos/{index}/git` and renders as markdown:

```markdown
# Branch: master
**Status:** 2 ahead, 0 behind | 5 changed (3 staged) | 1 stash

## Changed Files
| Status | Staged | File |
|--------|--------|------|
| Modified | Yes | src/Services/ApiServer.cs |
| Added | No | src/Pages/NewPage.cs |

## Recent Commits
- `abc1234` fix: Batch git operations — *Steve, 2h ago*
- `def5678` feat: Add timeline view — *Steve, 1d ago*
```

### TasksPage (ListPage)

Fetches `/api/tasks` and groups by status (in_progress → pending → completed).

| Field | Content |
|-------|---------|
| Title | Task title |
| Subtitle | `status \| elapsed \| branch` |
| Tags | Status badge, "Claude" if AI-originated |
| Action | `host.exe <projectPath>` to focus repo |

## Prerequisites

- **Windows 11** with PowerToys installed (Command Palette enabled)
- **Developer Mode** enabled (for sideloading during development)
- **TerminalHost REST API** enabled (Settings → API & Webhooks → Enabled)
- **Visual Studio** with C# + WinUI workloads (for building/deploying)

## Build & Deploy

```bash
# Build
dotnet build src/TerminalHost.CmdPal/TerminalHost.CmdPal.sln

# Deploy (from Visual Studio)
# Build → Deploy TerminalHost.CmdPal

# Then in Command Palette: run "Reload" to discover the extension
```

## Security Considerations

- Extension only communicates over localhost (`127.0.0.1`), no network exposure
- No authentication needed (TerminalHost's localhost API requires no API key)
- `host.exe` CLI invocation is the same mechanism users already use manually
- No sensitive data stored by the extension (all state lives in TerminalHost)

---

*Document Version: 1.0*
*Last Updated: 2026-03-18*
