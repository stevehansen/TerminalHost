# PRD: Claude Code Task System Integration

## Status
**In Progress** - Core infrastructure complete, Timeline integration in progress

### Implementation Status
- ✅ **Phase 1 Complete**: Core detection service and infrastructure (Tasks 1-5)
  - Created `IClaudeTaskDetectionService` interface
  - Implemented terminal output parsing with regex patterns
  - Extended `FocusTask` domain model with Claude-specific properties
  - Wired up terminal output hooks (both WPF and Avalonia)
  - Integrated with TaskPanelViewModel (Avalonia only)

- ✅ **Phase 2 Complete**: Dedicated panel prototype (Task 8 - Avalonia only)
  - Created `ClaudeTasksPanelViewModel` with real-time updates
  - Built UI with Claude blue theme and progress indicators
  - Keyboard shortcut: Ctrl+Shift+K

- 🔄 **Phase 3 In Progress**: Timeline integration (Option C)
  - **Decision**: Integrate Claude task detection into Timeline view instead of standalone panels
  - **Rationale**: WPF removed Task Panel in favor of Timeline (commit 5e0355c7)
  - Target: Both WPF and Avalonia Timeline views

- ⏳ **Phase 4 Pending**: Activity indicators and taskbar integration
  - Sidebar workspace activity indicator (Avalonia)
  - Windows taskbar glow (WPF)

## Overview

Integrate Claude Code's task management system with TerminalHost's **Timeline** to provide:
1. **Manual Tasks**: User-created tasks via TerminalHost UI (existing `FocusTask` system)
2. **Claude Tasks**: Tasks created by Claude Code CLI during active sessions (new integration)
3. **Unified Timeline View**: See what Claude worked on, when, and for how long in the visual timeline

This enables developers to track AI-assisted development progress through the Timeline's visual history rather than a separate task panel.

---

## Problem Statement

Currently, TerminalHost has a comprehensive task management system (`FocusTask`, Ctrl+T), but when Claude Code CLI is running in the Custom terminal:
- Users cannot see what tasks Claude is creating/working on
- No visibility into Claude's multi-step workflows
- Activity indicator only shows "terminal is active" but not what Claude is doing
- No integration between Claude's internal task tracking and TerminalHost's task panel

**Goal**: Bridge the gap between Claude Code's task system and TerminalHost's UI to provide transparency and progress tracking.

---

## Background: How Claude Code Uses Tasks

Based on Claude Code documentation and API analysis, Claude uses three main task tools:

### 1. TaskCreate
Creates a new task with:
- **subject**: Brief title (imperative form, e.g., "Fix authentication bug")
- **description**: Detailed requirements
- **activeForm**: Present continuous form for spinner display (e.g., "Fixing authentication bug")
- **metadata**: Optional arbitrary key-value pairs

Returns a unique **taskId** for subsequent operations.

### 2. TaskUpdate
Updates task properties:
- **status**: `pending` → `in_progress` → `completed` (or `deleted`)
- **owner**: Assign to agent (for multi-agent scenarios)
- **addBlocks/addBlockedBy**: Task dependencies
- **subject/description/activeForm**: Update details

### 3. TaskList
Retrieves all tasks with summary info:
- id, subject, status, owner, blockedBy
- Use `TaskGet` for full details

### Task Lifecycle Example
```
1. Claude: TaskCreate("Fix login bug", "Users cannot login...")
   → taskId: "task-abc123"

2. Claude: TaskUpdate(taskId: "task-abc123", status: "in_progress")
   → User sees: "Fixing login bug..." spinner

3. [Claude does work...]

4. Claude: TaskUpdate(taskId: "task-abc123", status: "completed")
   → Task marked done
```

---

## How to Detect Claude Tasks from TerminalHost

### Option A: Parse Terminal Output (Immediate, No CLI Changes)
**Approach**: Intercept terminal output and detect task-related messages from Claude.

**Detection Patterns**:
- Look for Claude's task creation messages: "Let me create a task..." or task status updates
- Parse structured output if Claude outputs task info in a predictable format
- Use regex/pattern matching on terminal text

**Pros**:
- No changes to Claude CLI required
- Works immediately with current Claude Code versions
- Can parse existing sessions retroactively

**Cons**:
- Fragile: Depends on Claude's output format (may change)
- Incomplete: May miss task details if Claude doesn't output them
- Performance: Regex parsing on every terminal output line

