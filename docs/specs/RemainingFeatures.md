# PRD: Remaining Features Roadmap

This document consolidates remaining features from various PRDs into a single tracking document.

## Current Implementation Status

### Workspace Sidebar (WorkspaceLayout.md)

**Already Implemented:**
- `WorkspaceSidebarView` and `WorkspaceSidebarViewModel`
- `GitWorktreeService` with full API (list, create, remove, prune, lock, unlock)
- Layout mode toggle (Ctrl+L) between Tabs and Sidebar
- Workspaces and Playgrounds sections
- Context menu (Open, Duplicate, Close, Move Up/Down, Git actions)
- Auto-sort by usage with toggle
- Multi-folder opening
- Git status display (branch, ahead/behind)
- Activity indicators
- Create Worktree Dialog (Ctrl+Alt+N)
- Manage Worktrees Panel (right-click → Manage Worktrees...)

**Remaining:**
1. Active Ports Detection
2. ~~Manage Worktrees Panel~~ ✅ Implemented
3. Playground Templates
4. Auto-cleanup for old playgrounds

### Git Advanced (GitAdvanced.md)

**Already Implemented:**
- Commit History Viewer (Ctrl+H)
- Interactive Staging (file-level)
- Commit Creation UI
- Stash Operations (Ctrl+Shift+S)
- File History & Blame (Ctrl+Shift+B)
- Reflog Access (Ctrl+Shift+G) - View reflog, checkout, create branch from ref
- Cherry-pick from Commit History - Right-click commit → Cherry-pick
- Revert from Commit History - Right-click commit → Revert

**Remaining:**
1. Submodule Support
2. Merge Conflict Resolution
3. Tags Management
4. Commit/Branch Comparison

---

## Feature Specifications

### 1. Active Ports Detection (Medium Priority)

Detect and display active listening ports from terminal processes.

#### Implementation Approach

```csharp
public interface IPortDetectionService
{
    /// <summary>
    /// Observable stream of currently active ports.
    /// </summary>
    IObservable<IReadOnlyList<ActivePort>> ActivePorts { get; }

    /// <summary>
    /// Start monitoring ports for the given process IDs.
    /// </summary>
    void StartMonitoring(IEnumerable<int> processIds);

    /// <summary>
    /// Stop monitoring.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Get current active ports synchronously.
    /// </summary>
    IReadOnlyList<ActivePort> GetActivePorts();
}

public record ActivePort(
    int Port,
    string? ProcessName,
    string? Label,      // User-defined or from knownPorts config
    bool IsHttp,        // True for ports 80, 443, 3000-9999
    int ProcessId
);
```

#### Detection Method

**Option A: netstat parsing (simpler, cross-platform compatible)**
```bash
netstat -ano | findstr LISTENING
# Output: TCP    0.0.0.0:3000    0.0.0.0:0    LISTENING    12345
```

**Option B: Windows API (more reliable, Windows-only)**
- Use `GetExtendedTcpTable` from `iphlpapi.dll`
- Filter by process ID and state (LISTENING)

#### UI Integration

1. **Status Bar**: Show active ports at bottom of main window
   ```
   🔌 Active Ports: ○ 3000  ○ 5000  ○ 5432 (PostgreSQL)
   ```

2. **Click Action**: Open `http://localhost:{port}` in browser (if HTTP)

3. **Tooltip**: Show process name and full address

#### Configuration

```json
{
  "settings": {
    "showActivePorts": true,
    "httpPortRange": "3000-9999",
    "knownPorts": {
      "5432": "PostgreSQL",
      "6379": "Redis",
      "27017": "MongoDB",
      "3306": "MySQL",
      "1433": "SQL Server"
    }
  }
}
```

#### Implementation Steps

1. Create `IPortDetectionService` interface in Core
2. Implement `PortDetectionService` using netstat parsing
3. Add `ActivePortsView` UserControl for status bar
4. Wire up monitoring when run terminals start/stop
5. Add configuration options in Settings

---

### 2. Git Worktree UI Improvements (Medium Priority)

#### 2.1 Create Worktree Dialog

When user clicks "Create Worktree" from context menu:

```
┌─────────────────────────────────────────────────────┐
│ Create Git Worktree                              [×]│
├─────────────────────────────────────────────────────┤
│                                                     │
│ Branch:                                             │
│ ┌─────────────────────────────────────────────────┐│
│ │ [search branches...]                        [▼] ││
│ └─────────────────────────────────────────────────┘│
│   ○ Use existing branch                             │
│   ● Create new branch                               │
│                                                     │
│ Location:                                           │
│ ┌─────────────────────────────────────────────────┐│
│ │ P:\projects\myapp-feature              [Browse] ││
│ └─────────────────────────────────────────────────┘│
│ ℹ️ Suggested: {parent-folder}\{repo}-{branch}       │
│                                                     │
│ ☑ Open in TerminalHost after creation               │
│                                                     │
│                          [Cancel]  [Create Worktree]│
└─────────────────────────────────────────────────────┘
```

