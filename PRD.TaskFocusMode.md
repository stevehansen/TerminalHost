# Task/Focus Mode - Implementation Specification

## Overview

Task/Focus Mode is a task management system integrated into TerminalHost that helps developers organize their work by:
- **Quick capture**: Easily enqueue tasks with minimal details, flesh out later
- **Focus mode**: Constrain visible project tabs to those relevant to the current task
- **Branch/PR linking**: Auto-detect and link tasks to git branches and pull requests
- **Meeting notes**: Lightweight scribbles that can evolve into proper tasks

## User Stories

### US-1: Quick Task Capture
> As a developer, I want to quickly jot down a task idea without interrupting my flow, so I can remember to do it later.

**Acceptance Criteria:**
- Single keyboard shortcut opens minimal input
- Type title, press Enter → task created in backlog
- No required fields besides title
- Can optionally expand for more details (Tab key)

### US-2: Focus Mode
> As a developer, I want to hide unrelated project tabs when working on a task, so I can focus without distraction.

**Acceptance Criteria:**
- Toggle focus mode on/off
- Only tabs associated with current task are visible
- Visual indicator shows focus mode is active
- Can quickly exit focus mode to see all tabs

### US-3: Branch/PR Linking
> As a developer, I want my "Review PR #123" task to automatically find the branch and show PR details, so I have context without switching to browser.

**Acceptance Criteria:**
- Parse task title for PR/issue patterns
- Auto-detect matching git branch in associated project
- Fetch PR details from GitHub (title, author, status, diff stats)
- One-click to checkout branch or open PR in browser

### US-4: Meeting Notes
> As a developer, I want to capture quick notes during meetings that might become tasks later, so nothing falls through the cracks.

**Acceptance Criteria:**
- Separate "quick note" capture (even simpler than tasks)
- Notes visible in task panel
- One-click to convert note into a proper task
- Notes can stay as notes (not everything becomes a task)

### US-5: Task Hierarchy
> As a developer, I want to create subtasks when I discover prerequisites, so I can track "need to fix X first" situations.

**Acceptance Criteria:**
- Tasks can have parent-child relationships
- Creating subtask from current task links them
- Completing subtask returns focus to parent
- Visual tree view shows hierarchy

---

## Data Model

### FocusTask

```csharp
namespace TerminalHost.Domain;

public class FocusTask
{
    /// <summary>Unique identifier (e.g., "task-20251217120000")</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Short task title (required)</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Longer description (optional, can add later)</summary>
    public string? Description { get; set; }

    /// <summary>Scratch notes, meeting notes, scribbles for this task</summary>
    public string? Notes { get; set; }

    /// <summary>Parent task ID for hierarchy (null = root level)</summary>
    public string? ParentTaskId { get; set; }

    /// <summary>Associated project directories</summary>
    public List<string> ProjectPaths { get; set; } = new();

    /// <summary>Current status</summary>
    public FocusTaskStatus Status { get; set; } = FocusTaskStatus.NotStarted;

    /// <summary>Priority for sorting (higher = more important)</summary>
    public int Priority { get; set; } = 0;

    /// <summary>Optional tags for categorization</summary>
    public List<string> Tags { get; set; } = new();

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Branch/PR Integration
    /// <summary>Linked git branch name (e.g., "issues/123")</summary>
    public string? LinkedBranch { get; set; }

    /// <summary>Linked PR number (e.g., "123")</summary>
    public string? LinkedPrNumber { get; set; }

    /// <summary>Full PR URL for quick access</summary>
    public string? LinkedPrUrl { get; set; }

    /// <summary>Cached PR details (refreshed on demand)</summary>
    public GitPrDetails? PrDetails { get; set; }
}

public enum FocusTaskStatus
{
    NotStarted,   // In backlog, not yet started
    InProgress,   // Currently being worked on
    Completed,    // Done
    Deferred      // Paused/postponed
}
```

### GitPrDetails

```csharp
namespace TerminalHost.Domain;

public class GitPrDetails
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;  // "open", "closed", "merged"
    public string? BaseBranch { get; set; }
    public string? HeadBranch { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int ChangedFiles { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
```

### QuickNote

```csharp
namespace TerminalHost.Domain;

public class QuickNote
{
    /// <summary>Unique identifier</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Note content</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When the note was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>If converted to task, stores the task ID</summary>
    public string? ConvertedToTaskId { get; set; }

    /// <summary>Optional: associated project path</summary>
    public string? ProjectPath { get; set; }
}
```

