# Timeline Mode Specification

Timeline Mode is an advanced mode for TerminalHost that provides a visual timeline view of AI-assisted development work. It organizes development into **intents** (goals/features), each backed by a git worktree, with Claude Code sessions displayed as blocks on a timeline.

## Core Concepts

### Intent = Swimlane = Worktree

Each intent represents a development goal or feature:
- **1 swimlane = 1 worktree = 1 intent**
- Each intent gets its own git worktree (e.g., `feature/auth`, `hotfix/payment`, `experiment/drizzle`)
- Intents are displayed as horizontal swimlanes in the timeline
- Multiple intents can be worked on in parallel across different worktrees

### Claude Code Sessions

Sessions are blocks on the timeline representing Claude Code work:
- A Claude Code "line" is a single session instance (via `--continue` flag)
- A second Claude Code line in the same swimlane represents either:
  - A **branch/fork** from a specific point (to try an alternative approach)
  - A **new session** (continuing from where the previous left off)
- Sessions track: duration, files changed, commands run, agent notes

### Intent Context

When creating an intent, you can provide context that every Claude Code session within that intent will load:
- Sessions start with: `CLAUDE.md` + `intent-context.md`
- Context can include: goals, constraints, relevant files, coding standards specific to the intent

## Features

### Timeline View

- **Time scale**: Toggle between Minutes, Hours, Days views
- **Current time marker**: Diamond indicator showing present time
- **Focus time**: Accumulated active work time across all intents
- **Session blocks**: Visual representation of Claude Code sessions
  - Green checkmark = Success
  - Red X = Failed
  - Blue spinner = Running
  - Gray = Abandoned path

### Intent Sidebar

Left panel showing all intents with:
- **Intent name**: Human-readable goal (e.g., "Implement user authentication")
- **Branch name**: Git worktree branch (e.g., `feature/auth`)
- **Status badge**: `active`, `completed`, `paused`
- **Agent indicator**: Shows when Claude Code is currently running
- **Context button**: View/edit intent context
- **Fork count**: Number of branched attempts

### Session Detail Popup

Clicking a session block shows:
- **Status**: SUCCESS / FAILURE with time range
- **Commit**: Hash and message (if committed)
- **Files Changed**: List with diff stats (+additions -deletions)
- **Commands**: Shell commands executed during session
- **Agent Notes**: Claude's summary of work done
- **Actions**:
  - **Fork from here**: Create new branch from this session's state
  - **Cherry-pick**: Apply this session's changes to another intent

### Forking & Branching

- Fork from any completed session to try alternative approaches
- Forked sessions appear as parallel tracks in the same swimlane
- Abandoned paths are shown in gray
- Can cherry-pick successful changes from one path to another

### Status Bar

Bottom bar showing aggregate statistics:
- Total intents count
- Active forks count
- Currently running sessions
- Total commits made

## User Stories

- **Parallel Development**: Work on multiple features simultaneously, each in isolated worktrees
- **Experimentation**: Fork from any point to try alternative approaches without losing original work
- **Context Preservation**: Give Claude Code persistent context per intent via `intent-context.md`
- **Progress Visualization**: See timeline of all Claude Code sessions across all intents
- **Recovery**: Return to any successful session point via git worktrees
- **Knowledge Transfer**: Cherry-pick learnings from experiments to main development

## Data Model

### Intent

```csharp
public record Intent
{
    public string Id { get; init; }
    public string Name { get; init; }           // "Implement user authentication"
    public string WorktreePath { get; init; }    // Full path to worktree directory
    public string BranchName { get; init; }      // "feature/auth"
    public IntentStatus Status { get; init; }    // Active, Completed, Paused
    public string? ContextFilePath { get; init; } // Path to intent-context.md
    public DateTime CreatedAt { get; init; }
    public List<string> SessionIds { get; init; }
}

public enum IntentStatus { Active, Completed, Paused }
```

### ClaudeSession

