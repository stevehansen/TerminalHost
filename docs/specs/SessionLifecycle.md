# Session Lifecycle & Activity Tracking

> Rich data model for AI agent execution — tool calls, subagents, context usage, and session lifecycle. Visualization via Spark Canvas.

## Status: Phase 0-2 Implemented (2026-03-25)

---

## Part 1: Gap Analysis — Why Timeline Mode Doesn't Work

### The Symptom

Sessions stay "Running" indefinitely, even after Claude Code has stopped. The Timeline view fills up with ghost sessions showing ever-increasing durations. Users stop trusting the feature and stop using it.

### Root Cause Analysis

**1. Single point of failure: the Stop hook**

The entire session lifecycle depends on Claude Code firing a `Stop` hook AND that hook successfully reaching TerminalHost via named pipe. If either fails, the session is stuck forever:

```
Claude Code stops
  → Stop hook fires host.exe --hook session-stop  (5s timeout — can fail)
    → Reads stdin JSON                             (2s timeout — can fail)
      → Sends via named pipe to main instance      (can fail if not running)
        → HandleSessionStopAsync fires             (fire-and-forget, can silently fail)
          → GatherSessionGitDataAsync              (always sets status = Success)
```

Any break in this chain = permanent Running state.

**2. No fallback detection**

| What agent-flow does | What we do |
|---|---|
| JSONL file watching — if no new content for 10s, session marked ended | Nothing |
| Polling scan every 30s for stale sessions | Nothing |
| Dual source (hooks + file) with deduplication | Single source (hooks only) |
| Permission detection — detects "waiting for user" state | Nothing |

**3. Status always Success**

`GatherSessionGitDataAsync` (TimelineService.cs ~line 1664) unconditionally sets `session.Status = Success`, even in the catch block. There's no way to distinguish a productive session from one that errored out.

**4. Extremely limited hook coverage**

Current hooks:
```json
{
  "SessionStart": "host.exe --hook session-start",
  "Stop": "host.exe --hook session-stop",
  "PostToolUse": { "matcher": "Write|Edit|MultiEdit", "command": "host.exe --hook file-changed" }
}
```

This gives us: session boundaries + file modifications. That's it. We're blind to:
- Tool execution timing (when did a Read/Bash/Grep start and end?)
- Subagent lifecycle (spawned via Agent/Task tools)
- What the agent is actually doing right now (thinking? waiting for permission?)
- Non-file tools (Bash commands, searches, web fetches)

**5. Shallow JSONL parsing**

`TranscriptParserService` extracts only:
- Bash commands (from `tool_name: "Bash"` entries)
- A heuristic "summary" from the last assistant message
- Message count and tool call count (as totals, not individual records)

It doesn't extract: individual tool calls with timing, tool arguments, tool results, thinking blocks, model info, content structure, or subagent relationships.

### What Needs to Change (Before Any Visualization)

| Problem | Fix |
|---------|-----|
| Sessions stuck Running | Add inactivity timeout + JSONL file watching as fallback |
| Status always Success | Determine status from session content (errors, no output, etc.) |
| Fire-and-forget stop | Await the async, add error handling |
| No tool-level data | Expand hooks to PreToolUse + all PostToolUse; enhance JSONL parser |
| No subagent awareness | Detect Agent/Task tool calls from hooks or JSONL |
| No "what is it doing now" | Track agent state machine: idle → thinking → tool_calling → waiting_permission |

---

## Part 2: Data Sources Available

### Source 1: Claude Code Hooks (Real-time push)

Available hook events from Claude Code:

| Hook Event | Fires When | Payload Contains |
|---|---|---|
| `SessionStart` | Session begins | session_id, cwd, transcript_path, permission_mode |
| `Stop` | Session ends | session_id, cwd, transcript_path |
| `PreToolUse` | Before tool executes | session_id, tool_name, tool_input, tool_use_id |
| `PostToolUse` | After tool completes | session_id, tool_name, tool_input, tool_response, tool_use_id |

**We currently capture**: SessionStart, Stop, PostToolUse (Write|Edit|MultiEdit only).
**We should capture**: All of the above, plus all tools in PostToolUse (not just file-modification tools).

Hooks give us tool_use_id which allows pairing PreToolUse → PostToolUse for duration measurement.

### Source 2: JSONL Transcript Files (File-based pull)

Claude Code writes to `~/.claude/projects/<encoded-path>/<session-uuid>.jsonl`. Each line is a JSON object:

