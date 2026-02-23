# PRD: REST API & Webhooks

## Overview

TerminalHost accumulates rich state — git status, terminal activity, AI sessions, tasks, timeline, configuration — that is currently locked inside the desktop process. A lightweight REST API and webhook system exposes this data externally, enabling dashboards, mobile apps, browser extensions, and automation pipelines to consume TerminalHost state in real time.

## Problem Statement

- **No external access**: All application state (repos, git, AI sessions, terminal activity) is only visible inside the WPF/Avalonia desktop window.
- **No event push**: External tools cannot react to TerminalHost events (e.g., commit created, session started, branch switched) without polling.
- **Dashboard gap**: Users building custom dashboards or mobile companion apps have no data source.
- **Automation gap**: CI/CD, chat integrations (Slack/Discord), and monitoring tools cannot be wired to TerminalHost events.

## Goals

1. **Read-only REST API** exposing application state (repos, git status, activity, tasks, timeline, config) over HTTP.
2. **SSE streaming** for real-time event delivery to local clients.
3. **Webhook push** to configured HTTP endpoints with debounce, retry, and batching.
4. **Scriban template customization** for power users who need custom payload shapes.
5. **Secure by default** — localhost-only binding, optional API key for network exposure.
6. **Lightweight** — no ASP.NET/Kestrel dependency; uses `HttpListener` for minimal footprint.
7. **Cross-platform** — all new code in `TerminalHost.Core` where possible.

---

## Implementation Status

| Phase | Feature | Status |
|-------|---------|--------|
| 1 | Core REST API endpoints | **Completed** |
| 1 | Settings model & UI | **Completed** |
| 1 | Authentication (API key) | **Completed** |
| 2 | SSE event streaming | **Completed** |
| 2 | Event aggregation service | **Completed** |
| 3 | Webhooks with fixed JSON payloads | **Completed** |
| 3 | Debounce, retry, batching | **Completed** |
| 4 | Scriban template customization | Planned |
| 5 | Write endpoints (POST/PUT) | Future |
| 5 | MCP server integration | Future |

---

## Configuration Schema

### Settings Model

Added as a nested object under `AppSettings`:

```csharp
public class ApiSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 19280;

    [JsonPropertyName("bindAddress")]
    public string BindAddress { get; set; } = "127.0.0.1";

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("enableSse")]
    public bool EnableSse { get; set; } = true;

    [JsonPropertyName("enableWebhooks")]
    public bool EnableWebhooks { get; set; } = false;

    [JsonPropertyName("webhooks")]
    public List<WebhookEndpoint> Webhooks { get; set; } = new();

    [JsonPropertyName("corsOrigins")]
    public List<string> CorsOrigins { get; set; } = new() { "http://localhost:*" };
}

public class WebhookEndpoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new() { "*" };

    [JsonPropertyName("debounceMs")]
    public int DebounceMs { get; set; } = 500;

    [JsonPropertyName("batchWindow")]
    public int BatchWindowMs { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("templatePath")]
    public string? TemplatePath { get; set; }
}
```

### JSON Configuration Example

```json
{
  "settings": {
    "api": {
      "enabled": true,
      "port": 19280,
      "bindAddress": "127.0.0.1",
      "apiKey": null,
      "enableSse": true,
      "enableWebhooks": true,
      "webhooks": [
        {
          "id": "slack01",
          "name": "Slack #dev-activity",
          "url": "https://hooks.slack.com/services/T.../B.../xxx",
          "enabled": true,
          "secret": "whsec_abc123",
          "events": ["repo.commit", "repo.branch_switched", "session.*"],
          "debounceMs": 2000,
          "batchWindowMs": 5000,
          "maxRetries": 3,
          "templatePath": null
        }
      ],
      "corsOrigins": ["http://localhost:*"]
    }
  }
}
```

### AppSettings Integration

```csharp
public class AppSettings
{
    // ... existing properties ...

    [JsonPropertyName("api")]
    public ApiSettings Api { get; set; } = new();
}
```

---

## Security Model

### Localhost-Only (Default)