**Implementation:**
- Create `CreateWorktreeDialog.xaml` and ViewModel
- Use existing `GitWorktreeService.CreateWorktreeAsync`
- Branch autocomplete from `GitStatusService.GetBranchesAsync`
- Auto-generate location based on branch name

#### 2.2 Manage Worktrees Panel

Shows all worktrees for a project with management actions:

```
┌─────────────────────────────────────────────────────┐
│ Worktrees: myapp                                 [×]│
├─────────────────────────────────────────────────────┤
│ 📁 Main Worktree                                    │
│    P:\projects\myapp                                │
│    Branch: main  •  ✓ Clean                         │
│    [Open]                                           │
│ ────────────────────────────────────────────────────│
│ 🔀 feature/login                                    │
│    P:\projects\myapp-login                          │
│    Branch: feature/login  •  2 modified             │
│    [Open]  [Remove]  [Lock]                         │
│ ────────────────────────────────────────────────────│
│ 🔀 hotfix/urgent (locked: WIP)                      │
│    P:\projects\myapp-hotfix                         │
│    Branch: hotfix/urgent  •  ✓ Clean                │
│    [Open]  [Remove]  [Unlock]                       │
├─────────────────────────────────────────────────────┤
│                                   [+ New Worktree]  │
└─────────────────────────────────────────────────────┘
```

---

### 3. Reflog Access (Low Priority)

Access git reflog to recover "lost" commits after reset, rebase, or other operations.

#### Access Method
- Command palette: "Git: Reflog"
- Could add to Commit History view as a tab/toggle

#### UI Design

```
┌─────────────────────────────────────────────────────┐
│ Git Reflog                           [Refresh] [×]  │
├─────────────────────────────────────────────────────┤
│ HEAD@{0}  abc1234  checkout: moving from main to... │
│           2 minutes ago                             │
│           [Checkout]  [Create Branch]               │
│ ────────────────────────────────────────────────────│
│ HEAD@{1}  def5678  commit: Add new feature          │
│           5 minutes ago                             │
│           [Checkout]  [Create Branch]  [View]       │
│ ────────────────────────────────────────────────────│
│ HEAD@{2}  789abcd  reset: moving to HEAD~3          │
│           10 minutes ago                            │
│           [Checkout]  [Create Branch]  [View]       │
│ ────────────────────────────────────────────────────│
│                               [Load More (50)]      │
└─────────────────────────────────────────────────────┘
```

#### Git Commands

| Operation | Command |
|-----------|---------|
| List reflog | `git reflog --format="%h|%gd|%gs|%ar" -n 50` |
| Checkout entry | `git checkout HEAD@{N}` |
| Create branch | `git branch <name> HEAD@{N}` |
| Show commit | `git show HEAD@{N}` |

#### Service Addition

```csharp
public interface IGitStatusService
{
    // Add to existing interface
    Task<IReadOnlyList<ReflogEntry>> GetReflogAsync(string repoPath, int count = 50);
}

public record ReflogEntry(
    string Hash,
    string Ref,           // e.g., "HEAD@{0}"
    string Action,        // e.g., "commit", "checkout", "reset"
    string Description,   // e.g., "moving from main to feature"
    string RelativeTime   // e.g., "2 minutes ago"
);
```

---

### 4. Submodule Support (Low Priority)

Display and manage git submodules within projects.

#### Features

1. **File Explorer Integration**
   - Show submodule folders with special icon (📦)
   - Badge showing submodule status (clean/dirty/uninitialized)
   - Tooltip with current commit and tracked commit

2. **Context Menu on Submodule**
   - Initialize (if not initialized)
   - Update to tracked commit
   - Update to latest (--remote)
   - Open as separate project

3. **Submodules Panel** (optional)
   - List all submodules with status
   - Batch operations (Update All, Init All)

#### Git Commands

| Operation | Command |
|-----------|---------|
| List submodules | `git submodule status` |
| Initialize | `git submodule init <path>` |
| Update | `git submodule update <path>` |
| Update to latest | `git submodule update --remote <path>` |
| Sync URLs | `git submodule sync` |

#### Detection

```bash
# Check for submodules
git submodule status
# Output format:
#  abc1234 path/to/submodule (v1.0.0)    <- initialized, clean
# -abc1234 path/to/submodule             <- not initialized
# +abc1234 path/to/submodule (v1.0.0)    <- modified
```

---

### 5. Cherry-pick UI (Low Priority)

Cherry-pick commits from history to current branch.

#### Access Method
- Right-click commit in Commit History → "Cherry-pick"
- Command palette: "Git: Cherry-pick..."

#### UI Flow

1. **From Commit History**: Right-click → Cherry-pick
   - Confirmation dialog with commit details
   - Options: --no-commit, --edit