```jsonl
{"type":"user","time":"2026-03-24T10:00:00Z","entry":{"uuid":"msg-1","message":{"role":"user","content":"Fix the login bug"}}}
{"type":"assistant","time":"2026-03-24T10:00:05Z","entry":{"uuid":"msg-2","message":{"role":"assistant","model":"claude-opus-4-6","content":[{"type":"thinking","thinking":"Let me analyze..."},{"type":"text","text":"I'll look at the login module."},{"type":"tool_use","id":"toolu_1","name":"Read","input":{"file_path":"src/login.ts"}}]}}}
{"type":"user","time":"2026-03-24T10:00:08Z","entry":{"uuid":"msg-3","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"[file contents]"}]}}}
```

**Content block types within messages:**
- `text` — assistant's visible response
- `thinking` — extended thinking (reasoning)
- `tool_use` — tool invocation with `id`, `name`, `input`
- `tool_result` — tool output linked by `tool_use_id`

**Key fields available:**
- `type` — "user", "assistant", or "progress"
- `time` — ISO8601 timestamp (gives us real timing for replay)
- `entry.uuid` — deduplication key
- `entry.message.model` — which model is running (opus, sonnet, haiku)
- `entry.message.content` — array of content blocks

**Subagent detection**: When `tool_use.name` is "Agent" or "Task", the input contains the subagent's task description. The corresponding `tool_result` marks subagent completion.

### Source 3: sessions-index.json (Claude Code metadata)

Located at `~/.claude/projects/<encoded-path>/sessions-index.json`:

```json
{
  "version": 1,
  "entries": [{
    "sessionId": "uuid",
    "fullPath": "/path/to/session.jsonl",
    "firstPrompt": "Fix the login bug",
    "summary": "Fixed authentication timeout...",
    "messageCount": 42,
    "created": "2026-03-24T10:00:00Z",
    "modified": "2026-03-24T10:45:00Z",
    "gitBranch": "fix/login"
  }]
}
```

We already read this via `ClaudeSessionIndexService`. It gives us session summaries, message counts, and first prompts — useful metadata but not granular activity data.

### Source Priority

| Need | Best Source | Fallback |
|---|---|---|
| Session start/stop | Hook (instant) | JSONL file mtime (10s delay) |
| Tool call start | Hook PreToolUse | JSONL tool_use block |
| Tool call end | Hook PostToolUse | JSONL tool_result block |
| Tool timing | Hook pair (Pre→Post) | JSONL timestamps |
| Subagent lifecycle | Hook PreToolUse(Agent) | JSONL tool_use(Agent) |
| Agent state (thinking/idle) | JSONL content blocks | Inferred from hook gaps |
| Context/token usage | JSONL content size | Estimated from hooks |
| Model identification | JSONL assistant.model | Hook payload (if available) |
| Session summary | sessions-index.json | JSONL last assistant message |

**Design principle**: Hooks for speed, JSONL for completeness, sessions-index for metadata. Dedup across sources.

---

## Part 3: Proposed Data Model

The model has four layers, each building on the previous. Layer 1 fixes what's broken today. Layers 2-4 add the richer data needed for visualization.

### Layer 1: Session Lifecycle (fix the fundamentals)

```csharp
/// <summary>
/// Richer session state that replaces the binary Running/Success/Failed model.
/// Derived from hook events + JSONL activity + inactivity detection.
/// </summary>
public enum SessionActivityState
{
    /// <summary>Session started, agent is working.</summary>
    Active,

    /// <summary>Agent is executing a tool (we know which one).</summary>
    ToolCalling,

    /// <summary>Agent is waiting for user permission (tool blocked).</summary>
    WaitingPermission,

    /// <summary>No activity for > N seconds but session not explicitly ended.
    /// Key difference from current: this is DETECTED, not just inferred.</summary>
    Idle,

    /// <summary>Session ended normally (Stop hook or JSONL complete).</summary>
    Completed,

    /// <summary>Session ended with an error (detected from JSONL/tool failures).</summary>
    Failed,

    /// <summary>Session presumed ended — no activity for extended period,
    /// no Stop hook received. EndTime set to LastActivityTime.</summary>
    TimedOut,

    /// <summary>User manually marked as abandoned.</summary>
    Abandoned
}
```

**Inactivity timeout logic** (new — the core fix):

```
On each hook event or JSONL line:
  → Update LastActivityTime

Every 30 seconds (timer):
  For each session where State == Active/ToolCalling/WaitingPermission/Idle:
    If (now - LastActivityTime) > InactivityTimeout (default: 2 minutes):
      → Check JSONL file mtime as confirmation
      → If JSONL also stale: mark as TimedOut, set EndTime = LastActivityTime
    Elif (now - LastActivityTime) > IdleThreshold (default: 30 seconds):
      → Transition to Idle state
```

**JSONL file watcher** (new — backup detection):