**Implementation**:
- Hook into `TerminalSession.OutputReceived` event (already exists for activity tracking)
- Add `ClaudeTaskDetectionService` to parse output and extract task info
- Create shadow `FocusTask` entries for detected Claude tasks

### Option B: Direct Claude CLI Integration (Future, More Robust)
**Approach**: Extend Claude CLI with a flag like `--task-output-file` to write task events to JSON.

**Example**: `claude --task-output-file ~/.claude/tasks.jsonl`

Output format (JSONL - one JSON object per line):
```jsonl
{"event":"task_created","taskId":"task-123","subject":"Fix bug","description":"...", "activeForm":"Fixing bug","timestamp":"2026-01-27T10:00:00Z"}
{"event":"task_updated","taskId":"task-123","status":"in_progress","timestamp":"2026-01-27T10:00:05Z"}
{"event":"task_updated","taskId":"task-123","status":"completed","timestamp":"2026-01-27T10:05:00Z"}
```

**Pros**:
- Reliable and structured
- Full task details available
- Easy to parse and process
- Can replay task history

**Cons**:
- Requires Claude CLI changes (may not be available)
- Needs file watcher for real-time updates

**Implementation**:
- Add `--task-output-file` to Claude CLI (upstream change)
- Create `ClaudeTaskFileWatcher` service (similar to `ClaudeCommandService` file watching)
- Parse JSONL file and create/update `FocusTask` entries
- Use `FileSystemWatcher` for real-time updates

### Option C: API/IPC Integration (Ideal, Long-term)
**Approach**: Claude CLI exposes task events via named pipe, Unix socket, or HTTP webhook.

**Example**: `claude --task-webhook http://localhost:9876/tasks`

**Pros**:
- Real-time, low latency
- Structured data
- Bidirectional (TerminalHost could send tasks to Claude)

**Cons**:
- Requires significant Claude CLI changes
- Complex implementation

**Recommendation**: Start with **Option A** (terminal output parsing) for immediate prototype, then evaluate **Option B** if Claude CLI adds support.

---

## Existing TerminalHost Architecture

### Current Task System (`FocusTask`)
**Location**: `src/TerminalHost.Core/Domain/FocusTask.cs`

**Features**:
- Hierarchical tasks (parent/child)
- Status: `NotStarted`, `InProgress`, `Completed`, `Deferred`
- Time tracking with start/elapsed time
- Priority, tags, notes
- Project path association (filter by project)
- PR/branch linking for GitHub integration

**Service**: `ITaskService` (`src/TerminalHost.Core/Interfaces/ITaskService.cs`)
- CRUD operations: `AddTask()`, `UpdateTask()`, `DeleteTask()`
- State transitions: `StartTask()`, `CompleteTask()`, `PauseTask()`, `DeferTask()`
- Focus mode filtering
- Persistence via `ConfigurationService` (JSON config)

**UI**: Task Panel (Ctrl+T)
- **ViewModel**: `TaskPanelViewModel.cs`
- **View**: `TaskPanelView.xaml` (Avalonia) or `TaskPanelView.xaml` (WPF)
- Collections: `BacklogTasks`, `CompletedTodayTasks`, `QuickNotes`
- Edit mode, subtask creation, search/filter

### Activity Indicator System
**Location**: `src/TerminalHost.Avalonia/Domain/TerminalSession.cs`

**Mechanism**:
- `IsActive` property: `true` if terminal output received within last **3 seconds**
- `LastOutputTime` timestamp tracking
- `ActivityChanged` event fires on transitions
- `SuppressActivityBriefly()` for false positive prevention

**UI Integration**: `TerminalPairTabViewModel.cs`
- `IsCustomTerminalActive`, `IsShellTerminalActive`, `IsRunTerminalActive`
- Tab spinner animates when terminal is active
- `HasUnreadActivity` flag for background activity

---

## Functional Requirements

### 1. Claude Task Detection
**Requirement**: Automatically detect Claude tasks from Claude Code CLI running in Custom terminal.

**Implementation** (Option A - Terminal Parsing):
1. **Service**: Create `ClaudeTaskDetectionService`
   - Interface: `IClaudeTaskDetectionService`
   - Location: `src/TerminalHost.Core/Services/ClaudeTaskDetectionService.cs`

2. **Detection Logic**:
   - Hook into `TerminalSession.OutputReceived` event for Custom terminal
   - Parse output lines for task patterns:
     - Task creation: Look for "Creating task:", "New task:", etc.
     - Status updates: "Task in progress:", "Task completed:", etc.
     - Use regex patterns for structured extraction