2. **From Command Palette**: Opens commit picker
   - Search/filter commits
   - Select one or multiple commits
   - Shows preview of changes

3. **Conflict Handling**
   - Show conflict notification
   - Link to Git Changes panel to resolve
   - "Continue Cherry-pick" / "Abort Cherry-pick" buttons

#### Git Commands

| Operation | Command |
|-----------|---------|
| Cherry-pick | `git cherry-pick <hash>` |
| No commit | `git cherry-pick --no-commit <hash>` |
| Multiple | `git cherry-pick <hash1> <hash2>` |
| Continue | `git cherry-pick --continue` |
| Abort | `git cherry-pick --abort` |
| Status | `git status` (check for CHERRY_PICK_HEAD) |

---

### 6. Revert Commit UI (Low Priority)

Create a revert commit for any commit in history.

#### Access Method
- Right-click commit in Commit History → "Revert"
- Command palette: "Git: Revert Commit..."

#### UI Flow

Similar to Cherry-pick:
1. Select commit to revert
2. Preview what will change (inverse diff)
3. Confirm action
4. Handle conflicts if any

#### Git Commands

| Operation | Command |
|-----------|---------|
| Revert | `git revert <hash>` |
| No commit | `git revert --no-commit <hash>` |
| Continue | `git revert --continue` |
| Abort | `git revert --abort` |

---

### 7. Merge Conflict Resolution (Low Priority)

Visual three-way merge for resolving conflicts.

#### When to Show
- After merge with conflicts
- After rebase with conflicts
- After cherry-pick with conflicts
- After stash apply with conflicts

#### UI Design

```
┌─────────────────────────────────────────────────────────────────┐
│ Resolve Conflict: src/App.cs                    [Skip] [Abort] │
├───────────────────┬───────────────────┬─────────────────────────┤
│ Ours (HEAD)       │ Result            │ Theirs (feature/x)      │
├───────────────────┼───────────────────┼─────────────────────────┤
│ public void Foo() │ public void Foo() │ public void Foo()       │
│ {                 │ {                 │ {                       │
│   return 1;       │ <<<CONFLICT>>>    │   return 2;             │
│ }                 │ }                 │ }                       │
├───────────────────┴───────────────────┴─────────────────────────┤
│ [Accept Ours] [Accept Theirs] [Accept Both] [Mark as Resolved]  │
└─────────────────────────────────────────────────────────────────┘
```

#### Implementation Notes

- Parse conflict markers from file content
- Three-pane view with synchronized scrolling
- Quick actions per conflict region
- "Mark as Resolved" stages the file
- Track operation type (merge/rebase/cherry-pick) for continue/abort

---

## Implementation Priority

| Priority | Feature | Effort | Notes |
|----------|---------|--------|-------|
| **Medium** | Active Ports Detection | Medium | Most useful for dev workflow |
| ~~Medium~~ | ~~Create Worktree Dialog~~ | ~~Low~~ | ✅ Implemented |
| ~~Medium~~ | ~~Manage Worktrees Panel~~ | ~~Low~~ | ✅ Implemented |
| ~~Low~~ | ~~Reflog Access~~ | ~~Low~~ | ✅ Implemented |
| ~~Low~~ | ~~Submodule Support~~ | ~~Medium~~ | ✅ Implemented |
| ~~Low~~ | ~~Cherry-pick UI~~ | ~~Low~~ | ✅ Implemented |
| ~~Low~~ | ~~Revert Commit UI~~ | ~~Low~~ | ✅ Implemented |
| **Low** | Merge Conflict Resolution | High | Complex UI, can use external tools |
| **Low** | Playground Templates | Low | Nice to have |
| **Low** | Playground Auto-cleanup | Low | Nice to have |
| **Future** | Timeline IDE | Very High | Advanced mode - see TimelineIDE.md |

---

### 8. Timeline IDE (Future)

Advanced mode for visual timeline-based AI development. See full specification: [TimelineIDE.md](TimelineIDE.md)

#### Core Concepts
- **1 swimlane = 1 worktree = 1 intent**: Each feature/task gets isolated git worktree
- **Claude Code sessions as timeline blocks**: Visual representation of AI work sessions
- **Forking**: Branch from any session point to try alternative approaches
- **Intent context**: Custom context files loaded into every Claude Code session

#### Key Features
- Timeline view with Minutes/Hours/Days scale
- Intent sidebar with status, branch, activity indicators
- Session tracking: duration, files changed, commands, agent notes
- Fork from any session, cherry-pick between intents
- Focus time accumulator

#### Implementation Phases
1. Core Infrastructure (Intent/Session models, TimelineService, persistence)
2. Timeline UI (swimlanes, session blocks, time scale)
3. Session Tracking (monitor Claude Code, capture metadata, link to commits)
4. Advanced Features (fork, cherry-pick, context files, focus time)
5. Polish (keyboard nav, drag reorder, export/import)

---

*Document Version: 1.1*
*Created: 2025-12-26*