### FocusModeState

```csharp
namespace TerminalHost.Domain;

public class FocusModeState
{
    /// <summary>Whether focus mode is currently active</summary>
    public bool IsEnabled { get; set; }

    /// <summary>ID of the current task being worked on</summary>
    public string? CurrentTaskId { get; set; }

    /// <summary>Recently worked-on task IDs (for quick switching)</summary>
    public List<string> TaskHistory { get; set; } = new();
}
```

### Configuration Storage

Add to `AppConfiguration.cs`:

```csharp
public class AppConfiguration
{
    // ... existing properties ...

    /// <summary>Focus mode state (enabled, current task)</summary>
    public FocusModeState FocusMode { get; set; } = new();

    /// <summary>All tasks</summary>
    public List<FocusTask> Tasks { get; set; } = new();

    /// <summary>Quick notes (not yet converted to tasks)</summary>
    public List<QuickNote> QuickNotes { get; set; } = new();
}
```

**JSON Example:**

```json
{
  "focusMode": {
    "isEnabled": true,
    "currentTaskId": "task-20251217120000",
    "taskHistory": ["task-20251217120000", "task-20251216090000"]
  },
  "tasks": [
    {
      "id": "task-20251217120000",
      "title": "Review PR #123",
      "description": "Review the authentication changes from the team",
      "notes": "Meeting notes:\n- Team wants to use JWT instead of sessions\n- Need to check backwards compat",
      "parentTaskId": null,
      "projectPaths": ["P:\\MyProject"],
      "status": "InProgress",
      "priority": 10,
      "tags": ["review", "auth"],
      "createdAt": "2025-12-17T12:00:00Z",
      "startedAt": "2025-12-17T14:00:00Z",
      "completedAt": null,
      "linkedBranch": "issues/123",
      "linkedPrNumber": "123",
      "linkedPrUrl": "https://github.com/myorg/myrepo/pull/123",
      "prDetails": {
        "title": "Improve authentication flow with JWT",
        "author": "teammate",
        "state": "open",
        "baseBranch": "main",
        "headBranch": "issues/123",
        "additions": 150,
        "deletions": 50,
        "changedFiles": 8,
        "updatedAt": "2025-12-17T10:00:00Z",
        "fetchedAt": "2025-12-17T14:00:00Z"
      }
    },
    {
      "id": "task-20251217140000",
      "title": "Fix memory leak",
      "description": null,
      "notes": null,
      "parentTaskId": null,
      "projectPaths": [],
      "status": "NotStarted",
      "priority": 5,
      "tags": [],
      "createdAt": "2025-12-17T14:00:00Z"
    }
  ],
  "quickNotes": [
    {
      "id": "note-20251217150000",
      "text": "discussed perf issues in standup - need to profile",
      "createdAt": "2025-12-17T15:00:00Z",
      "convertedToTaskId": null,
      "projectPath": "P:\\MyProject"
    }
  ]
}
```

---

## UI Design

### Task Panel (`Ctrl+T` or `Ctrl+Shift+T`)

A popup panel (similar to command palette) for managing tasks.

```
┌─────────────────────────────────────────────────────────────────┐
│ 📋 Tasks                                    [Focus: ON] [×]     │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ 🔍 Search tasks...                                          │ │
│ └─────────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│ ● CURRENT TASK                                                  │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ 🟡 Review PR #123                                    ⏱ 2h   │ │
│ │    P:\MyProject                                             │ │
│ │    🔀 issues/123 → main  •  PR: +150/-50  •  8 files       │ │
│ │    ────────────────────────────────────────────────────     │ │
│ │    📝 Notes: Team wants JWT instead of sessions...          │ │
│ │    ────────────────────────────────────────────────────     │ │
│ │    [✓ Complete] [⏸ Pause] [+ Subtask] [🔗 Open PR]         │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ ○ BACKLOG (3)                                                   │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ ⚪ Fix memory leak in logger                         P:5    │ │
│ │ ⚪ Update documentation                              P:3    │ │
│ │ ⚪ Refactor auth module                              P:2    │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ 📝 QUICK NOTES (2)                                              │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ • discussed perf issues in standup        [→ Task] [× Del]  │ │
│ │ • check CI config for flaky test          [→ Task] [× Del]  │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ ✓ COMPLETED TODAY (1)                                           │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ ✅ Fix login button styling                         ⏱ 30m   │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│ [+ New Task]  [+ Quick Note]  [Toggle Focus Mode]               │
└─────────────────────────────────────────────────────────────────┘
```