```
For each active session with a known transcript_path:
  → FileSystemWatcher on the .jsonl file
  → On change: parse new lines, emit events, update LastActivityTime
  → On no change for InactivityTimeout: confirm session ended
```

### Layer 2: Tool Activity (what's happening inside a session)

```csharp
/// <summary>
/// A single tool invocation within a session.
/// Populated from PreToolUse+PostToolUse hook pair, or from JSONL tool_use+tool_result pair.
/// </summary>
public class ToolCall
{
    /// <summary>Claude Code's tool_use_id (e.g., "toolu_abc123").</summary>
    public string ToolUseId { get; set; } = "";

    /// <summary>Which session this belongs to.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Which agent invoked this tool (main or subagent ID).</summary>
    public string AgentId { get; set; } = "";

    /// <summary>Tool name: Read, Edit, Write, Bash, Grep, Glob, Agent, Task, etc.</summary>
    public string ToolName { get; set; } = "";

    /// <summary>Running, Complete, Error.</summary>
    public ToolCallState State { get; set; } = ToolCallState.Running;

    /// <summary>Human-readable summary of input (e.g., "src/login.ts" for Read).</summary>
    public string InputSummary { get; set; } = "";

    /// <summary>Human-readable summary of result.</summary>
    public string? ResultSummary { get; set; }

    /// <summary>Structured input data (file_path, command, pattern, etc.).</summary>
    public Dictionary<string, object>? InputData { get; set; }

    /// <summary>Estimated token cost of the tool result content.</summary>
    public int? TokenCost { get; set; }

    /// <summary>When the tool started executing.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the tool finished (null if still running).</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Whether the tool produced an error.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Duration of the tool call.</summary>
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

    /// <summary>Tool category for grouping/coloring.</summary>
    public ToolCategory Category => ToolName switch
    {
        "Read" or "Glob" or "Grep" => ToolCategory.FileRead,
        "Write" or "Edit" or "MultiEdit" => ToolCategory.FileWrite,
        "Bash" => ToolCategory.Shell,
        "Agent" or "Task" => ToolCategory.Subagent,
        "WebSearch" or "WebFetch" => ToolCategory.Web,
        _ => ToolCategory.Other
    };
}

public enum ToolCallState { Running, Complete, Error }

public enum ToolCategory { FileRead, FileWrite, Shell, Subagent, Web, Other }
```

**Input summarization** (tool-specific):

| Tool | Summary from input |
|---|---|
| Read | file_path (+ line range if present) |
| Edit | file_path + "old_string → new_string" (truncated) |
| Write | file_path |
| Bash | command (truncated to 80 chars) |
| Grep | pattern + path |
| Glob | pattern |
| Agent | description or prompt (first 80 chars) |
| WebSearch | query |
| WebFetch | url (domain only) |

### Layer 3: Agent Hierarchy (subagents)

```csharp
/// <summary>
/// Represents an agent instance within a session.
/// The main orchestrator is always present. Subagents are spawned via Agent/Task tool calls.
/// </summary>
public class AgentInstance
{
    /// <summary>Unique ID. Main agent = session ID. Subagents = tool_use_id of Agent/Task call.</summary>
    public string Id { get; set; } = "";

    /// <summary>Which session this agent belongs to.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Display name (e.g., "main", "subagent-1", or from Agent tool's description param).</summary>
    public string Name { get; set; } = "";

    /// <summary>Parent agent ID (null for main orchestrator).</summary>
    public string? ParentId { get; set; }

    /// <summary>Whether this is the main/root agent.</summary>
    public bool IsMain { get; set; }

    /// <summary>Current state of this agent.</summary>
    public AgentState State { get; set; } = AgentState.Active;

    /// <summary>Task description (from Agent/Task tool input, or initial user prompt for main).</summary>
    public string? Task { get; set; }

    /// <summary>Model being used (claude-opus-4-6, claude-sonnet-4-6, etc.).</summary>
    public string? Model { get; set; }

    /// <summary>When this agent was spawned.</summary>
    public DateTime SpawnTime { get; set; }

    /// <summary>When this agent completed (null if still active).</summary>
    public DateTime? CompleteTime { get; set; }

    /// <summary>Cumulative estimated tokens consumed by this agent.</summary>
    public int TokensUsed { get; set; }

    /// <summary>Max context window for this agent's model.</summary>
    public int TokensMax { get; set; }

    /// <summary>Breakdown of context usage by category.</summary>
    public ContextBreakdown Context { get; set; } = new();

    /// <summary>Number of tool calls made by this agent.</summary>
    public int ToolCallCount { get; set; }

    /// <summary>Tool currently being executed (null if none).</summary>
    public string? CurrentToolUseId { get; set; }

    /// <summary>IDs of child agents spawned by this agent.</summary>
    public List<string> ChildAgentIds { get; set; } = [];

    /// <summary>Time alive.</summary>
    public TimeSpan TimeAlive => (CompleteTime ?? DateTime.UtcNow) - SpawnTime;
}

public enum AgentState
{
    Active,              // Working normally
    Thinking,            // Extended thinking block detected
    ToolCalling,         // Executing a tool
    WaitingPermission,   // Blocked on user permission
    Idle,                // No activity detected
    Complete,            // Finished successfully
    Error                // Finished with error
}

/// <summary>
/// Breakdown of an agent's context window usage.
/// Estimated from content sizes in JSONL (chars / 4 ≈ tokens).
/// </summary>
public class ContextBreakdown
{
    public int SystemPrompt { get; set; }
    public int UserMessages { get; set; }
    public int ToolResults { get; set; }
    public int Reasoning { get; set; }      // thinking blocks
    public int SubagentResults { get; set; }

    public int Total => SystemPrompt + UserMessages + ToolResults + Reasoning + SubagentResults;
}

/// <summary>
/// Known model context sizes for percentage calculations.
/// </summary>
public static class ModelContextSizes
{
    public static int GetMaxTokens(string? model) => model switch
    {
        string m when m.Contains("opus") => 1_000_000,
        string m when m.Contains("sonnet") => 1_000_000,
        string m when m.Contains("haiku") => 200_000,
        _ => 200_000  // conservative default
    };
}
```