When `bindAddress` is `127.0.0.1` (default):
- No authentication required — only local processes can connect.
- API key is optional but ignored if set.
- CORS allows configured origins (default: `http://localhost:*`).

### Network Exposure (Opt-In)

When `bindAddress` is `0.0.0.0` or a specific non-loopback IP:
- **API key is required** — server refuses to start without one.
- API key sent via `Authorization: Bearer <key>` header or `?key=<key>` query parameter.
- Settings UI shows a warning when binding to a non-loopback address.

### Webhook Signatures

Outgoing webhook requests include an HMAC-SHA256 signature when a `secret` is configured:

```
X-TerminalHost-Signature: sha256=<hex-encoded HMAC of request body>
X-TerminalHost-Event: repo.commit
X-TerminalHost-Delivery: <unique delivery id>
X-TerminalHost-Timestamp: 1708700000
```

Receivers verify by computing `HMAC-SHA256(secret, raw body)` and comparing.

---

## REST API Endpoints

All endpoints return JSON. Base URL: `http://127.0.0.1:19280`.

### GET /api/status

Application-level status.

**Response:**
```json
{
  "version": "1.4.0",
  "uptime": "02:15:33",
  "uptimeSeconds": 8133,
  "tabCount": 3,
  "activeTabIndex": 1,
  "layoutMode": "Tabs",
  "touchMode": false,
  "platform": "Windows",
  "apiVersion": "1"
}
```

### GET /api/repos

List all open repository tabs.

**Response:**
```json
{
  "repos": [
    {
      "index": 0,
      "title": "TerminalHost",
      "workingDirectory": "P:\\TerminalHost",
      "isActive": false,
      "layout": "HorizontalSplit",
      "splitRatio": 0.6,
      "activeTerminal": "Custom",
      "git": {
        "branch": "master",
        "isDirty": true,
        "ahead": 2,
        "behind": 0,
        "stashCount": 1
      },
      "terminals": {
        "custom": { "title": "Claude Code", "isActive": true },
        "shell": { "title": "PowerShell", "isActive": false },
        "run": null
      },
      "panels": {
        "center": null,
        "left": ["FileExplorer"],
        "right": ["ScratchPad"]
      }
    }
  ]
}
```

### GET /api/repos/{index}

Detailed status for a single repo tab.

**Path parameters:**
- `index` — tab index (0-based), matching the order in `/api/repos`

**Response:** Same shape as a single item from `/api/repos`, plus additional detail:

```json
{
  "index": 0,
  "title": "TerminalHost",
  "workingDirectory": "P:\\TerminalHost",
  "isActive": true,
  "layout": "HorizontalSplit",
  "splitRatio": 0.6,
  "activeTerminal": "Custom",
  "git": {
    "branch": "master",
    "isDirty": true,
    "ahead": 2,
    "behind": 0,
    "stashCount": 1,
    "changedFiles": 5,
    "stagedFiles": 2,
    "untrackedFiles": 1
  },
  "terminals": {
    "custom": { "title": "Claude Code", "isActive": true },
    "shell": { "title": "PowerShell", "isActive": false },
    "run": null
  },
  "panels": {
    "center": null,
    "left": ["FileExplorer"],
    "right": ["ScratchPad"]
  },
  "runConfiguration": {
    "id": "dotnet-run",
    "label": "dotnet run",
    "isRunning": false
  },
  "aiAssistant": {
    "id": "claude",
    "name": "Claude Code",
    "icon": "\ud83e\udd16"
  }
}
```

### GET /api/repos/{index}/git

Full git status for a repo tab.

**Response:**
```json
{
  "branch": "master",
  "isDirty": true,
  "ahead": 2,
  "behind": 0,
  "stashCount": 1,
  "files": [
    {
      "path": "src/Services/ApiService.cs",
      "status": "Modified",
      "isStaged": true,
      "oldPath": null
    },
    {
      "path": "README.md",
      "status": "Modified",
      "isStaged": false,
      "oldPath": null
    }
  ],
  "recentCommits": [
    {
      "hash": "ac3ae44",
      "message": "fix: Prevent center panel showing wrong repo data on startup",
      "author": "Steve",
      "date": "2026-02-22T14:30:00Z"
    }
  ]
}
```

