# Timeline IDE Specification

Timeline IDE is an advanced mode for TerminalHost that provides a visual timeline view of AI-assisted development work. It organizes development into **intents** (goals/features), each backed by a git worktree, with Claude Code sessions displayed as blocks on a timeline.

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
│ Timeline IDE    4h 47m     11:41    [Minutes] [Hours] [Days]  [+ New Intent]│
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
| `Ctrl+Shift+I` | Open Timeline IDE mode |
| `Ctrl+Alt+N` | New Intent |
| `Ctrl+Alt+S` | Start/Resume session in current intent |
| `Ctrl+Alt+F` | Fork from selected session |
| `↑` / `↓` | Navigate between intents |
| `←` / `→` | Navigate between sessions in timeline |
| `Enter` | Open session detail popup |
| `Escape` | Close popup / Exit Timeline IDE |

## Configuration

Timeline IDE state persisted in `config.json`:

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

Manages Timeline IDE state, intents, and sessions:

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

Timeline IDE builds on the existing `IGitWorktreeService`:
- Creating an intent creates a new worktree
- Each intent's worktree is managed via the worktree service
- Deleting an intent optionally removes the worktree

### Terminal Management

- Each intent can have an active terminal pair in its worktree
- Session blocks correspond to Claude Code terminal activity
- Terminal output is monitored for command extraction

### Workspace Sidebar

Timeline IDE can work alongside the existing workspace sidebar:
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
