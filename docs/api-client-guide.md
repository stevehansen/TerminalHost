# TerminalHost API Client Guide

Integrate with TerminalHost's REST API to read application state, stream real-time events, and receive webhook notifications.

## Quick Start

1. Open TerminalHost → Settings (Ctrl+,) → API & Webhooks
2. Check **Enable REST API** → Save
3. The server starts on `http://127.0.0.1:19280`

```bash
curl http://127.0.0.1:19280/api/status
```

## Base URL

```
http://127.0.0.1:19280
```

Default port is `19280`. All endpoints are read-only (`GET` only).

## Authentication

**Localhost (default):** No authentication required.

**Network-exposed** (bind address `0.0.0.0`): Send the API key via header or query param:

```bash
# Header
curl -H "Authorization: Bearer YOUR_API_KEY" http://host:19280/api/status

# Query param
curl http://host:19280/api/status?key=YOUR_API_KEY
```

## Endpoints

### GET /api/status

Application status and uptime.

```json
{
  "version": "1.0.0",
  "uptime": "02:15:33",
  "uptimeSeconds": 8133,
  "tabCount": 3,
  "activeTabIndex": 1,
  "touchMode": false,
  "platform": "Windows",
  "apiVersion": "1"
}
```

### GET /api/repos

All open project tabs.

```json
{
  "repos": [
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
        "stashCount": 0,
        "changedFiles": 3,
        "stagedFiles": 1,
        "untrackedFiles": 0
      },
      "terminals": {
        "custom": { "title": "Claude Code", "isActive": true, "isBusy": true, "lastActivityAt": "2026-02-23T10:15:00Z" },
        "shell": { "title": "PowerShell", "isActive": false, "isBusy": false, "lastActivityAt": "2026-02-23T09:30:00Z" },
        "run": null
      },
      "activityIndicator": {
        "state": "busy",
        "hasUnreadActivity": false,
        "isWaitingForInput": false
      }
    }
  ]
}

**Terminal fields:**
- `isActive` — whether this terminal pane is the focused/selected pane
- `isBusy` — whether the terminal is actively generating output (output within last 2 seconds)
- `lastActivityAt` — UTC timestamp of the last terminal output, `null` if no output received yet
- `run` — `null` when no run terminal has been created for this tab

**Activity indicator states** (maps to the visual tab strip indicators):
- `"busy"` — a terminal is actively outputting (spinning orange indicator)
- `"waiting"` — custom terminal is idle and detected as waiting for user input (orange dot)
- `"done"` — activity has finished but the tab hasn't been viewed yet (green dot)
- `"idle"` — no notable activity state
```

### GET /api/repos/{index}

Single repo by tab index (0-based). Same shape as above, plus:

```json
{
  "runConfiguration": {
    "id": "dotnet-run",
    "label": "dotnet run",
    "isRunning": false
  },
  "aiAssistant": {
    "id": "claude",
    "name": "Claude Code",
    "icon": "🤖"
  }
}
```

Returns `404` if index is out of range.

### GET /api/repos/{index}/git

Full git status with file list and recent commits.

```json
{
  "branch": "master",
  "isDirty": true,
  "ahead": 2,
  "behind": 0,
  "stashCount": 1,
  "changedFiles": 5,
  "stagedFiles": 2,
  "untrackedFiles": 1,
  "files": [
    {
      "path": "src/Services/ApiServer.cs",
      "status": "Modified",
      "isStaged": true,
      "oldPath": null
    }
  ],
  "recentCommits": [
    {
      "hash": "ac3ae44",
      "message": "fix: Prevent center panel showing wrong repo data",
      "author": "Steve",
      "date": "2026-02-22T14:30:00Z"
    }
  ]
}
```

### GET /api/repos/{index}/links

Detected links from terminal output (URLs, file paths).

```json
{
  "links": [
    {
      "text": "https://github.com/user/repo/pull/42",
      "url": "https://github.com/user/repo/pull/42",
      "path": null,
      "line": null,
      "type": "Url",
      "source": "Custom"
    }
  ]
}
```

### GET /api/tasks

Active focus tasks.

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

AI development timeline (intents and sessions).

