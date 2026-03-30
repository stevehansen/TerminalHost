---
description: "Context-prime the TerminalHost Collaboration MCP — topics and messaging for multi-session Claude Code coordination"
---

# TerminalHost Collaboration MCP

You have access to the **terminalhost-collab** MCP server — an in-memory collaboration system hosted by TerminalHost that lets multiple Claude Code sessions coordinate in real time via pub/sub topics.

## Quick Start

**Step 0: Identify yourself** (do this first, every session):

```
set_session_name(name: "MyProject", working_dir: "/path/to/project")
```

Use your project/folder name. This is how other sessions see you.

**Step 1: Just start sending messages** (topics auto-create, subscription is automatic):

```
send_message(topic: "backend-api", content: "I'm refactoring the auth middleware")
```

**Step 2: Read messages from any topic** (auto-subscribes you):

```
read_messages(topic: "backend-api", since_id: 0)  # 0 = all messages
```

No need to create topics or subscribe first — everything is automatic.

## Tool Reference

### Identity

| Tool | Purpose | Required Params | Optional Params |
|------|---------|-----------------|-----------------|
| `set_session_name` | Name your session (call first!) | `name` | `working_dir`, `project_name` |

### Topics (pub/sub channels)

| Tool | Purpose | Required Params | Optional Params |
|------|---------|-----------------|-----------------|
| `subscribe` | Join a topic (creates if needed), set description | `topic` | `description` |
| `unsubscribe` | Leave a topic (auto-deletes if last subscriber) | `topic` | — |
| `list_topics` | Show all topics, subscribers, message counts | — | — |

### Messaging

| Tool | Purpose | Required Params | Optional Params |
|------|---------|-----------------|-----------------|
| `send_message` | Send a message (auto-creates topic, auto-subscribes) | `topic`, `content` | — |
| `read_messages` | Read messages (auto-creates topic, auto-subscribes) | `topic` | `since_id` (0=all), `timeout` (ms, 0=immediate, max 300000) |

**Cursor system:** Each session tracks a per-topic read cursor. `read_messages` returns messages after `since_id` and advances your cursor. Use the returned cursor ID as `since_id` on the next call to get only new messages.

**Long-polling:** Set `timeout` > 0 to block until new messages arrive (or timeout). Useful for waiting on responses without busy-polling.

## Common Workflows

### Parallel Feature Development

Two sessions working on related features:

```
# Session A (backend):
set_session_name(name: "backend")
send_message(topic: "api-contract", content: "UserDTO: { id: string, name: string, email: string }")

# Session B (frontend):
set_session_name(name: "frontend")
read_messages(topic: "api-contract", since_id: 0)  # Gets all messages, auto-subscribes
```

### Handoff Between Sessions

```
# Session A finishes a subtask:
send_message(topic: "work", content: "Auth middleware refactored. New interface: IAuthProvider in src/Core/Interfaces/. Ready for integration.")

# Session B picks it up:
read_messages(topic: "work", since_id: 0)
```

### Long-Polling for Responses

```
# Ask a question and wait for answer:
send_message(topic: "questions", content: "What's the DB schema for users table?")

# Other session answers, then you read with timeout:
read_messages(topic: "questions", since_id: 5, timeout: 30000)  # Wait up to 30s
```

## Key Behaviors

- **Auto-everything**: `send_message` and `read_messages` auto-create topics and auto-subscribe you. No setup needed.
- **Topic descriptions**: Use `subscribe(topic, description)` to set or update a topic's description.
- **Auto-cleanup**: When the last subscriber leaves a topic (`unsubscribe`), the topic and its messages are deleted.
- **Unread hints**: Every tool response appends unread message counts (e.g., `[You have 2 unread message(s) on topic 'backend']`). Check these.
- **In-memory only**: All state resets when TerminalHost restarts. No persistence.
- **Thread-safe**: All operations are locked — safe for concurrent access.

## Architecture (for contributors)

```
Source files:
  src/TerminalHost.Core/Domain/CollabModels.cs      — Data models (CollabSession, CollabTopic, CollabMessage)
  src/TerminalHost.Core/Interfaces/ICollabService.cs — Service contract
  src/TerminalHost.Core/Services/CollabService.cs    — In-memory implementation (lock-based thread safety)
  src/TerminalHost.Core/Services/McpHandler.cs       — MCP JSON-RPC handler (tool routing, session resolution)
  src/TerminalHost.Core/Services/ApiServer.cs        — HTTP server (POST /api/mcp, GET /api/collab/topics, GET /api/collab/sessions)

Data flow:
  Claude Code MCP client → HTTP POST /api/mcp → McpHandler → CollabService → response
  REST observability:     → GET /api/collab/topics or /sessions → JSON

Session resolution:
  1. On MCP initialize: assign session ID, derive name from X-Session header / MCP roots / workspace folders
  2. On subsequent calls: Mcp-Session-Id header required
  3. set_session_name: override display name and enrich metadata
```

Now tell me what you'd like to coordinate across sessions, or ask about any part of the collaboration system.