### Layer 4: Conversation Messages

```csharp
/// <summary>
/// A message in the session conversation.
/// Used for transcript display and context tracking.
/// </summary>
public class ConversationMessage
{
    /// <summary>UUID from the JSONL entry (deduplication key).</summary>
    public string? Uuid { get; set; }

    /// <summary>Which session.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Which agent produced/received this message.</summary>
    public string AgentId { get; set; } = "";

    /// <summary>Message type.</summary>
    public MessageType Type { get; set; }

    /// <summary>Role: user, assistant, system.</summary>
    public string Role { get; set; } = "";

    /// <summary>Text content (may be truncated for large tool results).</summary>
    public string Content { get; set; } = "";

    /// <summary>For tool_call messages: the tool_use_id.</summary>
    public string? ToolUseId { get; set; }

    /// <summary>For tool_call messages: the tool name.</summary>
    public string? ToolName { get; set; }

    /// <summary>Timestamp from JSONL.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Estimated token count of this message.</summary>
    public int EstimatedTokens { get; set; }
}

public enum MessageType
{
    UserMessage,        // User input
    AssistantText,      // Agent's visible text response
    Thinking,           // Extended thinking block
    ToolCall,           // Tool invocation
    ToolResult,         // Tool output
    SystemMessage       // System/context injection
}
```

### Aggregate: Session Activity State (the complete picture)

```csharp
/// <summary>
/// Complete activity state for a single session.
/// This is the in-memory model that drives both the Timeline view and
/// any future visualization. Built from hooks + JSONL + sessions-index.
/// </summary>
public class SessionActivityState
{
    /// <summary>Claude Code session ID.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Working directory.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>Path to JSONL transcript file.</summary>
    public string? TranscriptPath { get; set; }

    /// <summary>When the session started.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the session ended.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Last activity from any source (hook, JSONL, etc.).</summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public SessionLifecycle Lifecycle { get; set; } = SessionLifecycle.Active;

    /// <summary>Initial user prompt.</summary>
    public string? InitialPrompt { get; set; }

    /// <summary>Session summary (from sessions-index or JSONL).</summary>
    public string? Summary { get; set; }

    /// <summary>Git branch.</summary>
    public string? GitBranch { get; set; }

    // --- Agent hierarchy ---

    /// <summary>All agents in this session (main + subagents).</summary>
    public Dictionary<string, AgentInstance> Agents { get; set; } = new();

    /// <summary>The main/root agent.</summary>
    public AgentInstance? MainAgent => Agents.Values.FirstOrDefault(a => a.IsMain);

    // --- Tool activity ---

    /// <summary>All tool calls (completed + in-progress).</summary>
    public Dictionary<string, ToolCall> ToolCalls { get; set; } = new();

    /// <summary>Currently running tool calls.</summary>
    public IEnumerable<ToolCall> ActiveToolCalls =>
        ToolCalls.Values.Where(t => t.State == ToolCallState.Running);

    // --- Conversation ---

    /// <summary>Conversation messages (for transcript display).</summary>
    public List<ConversationMessage> Messages { get; set; } = [];

    // --- File activity ---

    /// <summary>Files touched during this session, with access counts.</summary>
    public Dictionary<string, FileActivity> FileActivities { get; set; } = new();

    // --- Deduplication ---

    /// <summary>Seen message UUIDs (prevent re-processing from JSONL re-reads).</summary>
    public HashSet<string> SeenMessageIds { get; set; } = new();

    /// <summary>Seen tool_use_ids (prevent duplicate from hook + JSONL).</summary>
    public HashSet<string> SeenToolUseIds { get; set; } = new();

    // --- Derived stats ---

    public int TotalToolCalls => ToolCalls.Count;
    public int TotalAgents => Agents.Count;
    public int TotalTokensEstimated => Agents.Values.Sum(a => a.TokensUsed);
    public int FilesRead => FileActivities.Values.Count(f => f.ReadCount > 0);
    public int FilesWritten => FileActivities.Values.Count(f => f.WriteCount > 0);
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}

/// <summary>
/// Tracks how a file has been accessed during a session.
/// </summary>
public class FileActivity
{
    public string FilePath { get; set; } = "";
    public int ReadCount { get; set; }
    public int WriteCount { get; set; }
    public int GrepHitCount { get; set; }
    public DateTime FirstAccess { get; set; }
    public DateTime LastAccess { get; set; }
}
```