```csharp
public record ClaudeSession
{
    public string Id { get; init; }
    public string IntentId { get; init; }
    public string? ParentSessionId { get; init; } // null for first session, set for forks
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public SessionStatus Status { get; init; }   // Running, Success, Failed, Abandoned
    public string? CommitHash { get; init; }
    public string? CommitMessage { get; init; }
    public List<FileChange> FilesChanged { get; init; }
    public List<string> CommandsExecuted { get; init; }
    public string? AgentNotes { get; init; }
    public string? ContinueSessionId { get; init; } // Claude Code --continue ID
}

public enum SessionStatus { Running, Success, Failed, Abandoned }

public record FileChange(string Path, int Additions, int Deletions);
```

### TimelineState

```csharp
public record TimelineState
{
    public DateTime FocusStartTime { get; init; }
    public TimeSpan AccumulatedFocusTime { get; init; }
    public TimeScale CurrentScale { get; init; } // Minutes, Hours, Days
    public List<string> VisibleIntentIds { get; init; }
}

public enum TimeScale { Minutes, Hours, Days }
```

## UI Design

### Main Layout

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Timeline Mode    4h 47m     11:41    [Minutes] [Hours] [Days]  [+ New Intent]│
│ Wed, Jan 15     Focus Time  Current                                         │
├─────────────┬───────────────────────────────────────────────────────────────┤
│             │        08:00      09:00      10:00      11:00     ◆          │
│ Intent List │ ──────────────────────────────────────────────────────────────│
│             │                                                               │
│ ▼ Auth      │        [✓ CC 25m] [✓ CC 25m]           [● CC working...]     │
│   feature/  │                                                               │
│   ● running │                                                               │
│             │                                                               │
│ ▼ DB Layer  │              [✓ CC 25m]   [✓ CC] [✓ CC]                      │
│   feature/  │                           abandoned paths                     │
│   + Start   │                                                               │
│             │                                                               │
│ ▼ Drizzle   │                    ●───────────────[✓ CC 25m] [● CC...]      │
│   experiment│                   fork                                        │
│   ● running │                                                               │
│             │                                                               │
│ ▼ Payment   │        [✗ CC 15m] [✓ CC 15m]                                 │
│   hotfix/   │                                                               │
│   completed │                                                               │
├─────────────┴───────────────────────────────────────────────────────────────┤
│ 4 intents · 1 forks · 2 running · 6 commits     ■Success ■Failed ●Running □ │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Session Detail Popup

```
┌─────────────────────────────────────────────┐
│ [✓ CC 25m    ] [● CC working...]           │
│  └── selected                               │
├─────────────────────────────────────────────┤
│ SUCCESS  10:45 → 11:10                    ✕ │
├─────────────────────────────────────────────┤
│ x1y2z3a                                     │
│ Replace Prisma with Drizzle ORM             │
├─────────────────────────────────────────────┤
│ FILES CHANGED                               │
│ src/db/schema.ts              +95  -0       │
│ src/db/client.ts              +20  -15      │
│ drizzle.config.ts             +12  -0       │
├─────────────────────────────────────────────┤
│ COMMANDS                                    │
│ $ npm uninstall prisma @prisma/client       │
│ $ npm install drizzle-orm                   │
│ $ npm run test                              │
├─────────────────────────────────────────────┤
│ AGENT NOTES                                 │
│ Switched to Drizzle. Lighter weight,        │
│ better TypeScript inference.                │
├─────────────────────────────────────────────┤
│ [Fork from here]          [Cherry-pick]     │
└─────────────────────────────────────────────┘
```

### New Intent Dialog

```
┌─────────────────────────────────────────────┐
│ New Intent                                ✕ │
├─────────────────────────────────────────────┤
│ Name:                                       │
│ ┌─────────────────────────────────────────┐ │
│ │ Implement user authentication           │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ Branch:                                     │
│ ┌─────────────────────────────────────────┐ │
│ │ feature/auth                            │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ Base branch:  [main            ▼]           │
│                                             │
│ Context (optional):                         │
│ ┌─────────────────────────────────────────┐ │
│ │ Focus on JWT-based auth. Use existing   │ │
│ │ User model. Add middleware for protected│ │
│ │ routes. Tests required for all auth     │ │
│ │ flows.                                  │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│        [Cancel]    [Create & Start]         │
└─────────────────────────────────────────────┘
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+I` | Open Timeline Mode |
| `Ctrl+Alt+N` | New Intent |
| `Ctrl+Alt+S` | Start/Resume session in current intent |
| `Ctrl+Alt+F` | Fork from selected session |
| `↑` / `↓` | Navigate between intents |
| `←` / `→` | Navigate between sessions in timeline |
| `Enter` | Open session detail popup |
| `Escape` | Close popup / Exit Timeline Mode |