3. **Task Mapping**:
   - Map Claude task fields to `FocusTask`:
     - `subject` → `Title`
     - `description` → `Description`
     - `activeForm` → `Notes` or custom field
     - `status` → `Status` (map `pending`→`NotStarted`, `in_progress`→`InProgress`, `completed`→`Completed`)

4. **Metadata**:
   - Tag Claude-created tasks: `IsClaudeTask = true`
   - Store Claude task ID: `ClaudeTaskId = "task-abc123"`
   - Mark as read-only (users cannot edit Claude tasks)

### 2. Task Panel Integration
**Requirement**: Display Claude tasks alongside manual tasks in Task Panel (Ctrl+T).

**UI Changes**:
1. **Add Claude Task Section**:
   - New collection in `TaskPanelViewModel`: `ClaudeTasks` (ObservableCollection)
   - Display in separate section: "Claude Code Tasks" (above or below Backlog)

2. **Visual Distinction**:
   - Icon: 🤖 or Claude logo for Claude tasks
   - Color: Different background/border color (e.g., subtle blue tint)
   - Label: "AI Task" badge

3. **Task Details**:
   - Show subject, description, status
   - Display activeForm when status is `InProgress` (e.g., "Fixing authentication bug...")
   - Show elapsed time if available

4. **Read-Only Mode**:
   - Disable edit/delete buttons for Claude tasks
   - Show info tooltip: "This task is managed by Claude Code"

### 3. Activity Indicator Enhancement
**Requirement**: Show what Claude is working on in the activity indicator/tab.

**Current**: Tab shows spinning indicator when terminal is active (generic).

**Proposed Enhancement**:
1. **Tab Tooltip Enhancement**:
   - When Claude task is in progress, show task subject in tooltip
   - Example: "🤖 Fixing authentication bug..." (instead of just "Active")

2. **Status Bar Integration** (Optional):
   - Add status bar at bottom of window (if not exists)
   - Show current Claude task: "🤖 Claude: Fixing authentication bug..."
   - Progress indicator if task count available: "🤖 Claude: Task 2/5 - Writing tests..."

3. **Task Progress Overlay** (Future):
   - Small overlay panel (similar to toast) showing active Claude task
   - Auto-hide when task completes
   - Click to open Task Panel

**Implementation**:
- Add `CurrentClaudeTask` property to `TerminalPairTabViewModel`
- Update when Claude task status changes to `InProgress`
- Clear when task completes
- Use in tab tooltip and status bar

### 4. Task Lifecycle Sync
**Requirement**: Keep Claude tasks in sync with Claude's internal state.

**Challenges**:
- **Terminal restart**: Claude tasks may persist across terminal restarts (if using file-based tracking)
- **Multiple Claude sessions**: User might run multiple Claude instances in different tabs
- **Task cleanup**: Old completed tasks should be cleaned up

**Solutions**:
1. **Session Association**:
   - Associate Claude tasks with specific `TerminalSession` instance
   - When terminal exits, mark all associated Claude tasks as `Abandoned` or delete them

2. **Task Expiry**:
   - Auto-delete completed Claude tasks after 1 hour (or when session ends)
   - Keep in history for session summary

3. **Duplicate Prevention**:
   - Use Claude task ID to prevent duplicates
   - If same task ID detected, update existing task instead of creating new one

### 5. Integration with Existing Features
**Requirement**: Claude tasks should integrate with existing TerminalHost features.

**Integrations**:
1. **Timeline IDE**:
   - Link Claude tasks to `ClaudeSession` in Timeline
   - Show task list in session details

2. **GitHub Integration**:
   - If Claude task mentions PR number, auto-link to PR
   - Update PR status when task completes

3. **Focus Mode**:
   - Claude tasks visible in Focus Mode
   - Filter by current project path

---

## Non-Functional Requirements

### Performance
- Terminal output parsing must not slow down terminal rendering
- Use background thread for parsing (avoid UI thread blocking)
- Regex patterns must be efficient (compile once, reuse)

### Reliability
- Handle malformed Claude output gracefully (partial matches)
- Recover from parsing errors without crashing
- Log parse failures for debugging

### User Experience
- Clear visual distinction between manual and Claude tasks
- Non-intrusive: Don't steal focus or show modal dialogs
- Provide user control: Settings toggle to enable/disable Claude task detection