---

## Part 4: Event Protocol

All data flows through a unified event stream. Events come from hooks (real-time) or JSONL parsing (batch/fallback). The same event types drive both the in-memory model updates and any future visualization.

```csharp
/// <summary>
/// A single event in the session activity stream.
/// Normalized from either hook events or JSONL transcript parsing.
/// </summary>
public class ActivityEvent
{
    /// <summary>Seconds since session start (for ordering and timeline positioning).</summary>
    public double TimeOffset { get; set; }

    /// <summary>Absolute timestamp.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Event type.</summary>
    public ActivityEventType Type { get; set; }

    /// <summary>Which session.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>Which agent (main or subagent ID).</summary>
    public string? AgentId { get; set; }

    /// <summary>Event-specific data.</summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>Where this event came from (for debugging/dedup).</summary>
    public EventSource Source { get; set; }
}

public enum ActivityEventType
{
    // Session lifecycle
    SessionStart,        // Session began
    SessionEnd,          // Session ended (explicit Stop hook)
    SessionTimeout,      // Session ended (inactivity timeout)

    // Agent lifecycle
    AgentSpawn,          // Agent created (main or subagent)
    AgentComplete,       // Agent finished work
    AgentStateChange,    // Agent state transition (idle, thinking, etc.)
    ModelDetected,       // Identified which model agent is using

    // Tool activity
    ToolCallStart,       // Tool execution began (from PreToolUse or JSONL tool_use)
    ToolCallEnd,         // Tool execution completed (from PostToolUse or JSONL tool_result)

    // Messages
    UserMessage,         // User sent a message
    AssistantMessage,    // Agent responded with text
    ThinkingBlock,       // Extended thinking content

    // File activity
    FileAccessed,        // A file was read, written, or searched
}

public enum EventSource
{
    Hook,           // From Claude Code hook (real-time)
    Transcript,     // From JSONL file parsing (batch or file-watcher)
    SessionIndex,   // From sessions-index.json
    Inferred        // Derived (e.g., inactivity timeout)
}
```

### Event Data Payloads

| Event Type | Data Keys |
|---|---|
| `SessionStart` | `cwd`, `transcriptPath`, `permissionMode` |
| `SessionEnd` | `reason` ("explicit", "timeout", "error") |
| `AgentSpawn` | `agentId`, `name`, `parentId`, `isMain`, `task`, `model` |
| `AgentComplete` | `agentId` |
| `AgentStateChange` | `agentId`, `newState`, `previousState` |
| `ModelDetected` | `agentId`, `model` |
| `ToolCallStart` | `toolUseId`, `agentId`, `toolName`, `inputSummary`, `inputData` |
| `ToolCallEnd` | `toolUseId`, `agentId`, `toolName`, `resultSummary`, `tokenCost`, `error` |
| `UserMessage` | `content` (truncated), `estimatedTokens` |
| `AssistantMessage` | `agentId`, `content` (truncated), `estimatedTokens` |
| `ThinkingBlock` | `agentId`, `content` (truncated), `estimatedTokens` |
| `FileAccessed` | `filePath`, `accessType` ("read", "write", "search"), `toolUseId` |

---

## Part 5: Enhanced Hook Configuration

### Implemented hooks (9 total)