| Query Param | Default | Description |
|-------------|---------|-------------|
| `limit` | 50 | Max intents to return |

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

Application configuration (sensitive fields redacted).

```json
{
  "settings": {
    "customCommandName": "Claude Code",
    "shellCommandName": "PowerShell",
    "touchMode": false,
    "confirmOnClose": true
  },
  "quickCommands": [
    { "id": "commit", "label": "Commit", "icon": "💾", "shortcut": "Ctrl+Shift+C" }
  ],
  "aiAssistants": [
    { "id": "claude", "name": "Claude Code", "icon": "🤖" }
  ]
}
```

## Error Responses

All errors follow this shape:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Repo index 5 not found."
  }
}
```

| Status | Code | Meaning |
|--------|------|---------|
| 400 | `BAD_REQUEST` | Invalid parameters |
| 401 | `UNAUTHORIZED` | Missing/invalid API key |
| 404 | `NOT_FOUND` | Invalid repo index or unknown endpoint |
| 405 | `METHOD_NOT_ALLOWED` | Non-GET request |
| 429 | — | SSE connection limit reached (max 10) |
| 500 | `INTERNAL_ERROR` | Server error |

## SSE Event Streaming

### Connecting

```
GET /api/events
Accept: text/event-stream
```

| Query Param | Example | Description |
|-------------|---------|-------------|
| `events` | `repo.*,session.*` | Event type filter (supports `*` wildcards) |
| `repos` | `0,1` | Filter by repo tab indices |

### Stream Format

```
: connected to TerminalHost event stream

event: repo.git_status_changed
id: evt_a1b2c3d4
data: {"id":"evt_a1b2c3d4","type":"repo.git_status_changed","timestamp":"2026-02-23T10:15:00Z","repoIndex":0,"data":{"branch":"master","isDirty":true,"ahead":2,"behind":0}}

: heartbeat
```

- Heartbeat comment every 30 seconds
- Max 10 concurrent SSE connections
- Reconnect with `Last-Event-ID` header to resume (server buffers last 100 events)

### JavaScript Example

```javascript
const es = new EventSource('http://127.0.0.1:19280/api/events?events=repo.*');

es.addEventListener('repo.git_status_changed', (e) => {
  const event = JSON.parse(e.data);
  console.log(`Repo ${event.repoIndex}: branch=${event.data.branch}, dirty=${event.data.isDirty}`);
});

es.addEventListener('repo.branch_switched', (e) => {
  const event = JSON.parse(e.data);
  console.log(`Switched from ${event.data.previousBranch} to ${event.data.newBranch}`);
});

es.onerror = () => console.log('Connection lost, reconnecting...');
```

### Python Example

```python
import requests
import json

response = requests.get(
    'http://127.0.0.1:19280/api/events?events=repo.*',
    stream=True,
    headers={'Accept': 'text/event-stream'}
)

for line in response.iter_lines(decode_unicode=True):
    if line.startswith('data: '):
        event = json.loads(line[6:])
        print(f"[{event['type']}] repo={event.get('repoIndex')} data={event.get('data')}")