**Task Panel Features:**
- Search/filter tasks by title, tag, project
- Sections: Current, Backlog, Quick Notes, Completed Today
- Expand current task to show full details + notes
- Quick actions on each task
- Keyboard navigation (↑↓ to select, Enter to start, Space to complete)

### Quick Task Popup (`Ctrl+Shift+Q`)

Minimal popup for rapid task creation.

```
┌─────────────────────────────────────────────────────────────────┐
│ ➕ Quick Task                                                    │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ Review PR #123 authentication changes                       │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ 💡 Tip: Press Tab for more options, Enter to add to backlog    │
└─────────────────────────────────────────────────────────────────┘
```

**Expanded Mode (after Tab):**

```
┌─────────────────────────────────────────────────────────────────┐
│ ➕ New Task                                                      │
├─────────────────────────────────────────────────────────────────┤
│ Title:                                                          │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ Review PR #123 authentication changes                       │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ Project:        [P:\MyProject              ▼]                   │
│ Priority:       [●●●○○ Medium              ▼]                   │
│ Parent Task:    [None (root level)         ▼]                   │
│                                                                 │
│ Description:                                                    │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │                                                             │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ [Cancel]                              [Add to Backlog] [▶ Start]│
└─────────────────────────────────────────────────────────────────┘
```

### Quick Note Popup (`Ctrl+Shift+M`)

Even simpler than quick task - just capture a thought.

```
┌─────────────────────────────────────────────────────────────────┐
│ 📝 Quick Note                                                    │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ discussed auth refactor approach with team                  │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ [Escape to cancel]                    [Enter to save]           │
└─────────────────────────────────────────────────────────────────┘
```

### Focus Mode Indicator

When focus mode is active, show visual indication:

**Option A: Title Bar**
```
TerminalHost - [🎯 Review PR #123] - P:\MyProject
```

**Option B: Status Bar (recommended)**
```
┌─────────────────────────────────────────────────────────────────┐
│ [Tabs...]                                                       │
├─────────────────────────────────────────────────────────────────┤
│ [Terminal content...]                                           │
├─────────────────────────────────────────────────────────────────┤
│ 🎯 Focus: Review PR #123  [✓ Done] [⏸ Pause] [Exit Focus]  ... │
└─────────────────────────────────────────────────────────────────┘
```

**Option C: Colored Border**
- Subtle colored border (e.g., blue/purple) around entire window when in focus mode
- Click border area shows task details

### Task Item in Tab Strip

When focus mode is active, show mini task indicator:

```
┌──────────────────────────────────────────────────────────────────────┐
│ 🎯 Review PR #123 ▼ │ 🤖 MyProject │ 🤖 OtherProject │ [+]           │
└──────────────────────────────────────────────────────────────────────┘
```

The task indicator is clickable to open task panel.

---

## Branch/PR Integration

### Pattern Detection

When a task is created or edited, scan title for these patterns:

| Pattern | Example | Extracts |
|---------|---------|----------|
| `PR #(\d+)` | "Review PR #123" | prNumber: "123" |
| `PR#(\d+)` | "Review PR#123" | prNumber: "123" |
| `#(\d+)` | "Fix #456" | issueNumber: "456" |
| `pull/(\d+)` | "Check pull/789" | prNumber: "789" |
| `issues?/(\d+)` | "Work on issue/123" | issueNumber: "123" |

### Branch Detection

For each associated project, search for matching branches:

```csharp
// Given issueNumber = "123", search for:
var branchPatterns = new[]
{
    $"issues/{issueNumber}",
    $"issue/{issueNumber}",
    $"feature/{issueNumber}",
    $"fix/{issueNumber}",
    $"bugfix/{issueNumber}",
    $"hotfix/{issueNumber}",
    $"pr-{issueNumber}",
    $"{issueNumber}-*",  // e.g., "123-fix-auth"
};

// Run: git branch -a --list '*123*'
// Parse results and match against patterns
```

### PR Details Fetching

Use GitHub CLI (`gh`) if available:

```bash
gh pr view 123 --json title,author,state,baseRefName,headRefName,additions,deletions,changedFiles,updatedAt
```

**Response parsing:**
```json
{
  "title": "Improve authentication flow",
  "author": { "login": "developer" },
  "state": "OPEN",
  "baseRefName": "main",
  "headRefName": "issues/123",
  "additions": 150,
  "deletions": 50,
  "changedFiles": 8,
  "updatedAt": "2025-12-17T10:00:00Z"
}
```

**Fallback:** If `gh` not available, show message suggesting installation, but task still works without PR details.

### Auto-Linking Flow

```
User creates task: "Review PR #123"
         │
         ▼
┌─────────────────────────┐
│ Parse title for PR #123 │
└─────────────────────────┘
         │
         ▼
┌─────────────────────────┐     No project
│ Has associated project? │────────────────► Store prNumber only
└─────────────────────────┘
         │ Yes
         ▼
┌─────────────────────────┐     Not found
│ Search for branch       │────────────────► Store prNumber only
│ matching "123"          │
└─────────────────────────┘
         │ Found: issues/123
         ▼
┌─────────────────────────┐
│ Store linkedBranch      │
│ = "issues/123"          │
└─────────────────────────┘
         │
         ▼
┌─────────────────────────┐     gh not available
│ Try fetch PR details    │────────────────► Task works, no PR details
│ via gh CLI              │
└─────────────────────────┘
         │ Success
         ▼
┌─────────────────────────┐
│ Store prDetails         │
│ Show in task panel      │
└─────────────────────────┘
```

---

## Keyboard Shortcuts

| Shortcut | Action | Context |
|----------|--------|---------|
| `Ctrl+T` | Open Task Panel | Global |
| `Ctrl+Shift+Q` | Quick add task | Global |
| `Ctrl+Shift+M` | Quick add note/memo | Global |
| `Escape` | Close popup / Exit focus mode | In popup / Focus mode |
| `Enter` | Start selected task | Task panel |
| `Space` | Complete current task | Task panel |
| `Delete` | Delete selected task/note | Task panel |
| `Tab` | Expand quick task to full form | Quick task popup |
| `Ctrl+Enter` | Add task and start immediately | Quick task popup |

**Note:** `Ctrl+Shift+T` was previously tab switcher. Options:
1. Move tab switcher to `Ctrl+Tab` (more standard)
2. Use `Ctrl+Shift+F` for Focus/Tasks (F for Focus)
3. Keep both - `Ctrl+Shift+T` for tabs, `Ctrl+T` for tasks

**Recommendation:** Use `Ctrl+T` for Tasks (common pattern), keep `Ctrl+Shift+T` for tab switcher.

---

## Command Palette Integration

Add these commands to the Command Palette:

| Command | Description | Shortcut |
|---------|-------------|----------|
| Tasks: Open Panel | Open the task management panel | Ctrl+T |
| Tasks: Quick Add | Add a new task quickly | Ctrl+Shift+Q |
| Tasks: Quick Note | Add a quick note | Ctrl+Shift+M |
| Tasks: Toggle Focus Mode | Enable/disable focus mode | - |
| Tasks: Complete Current | Mark current task as done | - |
| Tasks: Pause Current | Pause current task, return to backlog | - |
| Tasks: Start Task... | Pick a task from backlog to start | - |
| Tasks: Add Subtask | Add subtask to current task | - |

---

## Services

### TaskService

Core service for task CRUD operations.

```csharp
namespace TerminalHost.Services;

public interface ITaskService
{
    // Task CRUD
    FocusTask CreateTask(string title, string? description = null, string? parentTaskId = null);
    FocusTask? GetTask(string id);
    IReadOnlyList<FocusTask> GetAllTasks();
    IReadOnlyList<FocusTask> GetBacklogTasks();
    IReadOnlyList<FocusTask> GetCompletedTasks(DateTime? since = null);
    void UpdateTask(FocusTask task);
    void DeleteTask(string id);

    // Task state transitions
    void StartTask(string id);
    void CompleteTask(string id);
    void PauseTask(string id);
    void DeferTask(string id);

    // Focus mode
    FocusTask? GetCurrentTask();
    void SetCurrentTask(string? taskId);
    bool IsFocusModeEnabled { get; }
    void ToggleFocusMode();
    void EnableFocusMode(string taskId);
    void DisableFocusMode();

    // Quick notes
    QuickNote CreateNote(string text, string? projectPath = null);
    IReadOnlyList<QuickNote> GetQuickNotes();
    FocusTask ConvertNoteToTask(string noteId);
    void DeleteNote(string noteId);

    // Project association
    void AddProjectToTask(string taskId, string projectPath);
    void RemoveProjectFromTask(string taskId, string projectPath);
    IReadOnlyList<string> GetProjectsForCurrentTask();

    // Events
    event EventHandler<FocusTask?> CurrentTaskChanged;
    event EventHandler<bool> FocusModeChanged;
    event EventHandler TasksChanged;
}
```

