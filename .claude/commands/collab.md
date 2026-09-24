---
description: "Context-prime Parley — pub/sub topics and messaging for multi-session Claude Code coordination"
---

# Parley (inter-session messaging)

You may have access to the **parley** MCP server — pub/sub topics that let several Claude Code sessions (different repos, terminals or worktrees) coordinate. Parley is a standalone tool (`dotnet tool install -g HC.Parley`, command `parley`); TerminalHost registers it for the sessions it launches. Source and docs: P:\Parley / https://github.com/stevehansen/parley.

## Quick Start

Your session is already named: TerminalHost sets `PARLEY_SESSION` to the tab name (otherwise the shim uses the working directory's folder name). No `set_session_name` call is needed.

**Send** (topics auto-create, subscription is automatic):

```
send_message(topic: "backend-api", content: "I'm refactoring the auth middleware")
```

**Read** (auto-subscribes you):

```
read_messages(topic: "backend-api", since_id: 0)  # 0 = all messages
```

## Push delivery

When the session was launched with `--dangerously-load-development-channels server:parley` (TerminalHost: Settings > API & Webhooks > Parley > "Push messages into Claude Code sessions"), new messages on your topics arrive as a new turn, even while idle:

```
<channel source="parley" topic="api-contract" sender="backend" message_id="42">UserDTO gained an email field</channel>
```

React to these like a message from a colleague. Without channels everything still works pull-based: tool results list unread messages, and `read_messages` can long-poll with `timeout`.

## Tool Reference

| Tool | Purpose | Required Params | Optional Params |
|------|---------|-----------------|-----------------|
| `send_message` | Send; creates and joins the topic as needed | `topic`, `content` | — |
| `read_messages` | History after a cursor; `timeout` waits for the next message | `topic` | `since_id` (0=all), `timeout` (ms, max 300000) |
| `subscribe` | Join a topic (receive its pushes); set its description | `topic` | `description` |
| `unsubscribe` | Leave; the last one out deletes the topic | `topic` | — |
| `list_topics` | Topics, subscribers, message counts, connected sessions | — | — |
| `set_session_name` | HTTP clients only (e.g. Codex via the hub URL), when the hub had to make a name up | `name` | `working_dir` |

## Common Workflows

### Parallel feature development

```
# Session "backend":
send_message(topic: "api-contract", content: "UserDTO: { id: string, name: string, email: string }")

# Session "frontend" (receives a push if channels are on, else):
read_messages(topic: "api-contract", since_id: 0)
```

### Waiting for an answer without channels

```
send_message(topic: "questions", content: "What's the DB schema for users table?")
read_messages(topic: "questions", since_id: 5, timeout: 30000)  # wait up to 30s
```

## Key Behaviors

- **Auto-everything**: `send_message` and `read_messages` create topics and subscribe you.
- **Persistent**: the hub keeps topics, messages and read cursors across restarts (500 messages per topic, 5000 total; topics idle for 24h are dropped on hub start).
- **One hub per machine**: `parley serve` on 127.0.0.1:19480, started automatically by the first shim.
- **Observability in TerminalHost**: Claude Tasks panel (Ctrl+Shift+K) shows topics, subscribers (blue = connected) and recent messages; `/api/collab/topics` and `/api/collab/sessions` proxy the hub for Spark Canvas.

Now tell me what you'd like to coordinate across sessions.