## Configuration

Timeline Mode state persisted in `config.json`:

```json
{
  "timelineIDE": {
    "enabled": false,
    "focusTime": "04:47:00",
    "currentScale": "Hours",
    "intents": [
      {
        "id": "intent-1",
        "name": "Implement user authentication",
        "worktreePath": "P:\\Project\\.worktrees\\feature-auth",
        "branchName": "feature/auth",
        "status": "Active",
        "contextFilePath": "P:\\Project\\.worktrees\\feature-auth\\intent-context.md",
        "createdAt": "2025-01-15T08:00:00Z",
        "sessionIds": ["session-1", "session-2", "session-3"]
      }
    ],
    "sessions": [
      {
        "id": "session-1",
        "intentId": "intent-1",
        "parentSessionId": null,
        "startTime": "2025-01-15T08:15:00Z",
        "endTime": "2025-01-15T08:40:00Z",
        "status": "Success",
        "commitHash": "abc123",
        "commitMessage": "Add login form component",
        "filesChanged": [
          { "path": "src/components/LoginForm.tsx", "additions": 45, "deletions": 0 }
        ],
        "commandsExecuted": ["npm run test"],
        "agentNotes": "Created login form with email/password fields and validation.",
        "continueSessionId": "claude-session-xyz"
      }
    ]
  }
}
```

## Services

### ITimelineService

Manages Timeline Mode state, intents, and sessions:

```csharp
public interface ITimelineService
{
    // Intent management
    Task<Intent> CreateIntentAsync(string name, string branchName, string? baseBranch, string? context);
    Task<Intent> GetIntentAsync(string intentId);
    Task<IReadOnlyList<Intent>> GetAllIntentsAsync();
    Task UpdateIntentStatusAsync(string intentId, IntentStatus status);
    Task DeleteIntentAsync(string intentId);

    // Session management
    Task<ClaudeSession> StartSessionAsync(string intentId, string? forkFromSessionId = null);
    Task<ClaudeSession> GetSessionAsync(string sessionId);
    Task UpdateSessionAsync(ClaudeSession session);
    Task AbandonSessionAsync(string sessionId);

    // Focus time
    TimeSpan GetAccumulatedFocusTime();
    void StartFocusTimer();
    void PauseFocusTimer();

    // Cherry-pick
    Task CherryPickSessionAsync(string sourceSessionId, string targetIntentId);
}
```

### ISessionTracker

Monitors running Claude Code sessions and captures metadata:

```csharp
public interface ISessionTracker
{
    event EventHandler<SessionProgressEventArgs> SessionProgress;
    event EventHandler<SessionCompletedEventArgs> SessionCompleted;

    Task StartTrackingAsync(string sessionId, string worktreePath);
    Task StopTrackingAsync(string sessionId);

    // Captures git diff, commands from terminal output, etc.
    Task<SessionMetadata> CaptureSessionMetadataAsync(string sessionId);
}
```

## Integration with Existing Features

### Git Worktree Service

Timeline Mode builds on the existing `IGitWorktreeService`:
- Creating an intent creates a new worktree
- Each intent's worktree is managed via the worktree service
- Deleting an intent optionally removes the worktree

### Terminal Management

- Each intent can have an active terminal pair in its worktree
- Session blocks correspond to Claude Code terminal activity
- Terminal output is monitored for command extraction

### Workspace Sidebar

Timeline Mode can work alongside the existing workspace sidebar:
- Intents appear as special entries in the workspace sidebar
- Clicking an intent in the sidebar focuses it in the timeline view