### GitPrService

Service for branch detection and PR fetching.

```csharp
namespace TerminalHost.Services;

public interface IGitPrService
{
    // Branch detection
    Task<string?> FindBranchForIssueAsync(string projectPath, string issueNumber);
    Task<IReadOnlyList<string>> GetAllBranchesAsync(string projectPath);

    // PR detection from title
    (string? prNumber, string? issueNumber) ParseTaskTitle(string title);

    // PR details
    Task<GitPrDetails?> FetchPrDetailsAsync(string projectPath, string prNumber);
    bool IsGitHubCliAvailable();

    // Branch operations
    Task<bool> CheckoutBranchAsync(string projectPath, string branchName);
}
```

---

## Implementation Phases

### Phase 1: Core Models & Storage (Day 1)

**Files to create:**
- `Domain/FocusTask.cs` - Task model with all properties
- `Domain/FocusTaskStatus.cs` - Status enum
- `Domain/QuickNote.cs` - Quick note model
- `Domain/FocusModeState.cs` - Focus mode state
- `Domain/GitPrDetails.cs` - PR details model

**Files to modify:**
- `Domain/AppConfiguration.cs` - Add FocusMode, Tasks, QuickNotes properties
- `Services/ConfigurationService.cs` - Ensure new properties serialize correctly

**Deliverable:** Data model in place, persisted to config.json

### Phase 2: TaskService (Day 1-2)

**Files to create:**
- `Services/ITaskService.cs` - Interface
- `Services/TaskService.cs` - Implementation

**Functionality:**
- CRUD operations for tasks and notes
- Task state transitions (start, complete, pause, defer)
- Focus mode state management
- Project association
- Events for UI updates

**Deliverable:** Working task service with persistence

### Phase 3: Task Panel UI (Day 2-3)

**Files to create:**
- `ViewModels/TaskPanelViewModel.cs` - Panel logic
- `Views/Popups/TaskPanelView.xaml` - Panel UI
- `Views/Popups/TaskPanelView.xaml.cs` - Code-behind

**Functionality:**
- Task list with sections (Current, Backlog, Notes, Completed)
- Search/filter
- Task selection and actions
- Keyboard navigation
- Responsive layout

**Deliverable:** Functional task panel accessible via keyboard shortcut

### Phase 4: Focus Mode (Day 3-4)

**Files to modify:**
- `ViewModels/MainViewModel.cs` - Tab filtering logic
- `MainWindow.xaml` - Focus mode indicator
- `Views/TabStrip.xaml` - Task indicator in tab strip

**Functionality:**
- Filter tabs based on current task's project paths
- Visual indicator (status bar or border)
- Quick actions (complete, pause, exit)
- Restore focus mode on app restart

**Deliverable:** Working focus mode that hides unrelated tabs

### Phase 5: Quick Capture (Day 4)

**Files to create:**
- `ViewModels/QuickTaskViewModel.cs`
- `Views/Popups/QuickTaskView.xaml`
- `ViewModels/QuickNoteViewModel.cs`
- `Views/Popups/QuickNoteView.xaml`

**Functionality:**
- Minimal popup for quick task entry
- Tab to expand to full form
- Quick note popup (even simpler)
- Keyboard shortcuts

**Deliverable:** Rapid task/note capture without breaking flow

### Phase 6: Branch/PR Integration (Day 5)

**Files to create:**
- `Services/IGitPrService.cs` - Interface
- `Services/GitPrService.cs` - Implementation

**Files to modify:**
- `Services/TaskService.cs` - Call GitPrService on task create/edit
- `ViewModels/TaskPanelViewModel.cs` - Display PR details