### Testability
- Unit tests for `ClaudeTaskDetectionService` with sample output
- Mock terminal output for testing
- Verify task creation, updates, and cleanup

---

## Technical Implementation Plan

### Phase 1: Core Task Detection (Option A - Terminal Parsing)

#### 1.1 Create Service Interface
**File**: `src/TerminalHost.Core/Interfaces/IClaudeTaskDetectionService.cs`

```csharp
public interface IClaudeTaskDetectionService
{
    /// <summary>
    /// Start monitoring terminal output for Claude tasks
    /// </summary>
    void StartMonitoring(TerminalSession session);

    /// <summary>
    /// Stop monitoring terminal output
    /// </summary>
    void StopMonitoring(TerminalSession session);

    /// <summary>
    /// Process a line of terminal output for task detection
    /// </summary>
    void ProcessOutput(string line, TerminalSession session);

    /// <summary>
    /// Get all detected Claude tasks for a session
    /// </summary>
    IReadOnlyList<FocusTask> GetClaudeTasks(TerminalSession session);

    /// <summary>
    /// Event fired when a Claude task is detected or updated
    /// </summary>
    event EventHandler<ClaudeTaskEventArgs>? ClaudeTaskChanged;
}

public class ClaudeTaskEventArgs : EventArgs
{
    public FocusTask Task { get; set; }
    public ClaudeTaskEventType EventType { get; set; } // Created, Updated, Completed
}
```

#### 1.2 Implement Detection Service
**File**: `src/TerminalHost.Core/Services/ClaudeTaskDetectionService.cs`

```csharp
public class ClaudeTaskDetectionService : IClaudeTaskDetectionService
{
    private readonly ITaskService _taskService;
    private readonly Dictionary<string, FocusTask> _claudeTasksById = new();
    private readonly Dictionary<TerminalSession, List<string>> _sessionTasks = new();

    // Regex patterns for detection (adjust based on actual Claude output)
    private static readonly Regex TaskCreatePattern = new Regex(
        @"(?:Creating|Starting) task:?\s+(?<subject>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex TaskProgressPattern = new Regex(
        @"(?<activeForm>.+?)\.\.\.",
        RegexOptions.Compiled
    );

    private static readonly Regex TaskCompletePattern = new Regex(
        @"(?:Completed|Finished|Done):?\s+(?<subject>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public void ProcessOutput(string line, TerminalSession session)
    {
        // Try task creation
        var createMatch = TaskCreatePattern.Match(line);
        if (createMatch.Success)
        {
            var subject = createMatch.Groups["subject"].Value.Trim();
            CreateClaudeTask(subject, session);
            return;
        }

        // Try task completion
        var completeMatch = TaskCompletePattern.Match(line);
        if (completeMatch.Success)
        {
            var subject = completeMatch.Groups["subject"].Value.Trim();
            CompleteClaudeTask(subject, session);
            return;
        }

        // Try in-progress detection
        var progressMatch = TaskProgressPattern.Match(line);
        if (progressMatch.Success)
        {
            var activeForm = progressMatch.Groups["activeForm"].Value.Trim();
            UpdateClaudeTaskProgress(activeForm, session);
        }
    }

    private void CreateClaudeTask(string subject, TerminalSession session)
    {
        var task = new FocusTask
        {
            Title = subject,
            Status = FocusTaskStatus.InProgress,
            IsClaudeTask = true, // New property
            ClaudeSessionId = session.Id, // Link to terminal session
            StartTime = DateTime.Now
        };

        _taskService.AddTask(task);
        // Track for this session
        if (!_sessionTasks.ContainsKey(session))
            _sessionTasks[session] = new List<string>();
        _sessionTasks[session].Add(task.Id);

        ClaudeTaskChanged?.Invoke(this, new ClaudeTaskEventArgs
        {
            Task = task,
            EventType = ClaudeTaskEventType.Created
        });
    }

    // ... (similar methods for CompleteClaudeTask, UpdateClaudeTaskProgress)
}
```

#### 1.3 Update FocusTask Domain Model
**File**: `src/TerminalHost.Core/Domain/FocusTask.cs`