### GET /api/repos/{index}/files

File explorer tree for a repo tab. Returns the same data the File Explorer panel displays.

**Query parameters:**
- `depth` (optional, default: 3) — max directory depth to return
- `path` (optional) — subtree root path relative to working directory

**Response:**
```json
{
  "workingDirectory": "P:\\TerminalHost",
  "tree": [
    {
      "name": "src",
      "path": "src",
      "isDirectory": true,
      "children": [
        {
          "name": "TerminalHost.Core",
          "path": "src/TerminalHost.Core",
          "isDirectory": true,
          "children": []
        }
      ]
    },
    {
      "name": "README.md",
      "path": "README.md",
      "isDirectory": false,
      "size": 4096,
      "gitStatus": "Modified"
    }
  ]
}
```

### GET /api/repos/{index}/links

Detected links from terminal output for a repo tab.

**Response:**
```json
{
  "links": [
    {
      "text": "https://github.com/user/repo/pull/42",
      "url": "https://github.com/user/repo/pull/42",
      "type": "Url",
      "source": "Custom"
    },
    {
      "text": "src/Services/ApiService.cs:42",
      "path": "P:\\TerminalHost\\src\\Services\\ApiService.cs",
      "line": 42,
      "type": "File",
      "source": "Shell"
    }
  ]
}
```

### GET /api/tasks

Active focus tasks (from the Tasks panel).

**Response:**
```json
{
  "tasks": [
    {
      "id": "abc123",
      "title": "Implement REST API",
      "description": "Add HTTP endpoints for external access",
      "status": "InProgress",
      "createdAt": "2026-02-23T10:00:00Z",
      "repoIndex": 0
    }
  ]
}
```

### GET /api/timeline

Timeline data (AI sessions, intents, worktrees).

**Query parameters:**
- `since` (optional) — ISO 8601 timestamp, return events after this time
- `limit` (optional, default: 50) — max items to return

**Response:**
```json
{
  "intents": [
    {
      "id": "intent-001",
      "name": "REST API Feature",
      "status": "Active",
      "branchName": "feature/rest-api",
      "repoPath": "P:\\TerminalHost",
      "createdAt": "2026-02-23T10:00:00Z",
      "sessions": [
        {
          "id": "session-001",
          "status": "Running",
          "startedAt": "2026-02-23T10:05:00Z",
          "endedAt": null,
          "commitHash": null,
          "commitMessage": null
        }
      ]
    }
  ]
}
```

### GET /api/config

Read-only view of application configuration (sensitive fields redacted).

**Response:**
```json
{
  "settings": {
    "customCommand": "claude.exe",
    "customCommandName": "Claude Code",
    "shellCommand": "pwsh.exe",
    "shellCommandName": "PowerShell",
    "touchMode": false,
    "layoutMode": "Tabs",
    "confirmOnClose": true
  },
  "quickCommands": [
    {
      "id": "commit",
      "label": "Commit",
      "icon": "\ud83d\udcbe",
      "shortcut": "Ctrl+Shift+C"
    }
  ],
  "profiles": [
    {
      "name": "Default",
      "command": "claude.exe"
    }
  ],
  "aiAssistants": [
    {
      "id": "claude",
      "name": "Claude Code",
      "icon": "\ud83e\udd16",
      "command": "claude.exe"
    }
  ]
}
```

**Redacted fields:** API keys, webhook secrets, file paths containing usernames (replaced with `~`).

### Error Responses