## Implementation Phases

### Phase 1: Core Infrastructure
- Intent and Session domain models
- TimelineService for state management
- Basic persistence in config.json
- Git worktree integration for intents

### Phase 2: Timeline UI
- Timeline view with swimlanes
- Session blocks with status indicators
- Time scale switching (Minutes/Hours/Days)
- Intent sidebar

### Phase 3: Session Tracking
- Monitor Claude Code sessions
- Capture files changed, commands, notes
- Link sessions to git commits
- Session detail popup

### Phase 4: Advanced Features
- Fork from session
- Cherry-pick between intents
- Intent context files
- Focus time tracking
- Session abandonment

### Phase 5: Polish
- Keyboard navigation
- Drag to reorder intents
- Export/import timeline data
- Statistics and insights

## Session Tracking via Claude Code Hooks

Session tracking is the core challenge for Timeline Mode. Rather than unreliably parsing terminal output, we leverage **Claude Code's hooks system** to receive structured events directly from Claude Code.

### Why Hooks?

| Approach | Reliability | Data Quality |
|----------|-------------|--------------|
| Terminal output parsing | ❌ Low - ANSI codes, formats change | Limited |
| Process lifecycle only | ⚠️ Medium - start/end times only | Minimal |
| Git-only tracking | ✅ High - git is source of truth | Commits only |
| **Claude Code Hooks** | ✅ High - structured events | Rich metadata |

Hooks provide:
- Exact session boundaries (start/stop events)
- Working directory (maps to Intent/worktree)
- Session ID (for correlation)
- Transcript path (for detailed analysis)
- Tool usage events (files modified)

### Claude Code Hooks Overview

Claude Code hooks are user-defined commands that run at specific lifecycle events. Configuration can be:
- **User-level**: `~/.claude/settings.json` (applies to all projects)
- **Project-level**: `.claude/settings.json` (per repository)
- **Plugin**: `.claude-plugin/` directory (installable package)

We use the **plugin approach** for easy installation without modifying user config files.