Add properties:
```csharp
/// <summary>
/// Indicates if this task was created by Claude Code CLI
/// </summary>
public bool IsClaudeTask { get; set; }

/// <summary>
/// Claude task ID (e.g., "task-abc123") for tracking
/// </summary>
public string? ClaudeTaskId { get; set; }

/// <summary>
/// Terminal session ID that created this Claude task
/// </summary>
public string? ClaudeSessionId { get; set; }

/// <summary>
/// Active form text shown while task is in progress (e.g., "Fixing bug...")
/// </summary>
public string? ActiveForm { get; set; }
```

#### 1.4 Wire Up Terminal Output Hook
**File**: `src/TerminalHost.Avalonia/ViewModels/TerminalPairTabViewModel.cs`

In constructor or terminal setup:
```csharp
private void SetupClaudeTaskDetection()
{
    if (CustomTerminal != null)
    {
        _claudeTaskDetectionService.StartMonitoring(CustomTerminal);

        // Hook output event
        CustomTerminal.OutputReceived += (s, e) =>
        {
            _claudeTaskDetectionService.ProcessOutput(e.Text, CustomTerminal);
        };
    }
}

// Cleanup on terminal exit
private void OnCustomTerminalExited()
{
    _claudeTaskDetectionService.StopMonitoring(CustomTerminal);
    // Clean up Claude tasks for this session
    var tasks = _claudeTaskDetectionService.GetClaudeTasks(CustomTerminal);
    foreach (var task in tasks)
    {
        _taskService.DeleteTask(task.Id);
    }
}
```

### Phase 2: UI Integration

#### 2.1 Update TaskPanelViewModel
**File**: `src/TerminalHost.Avalonia/ViewModels/TaskPanelViewModel.cs`

Add:
```csharp
/// <summary>
/// Claude tasks detected from active terminal sessions
/// </summary>
public ObservableCollection<FocusTask> ClaudeTasks { get; } = new();

/// <summary>
/// Currently active Claude task (in progress)
/// </summary>
[ObservableProperty]
private FocusTask? _currentClaudeTask;

private void OnClaudeTaskChanged(object? sender, ClaudeTaskEventArgs e)
{
    // Update ClaudeTasks collection
    var existing = ClaudeTasks.FirstOrDefault(t => t.Id == e.Task.Id);
    if (e.EventType == ClaudeTaskEventType.Created && existing == null)
    {
        ClaudeTasks.Add(e.Task);
    }
    else if (e.EventType == ClaudeTaskEventType.Updated && existing != null)
    {
        // Update properties
        var index = ClaudeTasks.IndexOf(existing);
        ClaudeTasks[index] = e.Task;
    }
    else if (e.EventType == ClaudeTaskEventType.Completed && existing != null)
    {
        ClaudeTasks.Remove(existing);
    }

    // Update current task
    CurrentClaudeTask = ClaudeTasks.FirstOrDefault(t => t.Status == FocusTaskStatus.InProgress);
}
```

#### 2.2 Update Task Panel View
**File**: `src/TerminalHost.Avalonia/Views/TaskPanelView.axaml`

Add Claude tasks section:
```xml
<!-- Claude Code Tasks Section -->
<Border IsVisible="{Binding ClaudeTasks.Count, Converter={StaticResource GreaterThanZeroConverter}}"
        Background="#1A1E90FF"
        CornerRadius="8"
        Padding="12"
        Margin="0,0,0,12">
    <StackPanel>
        <TextBlock Text="🤖 Claude Code Tasks"
                   FontWeight="SemiBold"
                   FontSize="14"
                   Margin="0,0,0,8"/>

        <ItemsControl ItemsSource="{Binding ClaudeTasks}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Background="#0A1E90FF"
                            CornerRadius="6"
                            Padding="8"
                            Margin="0,4">
                        <StackPanel>
                            <TextBlock Text="{Binding Title}"
                                       FontWeight="Medium"
                                       TextWrapping="Wrap"/>
                            <TextBlock Text="{Binding ActiveForm}"
                                       IsVisible="{Binding ActiveForm, Converter={StaticResource NotNullConverter}}"
                                       FontStyle="Italic"
                                       Opacity="0.7"
                                       Margin="0,4,0,0"/>
                            <TextBlock Text="{Binding StatusIcon}"
                                       FontSize="12"
                                       Margin="0,4,0,0"/>
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```

#### 2.3 Update Tab Activity Indicator
**File**: `src/TerminalHost.Avalonia/ViewModels/TerminalPairTabViewModel.cs`