All errors use a consistent shape:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Repo index 5 not found. Valid range: 0-2."
  }
}
```

| HTTP Status | Code | When |
|-------------|------|------|
| 400 | `BAD_REQUEST` | Invalid query parameters |
| 401 | `UNAUTHORIZED` | Missing or invalid API key (when required) |
| 404 | `NOT_FOUND` | Invalid repo index, unknown endpoint |
| 500 | `INTERNAL_ERROR` | Unhandled exception |

---

## Event Types

Events are emitted when application state changes. Used by both SSE streaming and webhooks.

### Event Schema

Every event shares a common envelope:

```json
{
  "id": "evt_abc123",
  "type": "repo.git_status_changed",
  "timestamp": "2026-02-23T10:15:00Z",
  "repoIndex": 0,
  "data": { }
}
```

### Event Catalog

| Event Type | Trigger | Data Payload |
|------------|---------|-------------|
| **Repo Events** | | |
| `repo.opened` | New tab opened | `{ workingDirectory, title }` |
| `repo.closed` | Tab closed | `{ workingDirectory, title }` |
| `repo.activated` | Tab focused | `{ workingDirectory, title, previousIndex }` |
| `repo.git_status_changed` | Git status refresh | `{ branch, isDirty, ahead, behind, changedFiles, stagedFiles }` |
| `repo.branch_switched` | Branch checkout | `{ previousBranch, newBranch }` |
| `repo.commit` | Commit created | `{ hash, message, author, filesChanged }` |
| `repo.terminal_activity` | Terminal output detected | `{ terminal, isActive }` |
| `repo.layout_changed` | Split/layout changed | `{ layout, splitRatio, activeTerminal }` |
| `repo.panel_changed` | Panel opened/closed | `{ panel, state, side }` |
| `repo.run_started` | Project run started | `{ configId, configLabel }` |
| `repo.run_stopped` | Project run stopped | `{ configId, exitCode }` |
| **Session Events** | | |
| `session.started` | AI session started | `{ sessionId, intentId, initialPrompt }` |
| `session.ended` | AI session ended | `{ sessionId, status, commitHash, commitMessage }` |
| `session.intent_created` | New intent created | `{ intentId, name, branchName }` |
| `session.intent_closed` | Intent closed | `{ intentId, name, status }` |
| **Task Events** | | |
| `task.created` | Focus task created | `{ taskId, title, description }` |
| `task.updated` | Focus task updated | `{ taskId, title, status, previousStatus }` |
| `task.deleted` | Focus task deleted | `{ taskId, title }` |
| **App Events** | | |
| `app.started` | Application launched | `{ version, platform }` |
| `app.settings_changed` | Settings modified | `{ changedKeys }` |
| `app.config_reloaded` | Config reloaded from disk | `{}` |

### Event Source Mapping

How events map to existing TerminalHost event sources:

| Event Type | Source | Signal |
|------------|--------|--------|
| `repo.git_status_changed` | `IGitStatusService.StatusChanged` | EventHandler on git poll |
| `repo.branch_switched` | `GitStatus.Branch` property change | Compare previous/current branch |
| `repo.commit` | `IGitStatusService` commit detection | Compare commit HEADs between polls |
| `repo.terminal_activity` | `ConPTYTerm.InterceptOutputToUITerminal` | Terminal output callback |
| `repo.opened` / `repo.closed` | `MainViewModel.Tabs` CollectionChanged | Add/Remove events |
| `repo.activated` | `MainViewModel.SelectedTab` PropertyChanged | Selection change |
| `session.*` | `ITimelineService` events | `SessionStatusChanged`, `IntentsChanged` |
| `task.*` | `AppConfiguration.Tasks` changes | Save/load events |
| `app.settings_changed` | `IConfigurationService` save | After config save |

---

## SSE Streaming

### GET /api/events

Server-Sent Events endpoint for real-time event delivery.

**Query parameters:**
- `events` (optional) — comma-separated event type filter (supports `*` wildcards, e.g. `repo.*,session.*`)
- `repos` (optional) — comma-separated repo indices to filter

**Request:**
```
GET /api/events?events=repo.*,session.*&repos=0,1
Accept: text/event-stream
```

**Response (streaming):**
```
: connected to TerminalHost event stream

event: repo.git_status_changed
id: evt_001
data: {"id":"evt_001","type":"repo.git_status_changed","timestamp":"2026-02-23T10:15:00Z","repoIndex":0,"data":{"branch":"master","isDirty":true,"ahead":2,"behind":0,"changedFiles":5,"stagedFiles":2}}