```json
{
  "hooks": {
    "SessionStart": [{ "hooks": [{ "type": "command", "command": "host.exe --hook session-start", "timeout": 10 }] }],
    "Stop": [{ "hooks": [{ "type": "command", "command": "host.exe --hook session-stop", "timeout": 10 }] }],
    "SessionEnd": [{ "hooks": [{ "type": "command", "command": "host.exe --hook session-end", "timeout": 10 }] }],
    "PreToolUse": [{ "hooks": [{ "type": "command", "command": "host.exe --hook tool-start", "timeout": 5 }] }],
    "PostToolUse": [{ "hooks": [{ "type": "command", "command": "host.exe --hook tool-end", "timeout": 5 }] }],
    "PostToolUseFailure": [{ "hooks": [{ "type": "command", "command": "host.exe --hook tool-error", "timeout": 5 }] }],
    "SubagentStart": [{ "hooks": [{ "type": "command", "command": "host.exe --hook subagent-start", "timeout": 5 }] }],
    "SubagentStop": [{ "hooks": [{ "type": "command", "command": "host.exe --hook subagent-stop", "timeout": 5 }] }],
    "Notification": [{ "hooks": [{ "type": "command", "command": "host.exe --hook notification", "timeout": 5 }] }]
  }
}
```

**All 9 Claude Code hook events are captured:**

| Hook | CLI arg | What it gives us |
|------|---------|------------------|
| `SessionStart` | `session-start` | Session lifecycle start |
| `Stop` | `session-stop` | Explicit session end |
| `SessionEnd` | `session-end` | Fallback session end |
| `PreToolUse` | `tool-start` | Tool start timing |
| `PostToolUse` | `tool-end` | Tool completion with result |
| `PostToolUseFailure` | `tool-error` | Tool errors (marks ToolCallState.Error) |
| `SubagentStart` | `subagent-start` | Real-time subagent spawn (agent_id, agent_type) |
| `SubagentStop` | `subagent-stop` | Real-time subagent completion |
| `Notification` | `notification` | Permission prompts → WaitingPermission state |

**Container support:** The `host.exe` bash proxy in containers forwards all hook types to `POST /api/hooks/:type` on the host API server, which dispatches through the same pipeline as CLI hooks.

### Event Translation

```
Hook: PreToolUse
  → If tool_name == "Agent" or "Task":
      Emit AgentSpawn (subagent)
  → Emit ToolCallStart
  → Update agent state to ToolCalling

Hook: PostToolUse
  → Emit ToolCallEnd
  → If tool_name == "Agent" or "Task":
      Emit AgentComplete (subagent)
  → If tool is file-related:
      Emit FileAccessed
  → Update agent state to Active
  → Update LastActivityTime

Hook: SessionStart
  → Emit SessionStart
  → Emit AgentSpawn (main agent)

Hook: Stop
  → Emit SessionEnd
  → Emit AgentComplete (main agent)
```

---

## Part 6: JSONL File Watcher (Fallback Detection)

### Purpose

Backup session lifecycle detection when hooks fail. Also the only way to get thinking blocks, message content, and model info.

### Design

```csharp
/// <summary>
/// Watches a session's JSONL transcript file for new content.
/// Emits ActivityEvents as new lines are appended.
/// Detects session completion via inactivity timeout.
/// </summary>
public interface ITranscriptWatcher
{
    /// <summary>Start watching a transcript file for a session.</summary>
    void Watch(string sessionId, string transcriptPath);

    /// <summary>Stop watching.</summary>
    void Unwatch(string sessionId);

    /// <summary>Event stream from transcript changes.</summary>
    event Action<ActivityEvent> OnEvent;

    /// <summary>Fired when inactivity timeout reached.</summary>
    event Action<string> OnSessionInactive;
}
```

**Implementation approach:**
- `FileSystemWatcher` on the .jsonl file for change notifications
- Track file position (byte offset) — only parse newly appended lines
- On each new batch of lines: parse → deduplicate against SeenMessageIds/SeenToolUseIds → emit events
- Timer per session: if no file change for InactivityTimeout (configurable, default 2 min), fire OnSessionInactive
- Open file with `FileShare.ReadWrite` (already done in current parser)

### Deduplication

Events from hooks and JSONL can overlap. Deduplicate by:
- `tool_use_id` — if a ToolCallStart was already emitted via hook, skip the JSONL-derived one
- `entry.uuid` — if a message was already processed, skip
- Hook events take priority (more timely), JSONL fills gaps

---

## Part 7: Integration with Existing Timeline

### Enriching ClaudeSession

The existing `ClaudeSession` model stays as the **persisted summary** of a session. The new `SessionActivityState` is the **live in-memory detail**. Relationship:

```
SessionActivityState (live, in-memory, per-active-session)
  ├── Built from hooks + JSONL watcher + sessions-index
  ├── Updated in real-time as events arrive
  ├── Drives visualization and "what's happening now" UI
  └── On session end → summarized into ClaudeSession for persistence

ClaudeSession (persisted, per-completed-session)
  ├── Enriched with data from SessionActivityState on completion
  ├── Gets accurate EndTime (from last activity, not just Stop hook)
  ├── Gets accurate Status (from activity analysis, not just "Success")
  ├── Gets tool call stats, file activity, agent count
  └── Stays in timeline/sessions/ directory as before
```

### New fields on ClaudeSession (persisted summary)

```csharp
// Add to existing ClaudeSession class:

/// <summary>Total number of tool calls in the session.</summary>
[JsonPropertyName("totalToolCalls")]
public int TotalToolCalls { get; set; }

/// <summary>Number of subagents spawned.</summary>
[JsonPropertyName("subagentCount")]
public int SubagentCount { get; set; }

/// <summary>Estimated total tokens consumed.</summary>
[JsonPropertyName("estimatedTokens")]
public int EstimatedTokens { get; set; }

/// <summary>Model used (e.g., "claude-opus-4-6").</summary>
[JsonPropertyName("model")]
public string? Model { get; set; }

/// <summary>How the session ended.</summary>
[JsonPropertyName("endReason")]
public string? EndReason { get; set; }  // "explicit", "timeout", "error"

/// <summary>Top files by activity (read+write count).</summary>
[JsonPropertyName("topFiles")]
public List<string> TopFiles { get; set; } = [];

/// <summary>Tool usage breakdown (tool name → count).</summary>
[JsonPropertyName("toolUsageSummary")]
public Dictionary<string, int> ToolUsageSummary { get; set; } = new();
```

### Status Determination (replacing always-Success)

On session end, determine status from activity data:

```
If explicit Stop hook received AND has file changes or commits:
  → Success
If explicit Stop hook received AND no file changes:
  → Completed (neutral — user may have just asked a question)
If tool call ended with error AND no subsequent success:
  → Failed
If timed out (no Stop hook, inactivity detected):
  → TimedOut (uses LastActivityTime as EndTime)
If user manually closes:
  → Abandoned
```

---

## Part 8: Implementation Phases

### Phase 0: Fix Session Lifecycle ✅ DONE

**Goal**: Sessions reliably close. This must happen before any visualization work.

1. ✅ Add `ITranscriptWatcher` — FileSystemWatcher on active session JSONL files with incremental byte-offset parsing, debounce, per-session inactivity timer
2. ✅ Add inactivity timeout timer (30s check interval, 2min/5min timeout) — `CheckInactiveSessions()`
3. ✅ Expand hooks to include `PreToolUse` and unfiltered `PostToolUse` — auto-installed via `InstallHooks()`
4. ✅ Fix `HandleSessionStopAsync` — sets EndTime, schedules 60s retention removal
5. ✅ Fix status determination — `DetermineEndStatus()` checks file writes, tool errors, and end reason (explicit/timeout) to assign Completed/Failed/TimedOut
6. ✅ Add `HookEventType.ToolStart` and `HookEventType.ToolEnd` to the enum
7. ✅ Process all tool types in PostToolUse (not just file tools)
8. ✅ **NEW**: Session recovery — `EnsureLiveSession()` bootstraps live sessions from any hook event, hydrated from `ClaudeSessionIndexService` with real start time and metadata

**Architecture change**: Eliminated custom `session-*.json` persistence entirely. Historical sessions come from Claude Code's own `sessions-index.json` via `ClaudeSessionIndexService`. Live sessions are in-memory only (`Dictionary<string, LiveSession>`), created from hooks and disposed after 60s retention.

**Test**: Start a session, kill Claude Code without Stop hook → session auto-closes within 2-5 minutes via `CheckInactiveSessions`.

### Phase 1: Rich Data Collection ✅ DONE

**Goal**: Build `SessionActivityState` with full tool call and agent tracking.

1. ✅ Create domain models: `ToolCall`, `AgentInstance`, `ConversationMessage`, `FileActivity`, `ContextBreakdown` — all in `TerminalHost.Core/Domain/`
2. ✅ Create `ActivityEvent` and `ActivityEventType`
3. ✅ Create `ISessionActivityService` — maintains `SessionActivityState` per active session, processes hook events into rich activity data
4. ✅ Enhanced `TranscriptParserService` — `ParseLines()` extracts all content block types (text, thinking, tool_use, tool_result) and emits `ActivityEvent` stream. Reused by both full-file and incremental parsing.
5. ✅ Hook event → `ActivityEvent` translation — `SessionActivityService.ProcessToolStart/End/SessionStart/Stop`
6. ✅ Deduplication logic (hook + JSONL) — `TranscriptWatcher` feeds events via `ProcessTranscriptEvents()`, dedup by `SeenToolUseIds`/`SeenMessageIds` on `SessionActivityState`
7. ✅ On session end: `DetermineEndStatus()` sets lifecycle from activity data; timeout paths update `SessionActivityState.Lifecycle`; transcript enrichment runs on explicit Stop; watcher provides incremental enrichment for timeouts