References:
- [Claude Code Hooks Reference](https://code.claude.com/docs/en/hooks)
- [Claude Code Plugins](https://claudeai.dev/blog/claude-code-plugins-introduction/)
- [GitButler's Hook Integration](https://docs.gitbutler.com/features/ai-integration/claude-code-hooks)

### Hook Events We Use

| Event | When | Purpose |
|-------|------|---------|
| `SessionStart` | Claude Code session begins | Record session start, create tracking entry |
| `PostToolUse` | After Write/Edit/MultiEdit | Track files modified during session |
| `Stop` | Session ends (exit, Ctrl+C) | Finalize session, gather git data |

### Hook Input Data (via stdin)

Hooks receive JSON via stdin with these fields:

```json
{
  "session_id": "abc123-def456",
  "cwd": "P:\\Project\\feature-auth",
  "transcript_path": "~/.claude/projects/.../session.jsonl",
  "permission_mode": "default",
  "hook_event_name": "Stop",
  "tool_name": "Write",
  "tool_input": {
    "file_path": "src/auth/login.ts",
    "content": "..."
  },
  "tool_use_id": "toolu_01ABC123"
}
```

Key fields:
- `session_id` - Unique identifier, persists across `--continue`
- `cwd` - Working directory, **maps to Intent worktree path**
- `transcript_path` - JSONL file with full conversation (for agent notes extraction)
- `tool_name` / `tool_input` - For PostToolUse, details of the tool call

### TerminalHost Plugin Structure

```
terminalhost-session-tracker/
├── .claude-plugin/
│   ├── manifest.json
│   └── hooks/
│       └── settings.json
└── README.md
```

**manifest.json:**
```json
{
  "name": "terminalhost-session-tracker",
  "version": "1.0.0",
  "description": "Session tracking for TerminalHost Timeline Mode",
  "author": "TerminalHost",
  "homepage": "https://github.com/user/terminalhost"
}
```

**hooks/settings.json:**
```json
{
  "hooks": {
    "SessionStart": [
      {
        "type": "command",
        "command": "host.exe --hook session-start"
      }
    ],
    "Stop": [
      {
        "type": "command",
        "command": "host.exe --hook session-stop"
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Write|Edit|MultiEdit",
        "hooks": [
          {
            "type": "command",
            "command": "host.exe --hook file-changed"
          }
        ]
      }
    ]
  }
}
```

### Installation

Users install the plugin via Claude Code:

```bash
# From local path (during development)
claude /plugin install ./terminalhost-session-tracker

# From registry (future)
claude /plugin install terminalhost/session-tracker
```

Or via TerminalHost UI:
- Settings → Timeline Mode → "Install Session Tracking Hooks" button
- Copies plugin to appropriate location and runs install command

### IPC Architecture

```
┌────────────────────┐                    ┌─────────────────────┐
│    Claude Code     │                    │    TerminalHost     │
│                    │                    │    (Main App)       │
│  ┌──────────────┐  │                    │                     │
│  │ SessionStart │──┼── stdin JSON ──────┼──► ┌─────────────┐  │
│  │    Hook      │  │                    │    │ Named Pipe  │  │
│  └──────────────┘  │     host.exe       │    │   Server    │  │
│                    │    --hook ...      │    └──────┬──────┘  │
│  ┌──────────────┐  │         │          │           │         │
│  │ PostToolUse  │──┼─────────┘          │           ▼         │
│  │    Hook      │  │                    │    ┌─────────────┐  │
│  └──────────────┘  │                    │    │  Timeline   │  │
│                    │                    │    │   Service   │  │
│  ┌──────────────┐  │                    │    └─────────────┘  │
│  │    Stop      │──┼────────────────────┼──►                  │
│  │    Hook      │  │                    │                     │
│  └──────────────┘  │                    │                     │
└────────────────────┘                    └─────────────────────┘
```

### CLI Arguments for Hooks

New arguments for `host.exe`:

```bash
# Called by Claude Code hooks (reads JSON from stdin)
host.exe --hook session-start    # SessionStart event
host.exe --hook session-stop     # Stop event
host.exe --hook file-changed     # PostToolUse for Write/Edit
```

Each command:
1. Reads JSON from stdin
2. Parses event data
3. Forwards to main TerminalHost instance via named pipe IPC
4. If main app not running, queues to file

### Handling Offline (TerminalHost Not Running)

Hooks fire machine-wide, even when TerminalHost isn't running. Options:

| Approach | Pros | Cons |
|----------|------|------|
| Ignore | Simplest | Lose data |
| Queue to file | No data loss | Need cleanup |
| Read transcript later | Full data | Complex parsing |

**Recommended: Queue + Transcript**

Events are queued to `%APPDATA%\TerminalHost\hook-queue.jsonl`:

```jsonl
{"event":"session-start","session_id":"abc","cwd":"P:\\Project","transcript_path":"...","timestamp":"2025-01-15T10:00:00Z"}
{"event":"file-changed","session_id":"abc","file":"src/foo.ts","timestamp":"2025-01-15T10:05:00Z"}
{"event":"session-stop","session_id":"abc","cwd":"P:\\Project","timestamp":"2025-01-15T10:30:00Z"}
```

On TerminalHost startup:
1. Read and process queued events
2. Match sessions to Intents by `cwd`
3. Read transcripts for additional metadata
4. Clear processed entries from queue

### Intent Matching

Sessions are matched to Intents by comparing `cwd` to Intent worktree paths:

```csharp
public Intent? FindIntentByWorkingDirectory(string cwd)
{
    // Normalize paths for comparison
    var normalizedCwd = Path.GetFullPath(cwd).TrimEnd('\\', '/');

    return _intents.FirstOrDefault(intent =>
    {
        var normalizedWorktree = Path.GetFullPath(intent.WorktreePath).TrimEnd('\\', '/');
        return string.Equals(normalizedCwd, normalizedWorktree, StringComparison.OrdinalIgnoreCase);
    });
}
```

Sessions in directories not matching any Intent are:
- Option A: Ignored (only track Intent-related sessions)
- Option B: Tracked in a special "Unassigned" section
- Configurable in settings

### Data Flow

```
1. User installs plugin
   └── claude /plugin install terminalhost/session-tracker

2. User starts Claude Code in Intent worktree (P:\Project\feature-auth)
   └── SessionStart hook fires
       └── host.exe --hook session-start
           └── Stdin: {"session_id":"abc","cwd":"P:\\Project\\feature-auth",...}
           └── IPC to main app: CreateSession(intentId, sessionId, startTime)

3. Claude edits files
   └── PostToolUse hook fires (for Write/Edit/MultiEdit)
       └── host.exe --hook file-changed
           └── Stdin: {"session_id":"abc","tool_input":{"file_path":"src/login.ts",...}}
           └── IPC: AddFileToSession(sessionId, filePath)

4. Session ends (/exit, Ctrl+C, completion)
   └── Stop hook fires
       └── host.exe --hook session-stop
           └── Stdin: {"session_id":"abc","cwd":"P:\\Project\\feature-auth",...}
           └── IPC: FinalizeSession(sessionId)
               └── Get git commits since session start
               └── Calculate files changed, additions/deletions
               └── Optionally parse transcript for agent notes
               └── Update session status (Success/Failed/NoChanges)
```

### Updated Data Model

```csharp
public record ClaudeSession
{
    // From hooks
    public string Id { get; init; }                    // Claude's session_id
    public string IntentId { get; init; }              // Matched by cwd → worktreePath
    public string? TranscriptPath { get; init; }       // For reading agent notes
    public string? ParentSessionId { get; init; }      // For forked sessions

    // Timing
    public DateTime StartTime { get; init; }           // SessionStart hook
    public DateTime? EndTime { get; init; }            // Stop hook

    // Status
    public SessionStatus Status { get; init; }         // Running → Success/Failed/NoChanges

    // Tracked during session (from PostToolUse hooks)
    public List<string> FilesModifiedDuringSession { get; init; } = [];

    // Calculated at session end (from git)
    public string? StartingCommitHash { get; init; }   // HEAD at session start
    public List<SessionCommit> Commits { get; init; } = [];
    public List<FileChange> FilesChanged { get; init; } = [];
    public int TotalAdditions { get; init; }
    public int TotalDeletions { get; init; }

    // Optional: Parsed from transcript
    public List<string> CommandsExecuted { get; init; } = [];
    public string? AgentSummary { get; init; }
    public string? UserNotes { get; init; }            // Manual user annotation
}

public record SessionCommit(string Hash, string Message, DateTime Timestamp);

public enum SessionStatus
{
    Running,      // Session in progress
    Success,      // Completed with commits
    NoChanges,    // Completed without commits or uncommitted changes
    Incomplete,   // Completed with uncommitted changes
    Failed,       // User marked as failed
    Abandoned     // User abandoned this path
}
```

### Transcript Parsing (Optional Enhancement)

The transcript JSONL file contains the full conversation. We can parse it for:

**Commands Executed:**
```csharp
// Find Bash tool calls in transcript
var bashCalls = transcriptLines
    .Where(line => line.Contains("\"tool_name\":\"Bash\""))
    .Select(line => ParseToolInput(line, "command"));
```

**Agent Summary:**
```csharp
// Find Claude's final message or look for summary patterns
var lastAssistantMessage = transcriptLines
    .Where(line => line.Contains("\"role\":\"assistant\""))
    .LastOrDefault();
```

This is deferred to Phase 4 as it depends on Claude Code's transcript format.

### Services Update

```csharp
/// <summary>
/// Handles hook events from Claude Code via IPC.
/// </summary>
public interface IHookEventHandler
{
    Task HandleSessionStartAsync(HookEventData data);
    Task HandleFileChangedAsync(HookEventData data);
    Task HandleSessionStopAsync(HookEventData data);
}

/// <summary>
/// Manages the hook event queue for offline handling.
/// </summary>
public interface IHookEventQueue
{
    Task EnqueueAsync(HookEvent hookEvent);
    Task<IReadOnlyList<HookEvent>> DequeueAllAsync();
    Task ClearAsync();
}

/// <summary>
/// Hook event data received from Claude Code.
/// </summary>
public record HookEventData
{
    public string SessionId { get; init; }
    public string Cwd { get; init; }
    public string? TranscriptPath { get; init; }
    public string? HookEventName { get; init; }
    public string? ToolName { get; init; }
    public string? FilePath { get; init; }  // Extracted from tool_input for Write/Edit
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Queued hook event for offline processing.
/// </summary>
public record HookEvent
{
    public string Event { get; init; }       // "session-start", "file-changed", "session-stop"
    public HookEventData Data { get; init; }
}
```

### Updated ISessionTracker

```csharp
public interface ISessionTracker
{
    // Events for UI updates
    event EventHandler<SessionStartedEventArgs> SessionStarted;
    event EventHandler<SessionProgressEventArgs> SessionProgress;
    event EventHandler<SessionCompletedEventArgs> SessionCompleted;

    // Called by IHookEventHandler
    Task OnSessionStartAsync(string sessionId, string cwd, string? transcriptPath);
    Task OnFileChangedAsync(string sessionId, string filePath);
    Task OnSessionStopAsync(string sessionId, string cwd);

    // Manual operations
    Task<ClaudeSession?> GetActiveSessionAsync(string intentId);
    Task AbandonSessionAsync(string sessionId);
    Task MarkSessionFailedAsync(string sessionId, string? reason);
}
```

### Settings

New configuration options:

```json
{
  "timelineIDE": {
    "enabled": false,
    "hooksInstalled": false,
    "trackUnassignedSessions": false,
    "parseTranscriptForCommands": false,
    "parseTranscriptForSummary": false,
    "hookQueuePath": "%APPDATA%\\TerminalHost\\hook-queue.jsonl"
  }
}
```

### UI for Hook Installation

Settings → Timeline Mode section:

```
┌─────────────────────────────────────────────────────────────┐
│ Timeline Mode                                                │
├─────────────────────────────────────────────────────────────┤
│ [✓] Enable Timeline Mode                                     │
│                                                             │
│ Session Tracking                                            │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Status: ⚠️ Hooks not installed                          │ │
│ │                                                         │ │
│ │ Timeline Mode requires Claude Code hooks to track        │ │
│ │ sessions automatically.                                 │ │
│ │                                                         │ │
│ │ [Install Session Tracking Hooks]                        │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ [ ] Track sessions outside of Intents                       │
│ [ ] Parse transcripts for executed commands                 │
│ [ ] Parse transcripts for agent summaries                   │
└─────────────────────────────────────────────────────────────┘
```

After installation:

```
│ Session Tracking                                            │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Status: ✅ Hooks installed                              │ │
│ │                                                         │ │
│ │ Plugin: terminalhost-session-tracker v1.0.0             │ │
│ │                                                         │ │
│ │ [Reinstall]  [Uninstall]                                │ │
│ └─────────────────────────────────────────────────────────┘ │
```

### Phase 3 Implementation Steps

1. **CLI Hook Handler**
   - Add `--hook` argument parsing to `Program.cs`
   - Read stdin JSON, parse `HookEventData`
   - Forward to main instance via named pipe or queue to file

2. **IPC Extensions**
   - Add hook event message types to `SingleInstanceService`
   - Handle incoming hook events in main application

3. **Hook Event Queue**
   - Implement `HookEventQueue` service
   - Queue file read/write with file locking
   - Startup processing of queued events

4. **Session Tracker Service**
   - Implement `ISessionTracker` with hook event handlers
   - Intent matching by working directory
   - Git integration for commit extraction on session end

5. **Plugin Package**
   - Create plugin directory structure
   - Write manifest.json and hooks/settings.json
   - Test installation via `claude /plugin install`

6. **Settings UI**
   - Add Timeline Mode section to Settings
   - Hook installation status and buttons
   - Configuration options

7. **Testing**
   - Unit tests for hook event parsing
   - Integration tests for IPC flow
   - Manual testing with real Claude Code sessions