event: repo.commit
id: evt_002
data: {"id":"evt_002","type":"repo.commit","timestamp":"2026-02-23T10:16:00Z","repoIndex":0,"data":{"hash":"abc1234","message":"feat: Add REST API","author":"Steve","filesChanged":3}}

: heartbeat
```

**Behavior:**
- Sends a comment line (`: connected...`) immediately on connect.
- Heartbeat comment every 30 seconds to keep the connection alive.
- Each event includes `id:` for `Last-Event-ID` reconnection support.
- Client can reconnect with `Last-Event-ID` header to resume from a missed event (buffer: last 100 events).
- Maximum 10 concurrent SSE connections.

---

## Webhook System

### Delivery

When an event matches a webhook's `events` filter:

1. **Debounce**: Wait `debounceMs` after the first event. If more events of the same type arrive for the same repo, only the latest is delivered (unless batching is enabled).
2. **Batch** (optional): If `batchWindowMs > 0`, collect all events within the window and deliver as an array.
3. **Sign**: If `secret` is set, compute HMAC-SHA256 signature.
4. **POST**: Send to the webhook URL.

### Single Event Delivery

```
POST https://hooks.slack.com/services/T.../B.../xxx
Content-Type: application/json
X-TerminalHost-Event: repo.commit
X-TerminalHost-Delivery: del_abc123
X-TerminalHost-Timestamp: 1708700000
X-TerminalHost-Signature: sha256=a1b2c3...

{
  "id": "evt_002",
  "type": "repo.commit",
  "timestamp": "2026-02-23T10:16:00Z",
  "repoIndex": 0,
  "data": {
    "hash": "abc1234",
    "message": "feat: Add REST API",
    "author": "Steve",
    "filesChanged": 3
  }
}
```

### Batch Delivery

When `batchWindowMs > 0`:

```json
{
  "batch": true,
  "events": [
    {
      "id": "evt_001",
      "type": "repo.git_status_changed",
      "timestamp": "2026-02-23T10:15:00Z",
      "repoIndex": 0,
      "data": { }
    },
    {
      "id": "evt_002",
      "type": "repo.commit",
      "timestamp": "2026-02-23T10:16:00Z",
      "repoIndex": 0,
      "data": { }
    }
  ]
}
```

### Retry Policy

| Attempt | Delay | Notes |
|---------|-------|-------|
| 1 | Immediate | First delivery |
| 2 | 5 seconds | After first failure |
| 3 | 30 seconds | After second failure |
| 4 | 5 minutes | Final attempt |

- Retries only on network errors and 5xx responses.
- 4xx responses are not retried (client error).
- After `maxRetries` exhausted, event is dropped and a warning toast is shown.
- Delivery state persisted in memory (not across restarts).

### Event Filtering

Webhook `events` field supports:
- Exact match: `"repo.commit"`
- Wildcard suffix: `"repo.*"` matches all repo events
- All events: `"*"`
- Multiple: `["repo.commit", "session.*"]`

---

## Scriban Templates (Phase 4)

### Overview

By default, webhooks send the standard JSON event envelope. Power users can customize payloads using [Scriban](https://github.com/scriban/scriban) templates, e.g., to format messages for Slack blocks, Discord embeds, or custom API shapes.

### Template Configuration

Set `templatePath` on a webhook endpoint to a `.sbn` file path (relative to config directory):

```json
{
  "id": "slack01",
  "url": "https://hooks.slack.com/services/...",
  "events": ["repo.commit"],
  "templatePath": "webhooks/slack-commit.sbn"
}
```

### Template Context

Templates receive the full event object as the root context:

```
{{ id }}           → "evt_002"
{{ type }}         → "repo.commit"
{{ timestamp }}    → "2026-02-23T10:16:00Z"
{{ repo_index }}   → 0
{{ data.hash }}    → "abc1234"
{{ data.message }} → "feat: Add REST API"
```

### Example: Slack Block Kit

File: `%APPDATA%\TerminalHost\webhooks\slack-commit.sbn`

```
{
  "blocks": [
    {
      "type": "section",
      "text": {
        "type": "mrkdwn",
        "text": "*{{ data.message }}*\nRepo: {{ data.working_directory | string.split '\\' | array.last }} | Branch: {{ data.branch }}\n`{{ data.hash }}`"
      }
    }
  ]
}
```

### Template Validation

- Templates are validated (parsed) when configuration is saved.
- Parse errors are reported via toast notification with the file path and error line.
- A `Test Webhook` button in settings sends a synthetic event to verify the full pipeline.

---

## Implementation Architecture

### New Interfaces (`TerminalHost.Core/Interfaces/`)

```csharp
/// <summary>
/// Lightweight HTTP server for the REST API and SSE endpoints.
/// </summary>
public interface IApiServer : IDisposable
{
    /// <summary>Start listening on the configured port.</summary>
    Task StartAsync();