Update tab title/tooltip:
```csharp
private string GetTabTooltip()
{
    if (IsCustomTerminalActive && _currentClaudeTask != null)
    {
        return $"🤖 Claude: {_currentClaudeTask.ActiveForm ?? _currentClaudeTask.Title}";
    }

    if (IsCustomTerminalActive)
        return "Custom terminal is active";
    if (IsShellTerminalActive)
        return "Shell terminal is active";
    if (IsRunTerminalActive)
        return "Run terminal is active";

    return $"Project: {WorkingDirectory}";
}
```

### Phase 3: Settings & Configuration

#### 3.1 Add Settings
**File**: `src/TerminalHost.Core/Domain/AppSettings.cs`

```csharp
/// <summary>
/// Enable Claude task detection from terminal output
/// </summary>
public bool EnableClaudeTaskDetection { get; set; } = true;

/// <summary>
/// Auto-delete completed Claude tasks after this duration (minutes)
/// </summary>
public int ClaudeTaskRetentionMinutes { get; set; } = 60;

/// <summary>
/// Show Claude task overlay when task starts
/// </summary>
public bool ShowClaudeTaskOverlay { get; set; } = false;
```

#### 3.2 Settings UI
Add toggle in Settings view (Ctrl+,) under "Task Management" section.

---

## Timeline Integration (Option C)

### Overview
Instead of creating standalone task panels, integrate Claude task detection directly into the **Timeline** view. This provides a unified historical view of:
- When Claude sessions occurred
- What tasks Claude worked on during each session
- How long each task took
- Visual timeline of AI-assisted development

### Architecture Decision
**Rationale for Timeline Integration:**
1. **WPF removed Task Panel** (commit 5e0355c7) in favor of Timeline
2. Timeline already tracks Claude sessions (`ClaudeSession` domain model)
3. Timeline provides better historical context than a live task panel
4. Aligns with "session-centric" rather than "task-centric" workflow
5. Both platforms (WPF and Avalonia) have Timeline implementation

### Implementation Plan

#### 4.1 Extend ClaudeSession Domain Model
**File**: `src/TerminalHost.Core/Domain/ClaudeSession.cs`

```csharp
public class ClaudeSession
{
    // Existing properties
    public string Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string ProjectPath { get; set; }
    public string IntentId { get; set; }

    // NEW: Task tracking
    [JsonPropertyName("tasks")]
    public List<ClaudeTaskSnapshot> Tasks { get; set; } = new();

    [JsonPropertyName("taskCount")]
    public int TaskCount => Tasks.Count;

    [JsonPropertyName("completedTaskCount")]
    public int CompletedTaskCount => Tasks.Count(t => t.Status == FocusTaskStatus.Completed);
}

public class ClaudeTaskSnapshot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("activeForm")]
    public string? ActiveForm { get; set; }

    [JsonPropertyName("status")]
    public FocusTaskStatus Status { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("elapsed")]
    public TimeSpan? Elapsed => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}
```

#### 4.2 Update TimelineService
**File**: `src/TerminalHost.Core/Services/TimelineService.cs` (or platform-specific)

Add method to associate Claude tasks with sessions:

```csharp
public void AddTaskToSession(string sessionId, FocusTask task)
{
    var session = GetSession(sessionId);
    if (session == null) return;

    var snapshot = new ClaudeTaskSnapshot
    {
        Id = task.Id,
        Title = task.Title,
        ActiveForm = task.ActiveForm,
        Status = task.Status,
        StartedAt = task.StartedAt,
        CompletedAt = task.CompletedAt
    };

    session.Tasks.Add(snapshot);
    SaveSession(session);
}
```

#### 4.3 Wire Up ClaudeTaskDetectionService
When a Claude task is detected:
1. Find the current active `ClaudeSession` for this terminal/project
2. Add the task to the session's task list
3. Update Timeline UI to show task progress

**Integration in TerminalPairTabViewModel:**

```csharp
private void OnClaudeTaskChanged(object? sender, ClaudeTaskEventArgs e)
{
    // Find current Claude session
    var currentSession = _timelineService.GetActiveClaudeSession(Pair.WorkingDirectory);

    if (currentSession != null)
    {
        _timelineService.AddTaskToSession(currentSession.Id, e.Task);
    }
}
```

#### 4.4 Update Timeline UI
**Files**:
- `src/TerminalHost/TerminalHost/Views/TimelineView.xaml` (WPF)
- `src/TerminalHost.Avalonia/Views/TimelineModeView.axaml` (Avalonia)

**Visual Enhancements:**