### Phase 2: Surface Data in Existing UI ✅ DONE

**Goal**: Make the rich data visible without new visualization infrastructure.

1. ✅ Session detail panel shows: tool call count by category (reads/writes/shell/searches), subagent count, model, duration, time range, messages, project path, session ID
2. ✅ "Currently active" indicator shows current tool name + input summary
3. ✅ Session cards show LIVE badge, activity summary (e.g., "42 tools · 12 reads · 8 writes · 3 shell")
4. ✅ Right-click context menu: Open Project Folder, Open Transcript Folder, Copy Session ID
5. ✅ Pop-out window (⧉ button + command palette) for multi-monitor use
6. ✅ Search + "Live only" filter
7. ✅ Stable `ObservableCollection<SessionCardViewModel>` with reconciliation loop (2s timer) — no collection thrashing, no threading crashes

**Architecture**: `TimelineTabViewModel` reconciles from two sources every 2s:
- `ITimelineService.GetLiveSessions()` — in-memory active sessions from hooks
- `IClaudeSessionIndexService.GetAllSessions()` — historical sessions from `~/.claude/projects/`

Live sessions take priority when both sources have the same ID. All UI mutations happen on the UI thread via `DispatcherTimer`.

### Phase 3: Spark Canvas → [SparkCanvas.md](SparkCanvas.md)

**Goal**: Canvas-based real-time visualization of agent execution. Renamed to "Spark Canvas".

Specified in a separate document: **[SparkCanvas.md](SparkCanvas.md)**

Sub-phases: 3a (minimal canvas MVP — **in progress**), 3b (holographic polish), 3c (interactive panels), 3d (remaining gaps — permission detection, context breakdown, subagent transcript watching).

**Phase 3a status (2026-03-26)**: WebView2-hosted canvas with force-directed graph, SSE event pipeline, REST endpoints, and command palette entries all compile. Custom force simulation (no D3 dependency). Multi-session observatory planned.

---

## Part 9: Token Estimation

Simple heuristic matching industry practice:

```csharp
public static class TokenEstimator
{
    private const int CharsPerToken = 4;

    public static int Estimate(string? content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        return Math.Max(1, content.Length / CharsPerToken);
    }

    public static int EstimateToolResult(string? content, string toolName)
    {
        var baseEstimate = Estimate(content);
        // Search tools often return structured/repetitive content → fewer effective tokens
        return toolName switch
        {
            "Grep" or "Glob" => (int)(baseEstimate * 0.8),
            _ => baseEstimate
        };
    }
}
```

---

## Open Questions

1. **Inactivity timeout value**: ✅ Resolved — 2 minutes for basic timeout, 5 minutes for no-activity timeout. Not configurable yet.

2. **JSONL watcher performance**: ✅ Resolved — `TranscriptWatcher` uses incremental byte-offset parsing with 300ms debounce. Only newly appended lines are read and parsed.

3. **Hook volume**: ✅ Resolved — hook events are processed through named pipe IPC without issues in practice. `SessionActivityService` handles rapid events efficiently.

4. **Backward compatibility**: ✅ Resolved — eliminated custom ClaudeSession persistence entirely. Historical sessions come from Claude Code's own sessions-index.json, which is always up to date.

5. **Cross-workspace session tracking**: ✅ Resolved — `ISessionActivityService` is a singleton tracking all workspaces. `ClaudeSessionIndexService` scans all `~/.claude/projects/*/sessions-index.json` directories.

## Success Criteria

1. ✅ **No more ghost sessions** — sessions auto-close within 2-5 minutes via inactivity timeout; sessions that started before TerminalHost are recovered via `EnsureLiveSession`
2. ✅ **Accurate status** — `DetermineEndStatus()` assigns Completed/Failed/TimedOut based on file writes, tool errors, and end reason. UI shows distinct icons/colors for each.
3. ✅ **Tool-level visibility** — can see which tools ran, how long each took, what files were touched (via `SessionActivityState`)
4. ✅ **Subagent awareness** — can see when subagents are spawned and count them (via Agent/Task tool detection)
5. ✅ **Data model validated** — `SessionActivityState` accurately represents real Claude Code sessions
6. ✅ **No more persistence bugs** — eliminated custom session-*.json files; source of truth is Claude Code's own sessions-index.json
7. ✅ **No more UI crashes** — stable ObservableCollection with reconciliation loop, all mutations on UI thread