    /// <summary>Stop the server gracefully.</summary>
    Task StopAsync();

    /// <summary>Whether the server is currently listening.</summary>
    bool IsRunning { get; }

    /// <summary>The base URL the server is listening on.</summary>
    string? BaseUrl { get; }
}

/// <summary>
/// Aggregates events from various TerminalHost subsystems
/// and distributes them to SSE clients and webhook endpoints.
/// </summary>
public interface IEventAggregatorService
{
    /// <summary>Publish an event to all subscribers.</summary>
    void Publish(ApiEvent apiEvent);

    /// <summary>Subscribe to events (for SSE connections).</summary>
    IDisposable Subscribe(Action<ApiEvent> handler, string? eventFilter = null);

    /// <summary>Recent event buffer for SSE reconnection.</summary>
    IReadOnlyList<ApiEvent> RecentEvents { get; }
}

/// <summary>
/// Manages webhook delivery including debounce, retry, and batching.
/// </summary>
public interface IWebhookDeliveryService : IDisposable
{
    /// <summary>Enqueue an event for delivery to matching webhooks.</summary>
    void Enqueue(ApiEvent apiEvent);

    /// <summary>Get delivery statistics.</summary>
    WebhookDeliveryStats GetStats();
}
```

### New Domain Models (`TerminalHost.Core/Domain/`)

```csharp
/// <summary>
/// An event emitted by the API system.
/// </summary>
public class ApiEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"evt_{Guid.NewGuid():N}"[..12];

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("repoIndex")]
    public int? RepoIndex { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// Webhook delivery statistics for diagnostics.
/// </summary>
public class WebhookDeliveryStats
{
    public int TotalDelivered { get; set; }
    public int TotalFailed { get; set; }
    public int PendingRetries { get; set; }
    public Dictionary<string, int> DeliveriesByEndpoint { get; set; } = new();
}
```

### Service Implementations

| Service | Project | Responsibility |
|---------|---------|---------------|
| `ApiServer` | `TerminalHost.Core/Services/` | `HttpListener`-based HTTP server, route dispatch, SSE streaming |
| `EventAggregatorService` | `TerminalHost.Core/Services/` | Central event bus, subscriber management, event buffer |
| `WebhookDeliveryService` | `TerminalHost.Core/Services/` | Debounce timers, retry queues, HTTP POST delivery, HMAC signing |
| `EventWiringService` | `TerminalHost.Core/Services/` | Subscribes to existing events (git, timeline, tabs) and publishes `ApiEvent`s |

### DI Registration

```csharp
// In Windows: App.xaml.cs ConfigureServices()
// In macOS: equivalent Avalonia startup
services.AddSingleton<IEventAggregatorService, EventAggregatorService>();
services.AddSingleton<IWebhookDeliveryService, WebhookDeliveryService>();
services.AddSingleton<IApiServer, ApiServer>();
services.AddSingleton<EventWiringService>(); // Started after DI container built
```

### Thread Safety

- `ApiServer` runs on a dedicated background thread; request handlers marshal to the UI thread via `IDispatcherService` when reading ViewModel state.
- `EventAggregatorService` uses `ConcurrentQueue<ApiEvent>` for the event buffer and a `ReaderWriterLockSlim` for subscriber management.
- `WebhookDeliveryService` uses `System.Threading.Timer` for debounce and `SemaphoreSlim` to limit concurrent outbound HTTP connections (max 5).
- All endpoint handlers are read-only — no state mutation through the API in Phase 1-4.

### Request Flow

```
HTTP Request
    │
    ▼
┌──────────┐     ┌──────────────────┐     ┌─────────────────┐
│ ApiServer │────▶│ Route Dispatcher  │────▶│ Endpoint Handler │
│ (listen)  │     │ (path matching)   │     │ (read VM state)  │
└──────────┘     └──────────────────┘     └─────────────────┘
                                                    │
                                                    ▼
                                            JSON Response

SSE Connection
    │
    ▼
┌──────────┐     ┌────────────────────┐     ┌───────────────┐
│ ApiServer │────▶│ EventAggregator    │────▶│ SSE Writer    │
│ (accept)  │     │ .Subscribe(filter) │     │ (stream out)  │
└──────────┘     └────────────────────┘     └───────────────┘

Internal Event
    │
    ▼
┌──────────────────┐     ┌────────────────────┐     ┌─────────────────────┐
│ EventWiringService│────▶│ EventAggregator    │────▶│ WebhookDeliveryService│
│ (subscribe to     │     │ .Publish(event)    │     │ .Enqueue(event)      │
│  git, timeline,  │     └────────────────────┘     │ → debounce → POST    │
│  tabs, config)   │             │                   └─────────────────────┘
└──────────────────┘             │
                                 ▼
                          SSE subscribers
```

---

## Settings UI Integration

### Settings Panel (Ctrl+,)

Add an "API & Webhooks" section to the settings editor:

| Setting | Control | Notes |
|---------|---------|-------|
| Enable API | Toggle | Starts/stops the HTTP server |
| Port | Number input | Default: 19280, range 1024-65535 |
| Bind Address | Dropdown | `127.0.0.1` (Local only) / `0.0.0.0` (All interfaces) |
| API Key | Password input | Shown only when bind is non-loopback |
| Enable SSE | Toggle | Default: on |
| CORS Origins | Text input | Comma-separated origins |
| Enable Webhooks | Toggle | Default: off |
| Webhooks | List editor | Add/edit/remove/test webhook endpoints |

### Webhook Editor (inline or dialog)

| Field | Control | Notes |
|-------|---------|-------|
| Name | Text input | Display name for the webhook |
| URL | Text input | HTTP(S) endpoint URL |
| Secret | Password input | Optional HMAC signing key |
| Events | Multi-select | Checkboxes for event categories |
| Debounce | Slider | 0-10000ms, default 500ms |
| Batch Window | Slider | 0-30000ms, default 0 (disabled) |
| Max Retries | Number | 0-10, default 3 |
| Template | File picker | Optional Scriban template file |
| Test | Button | Sends a test event and shows result |

---

## Command Palette Commands

| Command | Shortcut | Action |
|---------|----------|--------|
| API: Start Server | — | Start the REST API server |
| API: Stop Server | — | Stop the REST API server |
| API: Copy Base URL | — | Copy `http://127.0.0.1:19280` to clipboard |
| API: Open in Browser | — | Open `/api/status` in default browser |
| API: Test Webhooks | — | Send a test event to all enabled webhooks |
| API: Show Delivery Stats | — | Show webhook delivery statistics as a toast |

---

## Implementation Priority

### Phase 1: Core REST API

**Scope:** HTTP server, settings model, authentication, core endpoints.

| Item | Details |
|------|---------|
| `ApiSettings` domain model | Settings class with JSON serialization |
| `IApiServer` interface + `ApiServer` implementation | `HttpListener`, route table, auth middleware |
| Endpoints: `/api/status`, `/api/repos`, `/api/repos/{id}`, `/api/repos/{id}/git`, `/api/repos/{id}/files`, `/api/repos/{id}/links`, `/api/config`, `/api/tasks`, `/api/timeline` | Read-only JSON responses |
| Settings UI section | Enable/port/bind/key controls |
| Command palette commands | Start/stop/copy URL |
| Auth middleware | API key validation for non-loopback |
| CORS middleware | Origin checking |

### Phase 2: SSE Streaming + Event Aggregation

**Scope:** Real-time event streaming, event bus.

| Item | Details |
|------|---------|
| `IEventAggregatorService` + implementation | Publish/subscribe, event buffer |
| `EventWiringService` | Subscribe to git/timeline/tab/config events |
| SSE endpoint `/api/events` | Streaming, filtering, heartbeat, reconnection |
| Event types for repo, session, task, app | All events in catalog |

### Phase 3: Webhooks

**Scope:** Outbound HTTP delivery with reliability.

| Item | Details |
|------|---------|
| `IWebhookDeliveryService` + implementation | Debounce, batch, retry, signing |
| `WebhookEndpoint` domain model | Per-endpoint config |
| Webhook settings UI | List editor with add/edit/remove/test |
| Delivery stats | In-memory tracking, toast/palette display |

### Phase 4: Scriban Templates

**Scope:** Custom payload formatting.

| Item | Details |
|------|---------|
| Scriban NuGet dependency | `Scriban` package in Core |
| Template loading and caching | File watch for hot reload |
| Template context mapping | Event → Scriban ScriptObject |
| Template validation | Parse on save, toast on error |
| Example templates | Slack, Discord, generic HTTP |

### Phase 5: Future

**Scope:** Write endpoints and MCP integration.

| Item | Details |
|------|---------|
| POST endpoints | `/api/repos/{id}/terminal/input`, `/api/repos/{id}/git/stage`, `/api/repos/{id}/git/commit` |
| MCP server mode | Expose tools via Model Context Protocol for AI agents |
| WebSocket upgrade | Bidirectional communication as alternative to SSE |

---

## Technical Considerations

### Performance

- **Endpoint handlers** read ViewModel state synchronously on the UI thread via `IDispatcherService.InvokeAsync()`. Keep handlers fast (< 50ms).
- **File tree endpoint** (`/api/repos/{id}/files`) may be slow for large repos. Use `depth` parameter to limit traversal. Cache results with file watcher invalidation.
- **Event debouncing** is critical for `repo.git_status_changed` and `repo.terminal_activity`, which can fire many times per second.
- **SSE connections** hold open HTTP connections. Enforce max 10 concurrent connections; reject with 429 when exceeded.

### Stability

- API server failures must not crash the main application. All server code wrapped in try/catch with logging.
- `HttpListener` requires no elevated permissions on Windows for `http://127.0.0.1:*` URLs (localhost exemption).
- On macOS, `HttpListener` uses Mono's managed implementation in .NET 8 — test thoroughly.
- Server start/stop is idempotent. Multiple calls to `StartAsync()` are no-ops when already running.

### Testability

- All new services use interface abstractions registered via DI.
- `ApiServer` accepts `IEventAggregatorService` and a state-reading delegate (not a direct ViewModel reference) to remain testable.
- Unit tests can verify endpoint handlers independently by constructing request/response mocks.
- Integration tests can start `ApiServer` on a random port and make real HTTP requests.

### Cross-Platform

- `HttpListener` is available on both Windows and macOS in .NET 8.
- All domain models and service interfaces go in `TerminalHost.Core`.
- Platform-specific behavior (if any) can be injected via the existing `TerminalHost.Windows` / `TerminalHost.macOS` split.
- Port conflict handling: if configured port is in use, log a warning, show a toast, and remain stopped.

### Security

- Localhost binding means the API is only accessible from the same machine by default.
- API key is stored in config JSON — not encrypted, but config file has user-only permissions.
- Webhook secrets are stored in config JSON similarly.
- No sensitive data (passwords, tokens, file contents) is exposed through read-only endpoints.
- `/api/config` redacts API keys, webhook secrets, and paths containing the username.