**Functionality:**
- Parse task title for PR/issue patterns
- Search for matching branches
- Fetch PR details via `gh` CLI
- Display in task panel
- Quick actions (checkout, open in browser)

**Deliverable:** Auto-linked tasks with PR context

### Phase 7: Polish & Integration (Day 6)

**Enhancements:**
- Command palette integration (all task commands)
- Help view updates (new shortcuts)
- Settings for task feature (enable/disable, shortcuts)
- Drag-drop task reordering
- Note → Task conversion
- Task time tracking (optional)

**Testing:**
- End-to-end workflow testing
- Edge cases (no projects, no git, no gh CLI)
- Performance with many tasks

---

## Edge Cases & Error Handling

### No Associated Project
- Task works fine without project association
- Branch/PR detection skipped
- User can manually associate project later

### Git Not Available
- Skip branch detection
- Show info message in task panel
- Task still functional for organization

### GitHub CLI Not Available
- Skip PR details fetching
- Show message: "Install GitHub CLI (`gh`) for PR integration"
- Branch detection still works via `git branch`

### Multiple Projects per Task
- All associated projects visible in focus mode
- Branch detection runs on first project (or user-selected)
- PR details from primary project

### PR Not Found
- Store PR number anyway (user typed it for a reason)
- Show "PR #123 (not found)" in task panel
- Offer to open search URL in browser

### Task Hierarchy Cycles
- Prevent setting parent that would create cycle
- Validation in TaskService.UpdateTask()

---

## Testing Strategy

### Unit Tests
- TaskService CRUD operations
- Task state transitions
- Focus mode toggling
- GitPrService title parsing
- Branch pattern matching

### Integration Tests
- Task persistence to config.json
- Focus mode tab filtering
- PR details fetching (mocked gh CLI)

### Manual Testing Scenarios
1. Quick task capture workflow
2. Start task → complete task → next task
3. Focus mode with multiple projects
4. PR linking for GitHub repo
5. Note capture → convert to task
6. Subtask creation and completion
7. App restart with focus mode active

---

## Future Enhancements (Post-MVP)

### Time Tracking
- Track time spent on each task
- Show duration in completed tasks
- Daily/weekly time reports

### Task Templates
- Predefined task templates (e.g., "PR Review", "Bug Fix")
- Auto-set projects, tags, description structure

### Integration with Issue Trackers
- Sync tasks with GitHub Issues
- Sync with Jira, Linear, etc.
- Two-way sync (complete in app → close issue)

### Task Sharing
- Export task list
- Import tasks from file
- Team task board (separate service)

### Smart Suggestions
- Suggest related tasks when creating new one
- Auto-suggest project based on task title
- Suggest completion when PR merged

---

## File Summary

### New Files (17)

| File | Purpose |
|------|---------|
| `Domain/FocusTask.cs` | Task model |
| `Domain/FocusTaskStatus.cs` | Status enum |
| `Domain/QuickNote.cs` | Note model |
| `Domain/FocusModeState.cs` | Focus state |
| `Domain/GitPrDetails.cs` | PR details model |
| `Services/ITaskService.cs` | Task service interface |
| `Services/TaskService.cs` | Task service implementation |
| `Services/IGitPrService.cs` | PR service interface |
| `Services/GitPrService.cs` | PR service implementation |
| `ViewModels/TaskPanelViewModel.cs` | Task panel VM |
| `ViewModels/QuickTaskViewModel.cs` | Quick task VM |
| `ViewModels/QuickNoteViewModel.cs` | Quick note VM |
| `Views/Popups/TaskPanelView.xaml` | Task panel UI |
| `Views/Popups/TaskPanelView.xaml.cs` | Task panel code-behind |
| `Views/Popups/QuickTaskView.xaml` | Quick task UI |
| `Views/Popups/QuickNoteView.xaml` | Quick note UI |

### Modified Files (6)

| File | Changes |
|------|---------|
| `Domain/AppConfiguration.cs` | Add FocusMode, Tasks, QuickNotes |
| `ViewModels/MainViewModel.cs` | Focus mode tab filtering, task state |
| `MainWindow.xaml` | Focus mode indicator |
| `MainWindow.xaml.cs` | Keyboard shortcuts for tasks |
| `Views/TabStrip.xaml` | Task indicator |
| `Views/Popups/HelpView.xaml` | Add task shortcuts |

---

*Document Version: 1.0*
*Created: 2025-12-17*
