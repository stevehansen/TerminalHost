# Agentic Long-Term Memory

> **Status**: Completed (Eidet integration). Supersedes the original embedded RavenDB design.

## Overview

TerminalHost integrates with [Eidet](https://github.com/stevehansen/eidet) — a standalone local-first memory service for AI coding agents. Rather than embedding a memory library directly, TerminalHost is a **client** of Eidet's REST API.

Each repository gets an isolated memory namespace with typed, append-only entries that persist across sessions. Exposed via MCP tools so Claude Code (and other AI assistants) can store and recall project knowledge, user preferences, coding patterns, and decisions.

## Problem Statement

- **Context amnesia**: AI assistants lose all learned context between sessions — user preferences, architectural decisions, debugging history, codebase insights.
- **Flat-file fragility**: Claude Code's `MEMORY.md` is manually curated, unsearchable beyond grep, has no semantic recall, and grows unwieldy.
- **No cross-session learning**: Repeated mistakes, re-asked questions, and re-discovered patterns waste developer time every session.
- **No recall precision**: Existing solutions either dump everything into context (token waste) or miss relevant memories (semantic gap).

## Goals

1. **Fully local, zero API keys** — RavenDB localhost with built-in embeddings. No external services, no cloud dependencies, no Python.
2. **Per-repo memory with cross-repo linking** — each repo gets its own namespace, but memories can reference and link across repos.
3. **Typed memory entries** — observations, insights, procedures, heuristics with distinct lifecycles and retrieval characteristics.
4. **Memory layers (Docker-like)** — read-only base layers from package authors, shared team layers, and local read-write layer.
5. **Minimal wake-up cost** — L0 (identity, ~50 tokens) + L1 (top-k relevant, ~500 tokens) loaded at session start for <600 token overhead.
6. **Hybrid retrieval** — vector search + full-text + metadata filters in a single query round-trip.
7. **Append-only corrections** — validity intervals instead of deletion; full audit trail.
8. **Intake system** — structured ingestion from CLAUDE.md, README, docs, and package bundles.
9. **Consolidation** — periodic merging of granular observations into stable insights.
10. **MCP integration** — Eidet serves MCP tools directly to AI clients; TerminalHost does not proxy them.
11. **Independent updates** — Eidet updates don't require TerminalHost rebuilds.

---

## Architecture

```
TerminalHost (WPF/Avalonia) → IEidetService → HttpEidetService (HTTP) → Eidet Service → RavenDB

AI Clients (Claude Code, etc.) → Eidet MCP (stdio or HTTP) → Eidet Service → RavenDB
```

Eidet runs as a background service on `localhost:19380`. TerminalHost connects via REST API for UI features. AI clients connect to Eidet's MCP server independently — TerminalHost does not proxy memory MCP tools.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Separate service (not embedded) | Universal memory across all AI clients, independent updates, simpler TerminalHost codebase |
| ~170-line HTTP client vs ~5000-line embedded library | All complexity lives in Eidet — TerminalHost is just UI plumbing |
| No MCP proxy | AI clients connect to Eidet directly; TerminalHost only needs read-only REST for UI panels |
| Graceful degradation | Memory features hidden when Eidet unreachable — app works fine without it |

### Eidet Capabilities (served by Eidet, not TerminalHost)

- Typed entries: Observation, Insight, Procedure, Heuristic
- Docker-like memory layers (local read-write + shared/base read-only)
- Hybrid search (vector + full-text + metadata)
- <600 token wake-up context (L0 identity + L1 top-k)
- 13 MCP tools (`eidet_store`, `eidet_recall`, `eidet_context`, etc.)
- Write gates (secret scanning + signal filter)
- Echo/fizzle feedback loop
- Differential decay
- Cross-repo linking
- Ollama enrichment (optional)
- Intake from CLAUDE.md, README, docs
- Consolidation (observations → insights)
- Bundle export/import

---

## TerminalHost Integration Points

### 1. `IEidetService` — Port

**File**: `TerminalHost.Core/Interfaces/IEidetService.cs`

Single port hiding HTTP transport, connection state machine, retry policy, and JSON
serialization. Consumers (ApiServer, MemoryBrowser, Settings, MainViewModel) depend
only on this interface. Exposes typed memory operations and an observable
`MemoryStatus` via the `StatusChanged` event.

### 2. `HttpEidetService` — Production Adapter

**File**: `TerminalHost.Core/Services/HttpEidetService.cs`

Wraps the Eidet REST API and owns the connection-state machine, intake tracking,
and user-facing toasts/debug-log integration. Folds the former `EidetClient` (HTTP
wrapper) and `EidetClientService` (lifecycle) into a single deep module.

| TerminalHost needs | Eidet endpoint |
|-------------------|----------------|
| Health check on connect | `GET /api/health` |
| Test connection (Settings UI) | `GET /api/status` |
| Trigger intake for opened project | `POST /api/eidet/intake` |
| Memory stats | `GET /api/eidet/stats?repo=...` |
| Search for Memory Browser | `GET /api/eidet/search?repo=...&q=...` |
| Browse memories | `GET /api/eidet/browse?repo=...&type=...` |
| List layers | `GET /api/eidet/layers?repo=...` |
| Forget memory | `DELETE /api/eidet/{id}` |
| Proxy GET (for API) | Raw path forwarding |

Lifecycle:

- **App startup**: If `Memory.Enabled`, health-check Eidet, auto-intake for restored tabs
- **Settings change**: Connect or disconnect as needed
- **Tab opened**: Auto-intake (first time per repo)
- **Manual intake**: Via command palette
- **Status**: `MemoryConnectionStatus` (Disabled/Connecting/Connected/Error)

### 2a. `InMemoryEidetService` — Test Adapter

**File**: `tests/TerminalHost.Tests/TestAdapters/InMemoryEidetService.cs`

Deterministic, no-HTTP implementation used by the boundary tests in
`EidetServiceBoundaryTests`. Exposes seed/inspection helpers (`Seed`, `SeedLayers`,
`IntakeCallCounts`, `SimulatedHealthFailure`) so tests can pin down state machine
transitions, recall/browse/forget semantics, and proxy fallback behavior.

### 3. MCP Tools — Eidet Serves Directly

TerminalHost does **not** proxy memory MCP tools. AI clients connect to Eidet's MCP server independently:

```json
{
  "mcpServers": {
    "eidet": {
      "command": "eidet",
      "args": ["mcp"]
    }
  }
}
```

Eidet's MCP server uses the current working directory to determine the repo. TerminalHost's `McpHandler` has no memory tool references.

### 4. Memory REST API — Thin Proxy for UI

TerminalHost's `ApiServer` keeps a thin set of read-only endpoints for its own UI panels (Memory Browser, status display). These proxy to Eidet:

| TerminalHost API | Proxies to |
|-----------------|------------|
| `GET /api/memory/context` | `GET /api/eidet/context` |
| `GET /api/memory/search` | `GET /api/eidet/search` |
| `GET /api/memory/stats` | `GET /api/eidet/stats` |
| `GET /api/memory/layers` | `GET /api/eidet/layers` |

Query parameters (`repo`, `q`, `type`, `limit`, etc.) are forwarded to Eidet unchanged — Eidet uses the same names.

Write operations go through Eidet's MCP tools or directly to Eidet's REST API — not through TerminalHost.

### 5. Settings

**File**: `TerminalHost.Core/Domain/MemorySettings.cs` (~30 lines)

```csharp
public class MemorySettings
{
    public bool Enabled { get; set; }
    public string EidetUrl { get; set; } = "http://localhost:19380";
}
```

All memory-specific settings (L1 count, duplicate threshold, Ollama config, maintenance intervals, etc.) are managed by Eidet's own config at `~/.eidet/config.json`.

TerminalHost Settings UI shows:
- Enabled checkbox
- Eidet URL text field
- "Test Connection" button → `GET /api/status`
- Connection status display (version, document count)

### 6. Memory Browser Panel

The Memory Browser ViewModel depends on `IEidetService` which (via `HttpEidetService`) hits Eidet's REST API:

- Browse/filter memories → `GET /api/eidet/search` / `GET /api/eidet/browse`
- View memory details → `GET /api/eidet/{id}`
- Layer stack display → `GET /api/eidet/layers`
- Export → `GET /api/eidet/export`
- Stats → `GET /api/eidet/stats`

### 7. Domain Models

**File**: `TerminalHost.Core/Domain/EidetTypes.cs` (~200 lines)

Slim DTOs for JSON deserialization — no dependency on Eidet.Core:

- `MemoryType`, `MemoryProvenance`, `LayerType` enums
- `EidetStatusResponse`, `EidetMemoryEntry`, `EidetSearchResult`, `EidetSearchResponse`
- `EidetStatsResponse`, `EidetLayerInfo`, `EidetLayersResponse`
- `EidetIntakeResponse`, `EidetMemoriesResponse`

### 8. RepoId Normalization

**File**: `TerminalHost.Core/Services/RepoIdNormalizer.cs` (~30 lines)

Converts directory paths to stable, filesystem-safe identifiers using the same encoding as Claude Code's project path format:

- `P:\TerminalHost` → `P--TerminalHost`
- `/Users/steve/projects/my-app` → `-Users-steve-projects-my-app`

---

## Containerized Workspaces

When TerminalHost runs AI agents in Docker containers, Eidet's stdio MCP server can't work because:

1. **Binary unavailable** — The host's `eidet` dotnet tool doesn't exist inside the container
2. **Path mismatch** — Container sees `/workspace/MyProject` but the repoId should derive from the host path `P:\MyProject`

### Solution: HTTP MCP + Container Overlay

Eidet exposes MCP over Streamable HTTP at `POST /mcp` on port 19380 (started via `eidet serve`). TerminalHost's container overlay converts the eidet stdio entry to HTTP.

```
Normal workspace:
  Claude Code → stdio → eidet mcp (local process, CWD = project dir)

Containerized workspace:
  Claude Code (container) → HTTP POST → host.docker.internal:19380/mcp?repo=P--MyProject
```

### What TerminalHost Does

`ContainerService.GenerateContainerSettings()` handles the `~/.claude/settings.json` case (where eidet registers via `dotnet tool install`):

1. Reads `~/.claude/settings.json` which contains eidet's stdio MCP entry
2. Detects the eidet entry has `"command"` (stdio transport)
3. Writes HTTP override to `settings.local.json`: `{"type": "url", "url": "http://host.docker.internal:19380/mcp?repo=P--MyProject"}`
4. Mounts as `~/.claude/settings.local.json` in the container (overrides settings.json)

`ContainerService.GenerateContainerClaudeJson()` handles the legacy `~/.claude.json` case:

1. Reads `~/.claude.json`, detects eidet stdio entry
2. Replaces with HTTP URL entry via `ConvertEidetMcpToHttp()`
3. Writes per-workspace overlay `claude-{folderName}.json`
4. Mounts as `~/.claude.json` in the container

The `repo` query parameter carries the **host path's normalized repoId**, ensuring memories land in the correct repo regardless of the container mount path.

### Eidet-Side Support

Eidet's HTTP `/mcp` endpoint accepts a `repo` query parameter for per-request repoId scoping:

```csharp
var repoOverride = ctx.Request.QueryString["repo"];
var server = string.IsNullOrEmpty(repoOverride)
    ? _mcpServer                              // Default: service-level repoId
    : _mcpServerPool.GetOrAdd(repoOverride, id =>
        new McpServer(_svc, _intake, _consolidation, _maintenance, _export, id));
```

This was implemented in Eidet commit `caae407`.

---

## Implementation Summary

### New Files

| File | Lines | Purpose |
|------|-------|---------|
| `Core/Domain/EidetTypes.cs` | ~245 | Slim DTOs + MemoryStatus / MemoryConnectionStatus |
| `Core/Domain/MemorySettings.cs` | ~30 | Enabled + EidetUrl settings |
| `Core/Interfaces/IEidetService.cs` | ~70 | Port hiding HTTP + state machine |
| `Core/Services/HttpEidetService.cs` | ~320 | Production adapter (folds former EidetClient + EidetClientService) |
| `Core/Services/RepoIdNormalizer.cs` | ~30 | Path → repoId |
| `tests/.../TestAdapters/InMemoryEidetService.cs` | ~210 | Deterministic in-memory adapter |
| `tests/.../Services/EidetServiceBoundaryTests.cs` | ~210 | 15 boundary tests against the port |

### Modified Files

| File | Change |
|------|--------|
| `Core/Services/ApiServer.cs` | Injects `IEidetService`, 4 proxy routes, `HandleMemoryProxyAsync()`, `NormalizePathId()` uses `RepoIdNormalizer` |
| `Core/Services/McpHandler.cs` | Added `workingDirHint`, session directory tracking, `ResolveRepoId()`, removed memory tool references |
| `Core/Services/ContainerService.cs` | `GenerateContainerSettings()` adds eidet HTTP override, `ConvertEidetMcpToHttp()` for legacy .claude.json, per-workspace overlay names, `HC.SafeCommands` in Dockerfile |
| `Core/ViewModels/SettingsTabViewModel.cs` | Simplified to Enabled + EidetUrl + TestConnection; `IEidetService.TestConnectionAsync` replaces static helper |
| `WPF App.xaml.cs` | DI registers `IEidetService` → `HttpEidetService`; `AutoConnectMemoryAsync()` |
| `WPF MainWindow.xaml.cs` | Settings changed → `IEidetService.OnSettingsChangedAsync()` |
| `WPF MainViewModel.cs` | Project opened → `IEidetService.OnProjectOpenedAsync()` |
| `WPF SettingsView.xaml` | Simplified memory settings section |
| `WPF/Avalonia MemoryBrowserViewModel.cs` | Depends on `IEidetService` directly (no more `eidet.Client` indirection) |

### Removed

| What | Why |
|------|-----|
| `TerminalHost.Memory` project reference from Core | Replaced by `HttpEidetService` |
| `EidetClient.cs`, `EidetClientService.cs` (and `SetEidetClient` callback on `ApiServer`) | Folded into `HttpEidetService` behind `IEidetService` (RFC #52) |
| Memory MCP tools from McpHandler | Eidet serves them directly |
| Memory write endpoints from ApiServer | Go through Eidet directly |
| Complex MemorySettings (RavenDB, Ollama, etc.) | Managed by Eidet's own config |

---

## Benefits

- **No RavenDB dependency in TerminalHost** — no embedded database, no connection management
- **Universal memory** — same memories available to Claude Code, Cursor, Windsurf, any MCP client
- **Independent updates** — Eidet updates don't require TerminalHost rebuilds
- **Simpler codebase** — ~170-line client vs ~5000-line embedded memory library
- **Container support** — stdio→HTTP conversion via overlay, per-repo scoped MCP sessions

## Trade-offs

- **Requires Eidet running** — memory unavailable without the service (graceful degradation handles this)
- **REST latency** — ~1-5ms per call vs in-process (negligible for UI operations)
- **Settings split** — memory config lives in Eidet, not TerminalHost (cleaner separation)