1. **Session Block Tooltips**: Show task count and status
   ```xml
   <ToolTip.Tip>
       Claude Session: {SessionTitle}
       Duration: {Duration}
       Tasks: {CompletedTaskCount}/{TaskCount} completed
       - Task 1: Fix authentication bug (✓ 5m 23s)
       - Task 2: Add unit tests (✓ 3m 45s)
   </ToolTip.Tip>
   ```

2. **Task Timeline Bars**: Show individual task durations within session block
   - Each task gets a colored sub-bar
   - Hover shows task title and elapsed time
   - Click to expand task details

3. **Session Detail Panel**: When session is selected, show:
   - Task list with status indicators
   - Task start/end times relative to session
   - Task progress bars
   - Link to FocusTask if it was created from Claude task

**ViewModel Changes:**

```csharp
// SessionBlockViewModel.cs
public ObservableCollection<ClaudeTaskViewModel> Tasks { get; }
public string TaskSummary => $"{CompletedTaskCount}/{TaskCount} tasks completed";
public bool HasTasks => TaskCount > 0;

// ClaudeTaskViewModel.cs (new)
public class ClaudeTaskViewModel : ObservableObject
{
    public string Title { get; set; }
    public string? ActiveForm { get; set; }
    public FocusTaskStatus Status { get; set; }
    public TimeSpan? Elapsed { get; set; }
    public double ProgressPercent { get; set; }
    public string StatusIcon => Status switch
    {
        FocusTaskStatus.InProgress => "⏳",
        FocusTaskStatus.Completed => "✓",
        _ => "○"
    };
}
```

#### 4.5 Real-Time Updates
When Claude is actively working:
1. Timeline session block updates in real-time
2. Task bars animate as tasks progress
3. Session tooltip shows current task's `activeForm`
4. Timeline scrolls to keep active session visible

### Benefits of Timeline Integration
1. **Historical Context**: See what Claude did when, not just current tasks
2. **Session Correlation**: Tasks tied to specific Claude sessions
3. **Progress Visualization**: Timeline bars show task duration
4. **Unified View**: No separate panel to manage
5. **Cross-Platform**: Works on both WPF and Avalonia
6. **Persistence**: Tasks saved as part of session history

### Migration Path
1. Keep `ClaudeTasksPanelViewModel` (Avalonia) as optional live view
2. Make Timeline the primary interface for task history
3. Link between live panel and Timeline:
   - "View in Timeline" button in Claude Tasks Panel
   - Timeline highlights current session when panel is open

---

## Testing Strategy

### Unit Tests
**File**: `tests/TerminalHost.Tests/Services/ClaudeTaskDetectionServiceTests.cs`

```csharp
public class ClaudeTaskDetectionServiceTests
{
    [Fact]
    public void ProcessOutput_TaskCreation_CreatesTask()
    {
        var service = new ClaudeTaskDetectionService(mockTaskService);
        var session = new TerminalSession();

        service.ProcessOutput("Creating task: Fix authentication bug", session);

        var tasks = service.GetClaudeTasks(session);
        Assert.Single(tasks);
        Assert.Equal("Fix authentication bug", tasks[0].Title);
    }

    [Fact]
    public void ProcessOutput_TaskCompletion_CompletesTask()
    {
        // ... (test completion detection)
    }

    [Fact]
    public void ProcessOutput_MalformedInput_DoesNotCrash()
    {
        // ... (test error handling)
    }
}
```

### Integration Tests
- Start Claude CLI in test terminal
- Verify tasks are detected and displayed in UI
- Test terminal restart behavior
- Test multiple concurrent Claude sessions

### Manual Testing
1. Open TerminalHost
2. Run `claude` in Custom terminal
3. Give Claude a multi-step task
4. Verify tasks appear in Task Panel (Ctrl+T)
5. Verify tab tooltip shows current task
6. Verify tasks clean up when terminal exits

---

## Future Enhancements

### Phase 4: Advanced Features (Post-MVP)

1. **Bidirectional Task Sync**:
   - Create tasks in TerminalHost and send to Claude
   - User clicks "Ask Claude to implement" → creates Claude task

2. **Task History**:
   - Keep history of completed Claude tasks
   - Show in Timeline IDE

3. **Task Analytics**:
   - Track Claude task completion rate
   - Average time per task type
   - Show in Statistics view

4. **Task Templates**:
   - Detect common task patterns
   - Suggest templates for frequent workflows