```

### curl Example

```bash
curl -N http://127.0.0.1:19280/api/events?events=repo.*
```

## Event Types

All events share this envelope:

```json
{
  "id": "evt_a1b2c3d4",
  "type": "repo.git_status_changed",
  "timestamp": "2026-02-23T10:15:00Z",
  "repoIndex": 0,
  "data": { }
}
```

`repoIndex` is `null` for non-repo events.

### Currently Emitted Events

| Event | Trigger | Data Fields |
|-------|---------|-------------|
| `repo.opened` | Tab opened | `workingDirectory`, `title` |
| `repo.closed` | Tab closed | `workingDirectory`, `title` |
| `repo.activated` | Tab focused | `workingDirectory`, `title`, `previousIndex` |
| `repo.git_status_changed` | Git poll refresh | `branch`, `isDirty`, `ahead`, `behind` |
| `repo.branch_switched` | Branch changed | `previousBranch`, `newBranch` |

### Additional Event Types (Spec)

These are defined in the spec but not yet wired to emit:

| Event | Trigger | Data Fields |
|-------|---------|-------------|
| `repo.commit` | Commit created | `hash`, `message`, `author`, `filesChanged` |
| `repo.terminal_activity` | Terminal output | `terminal`, `isActive` |
| `repo.layout_changed` | Layout changed | `layout`, `splitRatio`, `activeTerminal` |
| `repo.run_started` | Project run started | `configId`, `configLabel` |
| `repo.run_stopped` | Project run stopped | `configId`, `exitCode` |
| `session.started` | AI session started | `sessionId`, `intentId` |
| `session.ended` | AI session ended | `sessionId`, `status`, `commitHash` |
| `task.created` | Task created | `taskId`, `title` |
| `task.updated` | Task updated | `taskId`, `status`, `previousStatus` |
| `app.settings_changed` | Config saved | `changedKeys` |

## Webhook Payloads

Webhooks are configured in TerminalHost settings. When an event matches a webhook's filter, TerminalHost sends a `POST` to the webhook URL.

### Single Event

```
POST https://your-endpoint.example.com/webhook
Content-Type: application/json
X-TerminalHost-Event: repo.git_status_changed
X-TerminalHost-Delivery: del_abc123
X-TerminalHost-Timestamp: 1708700000
X-TerminalHost-Signature: sha256=a1b2c3...

{
  "id": "evt_a1b2c3d4",
  "type": "repo.git_status_changed",
  "timestamp": "2026-02-23T10:15:00Z",
  "repoIndex": 0,
  "data": {
    "branch": "master",
    "isDirty": true,
    "ahead": 2,
    "behind": 0
  }
}
```

### Batch Delivery

When `batchWindowMs > 0` on the webhook:

```json
{
  "batch": true,
  "events": [
    { "id": "evt_001", "type": "repo.git_status_changed", "timestamp": "...", "repoIndex": 0, "data": {} },
    { "id": "evt_002", "type": "repo.branch_switched", "timestamp": "...", "repoIndex": 0, "data": {} }
  ]
}
```

### Verifying Signatures

If a webhook has a `secret` configured, verify the `X-TerminalHost-Signature` header:

```python
import hmac
import hashlib

def verify_signature(body: bytes, secret: str, signature_header: str) -> bool:
    expected = 'sha256=' + hmac.new(
        secret.encode(), body, hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, signature_header)
```

```javascript
const crypto = require('crypto');

function verifySignature(body, secret, signatureHeader) {
  const expected = 'sha256=' + crypto
    .createHmac('sha256', secret)
    .update(body)
    .digest('hex');
  return crypto.timingSafeEqual(
    Buffer.from(expected),
    Buffer.from(signatureHeader)
  );
}
```

### Webhook Headers

| Header | Description |
|--------|-------------|
| `X-TerminalHost-Event` | Event type (e.g., `repo.git_status_changed`) |
| `X-TerminalHost-Delivery` | Unique delivery ID |
| `X-TerminalHost-Timestamp` | Unix timestamp of delivery |
| `X-TerminalHost-Signature` | HMAC-SHA256 signature (only if `secret` is set) |

### Retry Behavior

| Attempt | Delay |
|---------|-------|
| 1 | Immediate |
| 2 | 5 seconds |
| 3 | 30 seconds |
| 4 | 5 minutes |

- 5xx and network errors are retried
- 4xx errors are **not** retried
- Configurable per-webhook: `debounceMs` (default 500), `batchWindowMs` (default 0), `maxRetries` (default 3)

## CORS

Default allowed origin: `http://localhost:*`. Configure additional origins in Settings → API & Webhooks → CORS Origins (comma-separated). Supports `*` wildcards.

## Polling Example

For clients that don't need real-time updates:

```bash
# Check if any repo has uncommitted changes
watch -n 5 'curl -s http://127.0.0.1:19280/api/repos | jq ".repos[] | {title, dirty: .git.isDirty}"'
```

```python
import requests, time

while True:
    repos = requests.get('http://127.0.0.1:19280/api/repos').json()['repos']
    for repo in repos:
        if repo['git'] and repo['git']['isDirty']:
            print(f"⚠ {repo['title']} has uncommitted changes on {repo['git']['branch']}")
    time.sleep(10)
```