5. **Multi-Agent Support**:
   - Support multiple Claude instances
   - Show which agent owns which task
   - Task handoff between agents

---

## Success Metrics

1. **Visibility**: Users can see Claude's current task within 1 second of task creation
2. **Accuracy**: 95%+ detection rate for Claude tasks (measure via test suite)
3. **Performance**: No noticeable terminal lag (<10ms overhead per output line)
4. **Reliability**: No crashes or hangs due to malformed output
5. **Usability**: Users can distinguish Claude tasks from manual tasks at a glance

---

## Dependencies

### Required Services
- `ITaskService` (existing)
- `TerminalSession` (existing)
- `IConfigurationService` (existing)

### New Services
- `IClaudeTaskDetectionService` (new)

### UI Components
- Task Panel (existing, needs updates)
- Tab tooltip (existing, needs updates)
- Status bar (optional, new)

---

## Documentation Updates Required

1. **CLAUDE.md**: Add to "Current Implementation Status" under "Completed Features"
2. **SHORTCUTS.md**: No new shortcuts (uses existing Ctrl+T)
3. **README.md**: Add brief mention in features list
4. **docs/specs/**: This spec file
5. **Code comments**: Document regex patterns and detection logic

---

## Open Questions

1. **Claude Output Format**: What exact output does Claude Code produce for tasks?
   - **Action**: Test with real Claude CLI to capture output samples
   - **Risk**: High - detection patterns depend on this

2. **Multi-Session Handling**: How to handle multiple Claude instances?
   - **Proposal**: Associate tasks with specific `TerminalSession` ID
   - **Risk**: Medium - need to test with concurrent sessions

3. **Task Cleanup Policy**: When to delete old Claude tasks?
   - **Proposal**: Delete on terminal exit + 1-hour retention for completed
   - **Risk**: Low - can adjust based on user feedback

4. **UI Placement**: Where should Claude tasks appear in Task Panel?
   - **Proposal**: Separate section at top (most visible)
   - **Alternative**: Mixed with manual tasks (with icon distinction)
   - **Decision**: Prototype both and user test

5. **Integration with Timeline IDE**: Should Claude tasks auto-link to `ClaudeSession`?
   - **Proposal**: Yes, link via session ID
   - **Benefit**: Complete workflow tracking
   - **Risk**: Low - Timeline already tracks sessions

---

## Appendix: Example Output Samples

### Sample Claude CLI Output (Hypothetical)
```
$ claude
Claude Code (v1.0.0)

I'll help you with that. Let me break this down into tasks:

Creating task: Fix authentication bug
  Description: Users report login failures on Safari browsers

Creating task: Add unit tests for auth service
  Description: Cover edge cases discovered during bug fix

Starting task 1 of 2: Fixing authentication bug...

[terminal output...]

Task completed: Fix authentication bug

Starting task 2 of 2: Adding unit tests for auth service...

[terminal output...]

Task completed: Add unit tests for auth service

All tasks finished! Summary:
- Fixed authentication bug (5m 23s)
- Added unit tests for auth service (3m 45s)
```

### Parsed Task Events
```
Event 1: TaskCreated
  - Subject: "Fix authentication bug"
  - Description: "Users report login failures on Safari browsers"
  - Status: NotStarted

Event 2: TaskCreated
  - Subject: "Add unit tests for auth service"
  - Description: "Cover edge cases discovered during bug fix"
  - Status: NotStarted

Event 3: TaskUpdated
  - TaskId: 1
  - Status: InProgress
  - ActiveForm: "Fixing authentication bug"

Event 4: TaskUpdated
  - TaskId: 1
  - Status: Completed
  - ElapsedTime: 5m 23s

Event 5: TaskUpdated
  - TaskId: 2
  - Status: InProgress
  - ActiveForm: "Adding unit tests for auth service"

Event 6: TaskUpdated
  - TaskId: 2
  - Status: Completed
  - ElapsedTime: 3m 45s
```

---

## References

- **Claude Code Documentation**: [claude.ai/code](https://claude.ai/code)
- **Claude API Task Tools**: TaskCreate, TaskUpdate, TaskList, TaskGet
- **TerminalHost Task System**: `FocusTask`, `ITaskService`, Task Panel (Ctrl+T)
- **Activity Indicators**: `TerminalSession.IsActive`, tab spinners
- **Timeline IDE**: `ClaudeSession`, `Intent` (future integration)
