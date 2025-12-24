# PRD: Workspace Sidebar Layout with Git Worktree Support

This document describes a new layout mode where projects are organized in a left sidebar with integrated git worktree management, similar to tools like Cldy (shown in reference screenshot).

## Overview

### Current State

The application uses a **tab-based layout** where:
- Each project is a tab in the top tab bar
- Tabs can be reordered, closed, and searched
- No visual hierarchy between projects
- Git worktrees are not managed within the app

### Proposed State

Add an alternative **workspace sidebar layout** where:
- Projects are listed in a collapsible tree in the left sidebar
- Each project shows its worktrees as children
- Quick terminal/worktree creation from dropdown menu
- Active ports displayed in status bar
- Optional "Playground" section for experiments

## Goals

1. **Workspace Organization**: Visual hierarchy for projects and their worktrees
2. **Worktree Management**: Create, switch, and manage git worktrees from the UI
3. **Quick Access**: Fast switching between projects without hunting through tabs
4. **Active Development View**: See all active ports/servers at a glance
5. **Backward Compatibility**: Keep existing tab layout as an option

## UI Design

### Layout Comparison

**Current (Tab Layout):**
```
┌────────────────────────────────────────────────────────────────────┐
│ [Tab1] [Tab2] [Tab3] [Tab4] [+]                                    │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│                        Terminal Content                            │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Proposed (Workspace Sidebar Layout):**
```
┌──────────────────┬─────────────────────────────────────────────────┐
│ WORKSPACES  [+]  │ project-name                        [⚙] │
│                  ├─────────────────────────────────────────────────┤
│ ▼ □ CLImanger    │                                                 │
│    ↳ main        │                                                 │
│   ▼ 🔀 feature/  │                Terminal Content                 │
│      ↳ Main      │                                                 │
│     > design     │                                                 │
│     > logic/login│                                                 │
│                  │                                                 │
│ ▼ □ product-ctr  │                                                 │
│    ↳ main        │                                                 │
│   > server part  │                                                 │
│   > watermark    │                                                 │
│                  │                                                 │
│ ▼ □ PorterX-App  │                                                 │
│    ↳ main        │                                                 │
│   > Main         │                                                 │
│                  │                                                 │
│ ─────────────────│                                                 │
│ PLAYGROUND       │                                                 │
│ □ Playground     │                                                 │
│   > Main         │                                                 │
│                  │                                                 │
│ [+ New Playground]│                                                │
├──────────────────┴─────────────────────────────────────────────────┤
│ 🔌 Active Ports: ○ 3000-9000  ○ 3000  ○ 5000  ○ 5432  ○ 7000      │
└────────────────────────────────────────────────────────────────────┘
```

### Sidebar Components

#### 1. Workspace Header
```
WORKSPACES  [+] [📁]
```
- **WORKSPACES**: Section title
- **[+]**: Add new workspace (open folder dialog)
- **[📁]**: Toggle showing full paths vs folder names only

#### 2. Project Entry
```
▼ □ project-name
   ↳ main  ● (2↑ 1↓)
```
- **▼/▶**: Expand/collapse worktrees
- **□**: Project icon (folder or custom)
- **project-name**: Directory name
- **↳ main**: Current branch indicator with git icon
- **●**: Activity indicator (terminal producing output)
- **(2↑ 1↓)**: Ahead/behind remote count

#### 3. Worktree Entry
```
   > server-part
   > feature/login ●
```
- **>**: Worktree indicator (branch icon)
- **worktree-name**: Branch or descriptive name
- **●**: Activity indicator for this worktree's terminal

#### 4. Context Menu (Right-click project)
```
┌─────────────────────────┐
│ Open in Explorer        │
│ Open in VS Code         │
│ ───────────────────────│
│ New Terminal       ▶   │
│ New Worktree...         │
│ Manage Worktrees...     │
│ ───────────────────────│
│ Close Project           │
│ Remove from Workspace   │
└─────────────────────────┘
```

#### 5. New Terminal Dropdown (+ button on project)
```
┌─────────────────────────┐
│ NEW TERMINAL            │
│ ↳ Plain Terminal        │
│ 🤖 claudecode           │
│ 🔀 New Worktree         │
│ ⚙️ Create git worktree  │
└─────────────────────────┘
```

#### 6. Active Ports Bar
```
🔌 Active Ports: ○ 3000-9000  ○ 3000 □ solhun  ○ 5000  ○ 5432 □ postgres  ○ 7000
```
- Click port to open in browser (if HTTP)
- Shows process name if detectable
- Collapsible/expandable
- Auto-detects from run terminal output

### Sidebar Behavior

| Action | Behavior |
|--------|----------|
| Click project | Select project, show its main terminal |
| Click worktree | Switch to that worktree's terminal |
| Double-click project | Toggle expand/collapse |
| Drag project | Reorder in list |
| Drag worktree | Cannot reorder (git managed) |
| Right-click | Show context menu |
| Hover | Show full path tooltip |

## Git Worktree Integration

### What are Git Worktrees?

Git worktrees allow multiple working directories from a single repository. Each worktree has its own branch checked out, enabling parallel development without stashing changes.

```bash
# Main repo at /projects/my-app (on main branch)
# Worktree at /projects/my-app-feature (on feature/login branch)
# Worktree at /projects/my-app-hotfix (on hotfix/urgent branch)
```

### Worktree Operations

#### List Worktrees
```bash
git worktree list
# Output:
# /path/to/repo         abc1234 [main]
# /path/to/repo-feature def5678 [feature/login]
# /path/to/repo-hotfix  789abcd [hotfix/urgent]
```

#### Create Worktree
```bash
# Create from existing branch
git worktree add ../my-app-feature feature/login

# Create with new branch
git worktree add -b feature/new ../my-app-new
```

#### Remove Worktree
```bash
git worktree remove ../my-app-feature
```

### UI for Worktree Management

#### Create Worktree Dialog
```
┌─────────────────────────────────────────────┐
│ Create Git Worktree                      [×]│
├─────────────────────────────────────────────┤
│                                             │
│ Branch:                                     │
│ ┌─────────────────────────────────────────┐│
│ │ feature/new-feature              [▼]    ││
│ └─────────────────────────────────────────┘│
│ □ Create new branch                         │
│                                             │
│ Location:                                   │
│ ┌─────────────────────────────────────────┐│
│ │ P:\projects\my-app-feature     [Browse] ││
│ └─────────────────────────────────────────┘│
│ ℹ️ Default: parent folder + branch name     │
│                                             │
│ □ Open in TerminalHost after creation       │
│                                             │
│                    [Cancel] [Create]        │
└─────────────────────────────────────────────┘
```

#### Manage Worktrees Panel
```
┌─────────────────────────────────────────────┐
│ Manage Worktrees: my-app                 [×]│
├─────────────────────────────────────────────┤
│                                             │
│ 📁 Main worktree                            │
│    P:\projects\my-app                       │
│    Branch: main                             │
│    [Open] [Copy Path]                       │
│                                             │
│ 🔀 Linked worktrees                         │
│                                             │
│ ┌─────────────────────────────────────────┐│
│ │ feature/login                           ││
│ │ P:\projects\my-app-login                ││
│ │ [Open] [Remove] [Copy Path]             ││
│ └─────────────────────────────────────────┘│
│                                             │
│ ┌─────────────────────────────────────────┐│
│ │ hotfix/urgent                           ││
│ │ P:\projects\my-app-hotfix               ││
│ │ [Open] [Remove] [Copy Path]             ││
│ └─────────────────────────────────────────┘│
│                                             │
│                         [+ New Worktree]    │
└─────────────────────────────────────────────┘
```

## Playground Section

A separate section for quick experiments without polluting the main workspace.

### Features

- **Temporary projects**: Quick scratch terminals
- **Auto-cleanup option**: Delete after X days of inactivity
- **Templates**: Common project scaffolds
- **Isolation**: Separate from main work

### Configuration

```json
{
  "playground": {
    "enabled": true,
    "basePath": "P:\\Playground",
    "autoCleanupDays": 30,
    "templates": [
      { "name": "Node.js", "command": "npm init -y" },
      { "name": ".NET", "command": "dotnet new console" },
      { "name": "Python", "command": "python -m venv venv" }
    ]
  }
}
```

## Active Ports Detection

### Implementation

Monitor network activity from run terminals to detect listening ports:

```csharp
// Use netstat or Windows API to detect listening ports
// Filter by process ID of terminals
// Display in status bar with optional labels
```

### Configuration

```json
{
  "activePorts": {
    "showInStatusBar": true,
    "portRange": "3000-9000",
    "knownPorts": {
      "5432": "PostgreSQL",
      "6379": "Redis",
      "27017": "MongoDB"
    }
  }
}
```

## Configuration Schema

### Layout Mode Setting

```json
{
  "settings": {
    "layoutMode": "Tabs",  // "Tabs" | "WorkspaceSidebar"
    "sidebarWidth": 250,
    "sidebarCollapsed": false
  }
}
```

### Workspace Data

```json
{
  "workspaces": [
    {
      "id": "ws-123",
      "name": "CLImanger",
      "path": "P:\\projects\\CLImanger",
      "section": "main",  // "main" | "playground"
      "worktrees": [
        {
          "path": "P:\\projects\\CLImanger",
          "branch": "main",
          "isMain": true
        },
        {
          "path": "P:\\projects\\CLImanger-feature",
          "branch": "feature/design",
          "isMain": false
        }
      ]
    }
  ],
  "playgroundPath": "P:\\Playground",
  "playgrounds": [
    {
      "id": "pg-456",
      "name": "Quick Test",
      "path": "P:\\Playground\\quick-test",
      "createdAt": "2025-12-24T00:00:00Z"
    }
  ]
}
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+` | Toggle sidebar visibility |
| Ctrl+1-9 | Jump to workspace by index |
| Ctrl+Shift+W | Focus workspace sidebar |
| Ctrl+Alt+N | New worktree for current project |
| Ctrl+Shift+P | New playground |

## Implementation Phases

### Phase 1: Core Sidebar UI
1. Create `WorkspaceSidebarView.xaml` with tree structure
2. Create `WorkspaceSidebarViewModel` with project/worktree management
3. Add layout mode toggle in settings
4. Implement sidebar expand/collapse
5. Wire up keyboard shortcuts

### Phase 2: Worktree Integration
1. Create `GitWorktreeService` for worktree operations
2. Add worktree detection on project load
3. Create worktree dialog UI
4. Implement worktree creation/removal
5. Auto-refresh worktree list on git operations

### Phase 3: Active Ports & Playground
1. Create `PortDetectionService` for network monitoring
2. Add active ports status bar
3. Implement playground section
4. Add playground templates
5. Auto-cleanup for old playgrounds

### Phase 4: Polish
1. Drag-drop reordering
2. Search/filter in sidebar
3. Custom icons per project
4. Workspace groups/folders
5. Import/export workspace configuration

## Domain Model

### New Classes

```csharp
public class Workspace
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string Section { get; set; }  // "main" | "playground"
    public ObservableCollection<WorktreeInfo> Worktrees { get; set; }
    public bool IsExpanded { get; set; }
    public string? CustomIcon { get; set; }
}

public class WorktreeInfo
{
    public string Path { get; set; }
    public string Branch { get; set; }
    public bool IsMain { get; set; }
    public bool HasActivity { get; set; }
    public GitStatus? Status { get; set; }
}

public class ActivePort
{
    public int Port { get; set; }
    public string? ProcessName { get; set; }
    public string? Label { get; set; }  // User-defined or from knownPorts
    public bool IsHttp { get; set; }
}
```

### Services

```csharp
public interface IGitWorktreeService
{
    Task<IReadOnlyList<WorktreeInfo>> ListWorktreesAsync(string repoPath);
    Task<WorktreeInfo> CreateWorktreeAsync(string repoPath, string branch, string targetPath, bool createBranch = false);
    Task RemoveWorktreeAsync(string worktreePath, bool force = false);
    Task<bool> IsWorktreeAsync(string path);
}

public interface IPortDetectionService
{
    IObservable<IReadOnlyList<ActivePort>> ActivePorts { get; }
    void StartMonitoring(IEnumerable<int> processIds);
    void StopMonitoring();
}
```

## Migration Path

### Existing Users

1. **Opt-in**: Workspace sidebar is off by default
2. **Preserve tabs**: Existing tabs converted to workspace entries
3. **No data loss**: Both modes share same terminal pairs
4. **Easy switch**: Toggle in settings or command palette

### CLI Behavior

```bash
# Tab mode (current behavior)
host .                    # Opens tab for current directory

# Workspace mode
host .                    # Adds to workspace sidebar
host --workspace P:\Path  # Explicitly add to workspace
host --playground         # Create in playground section
```

## Success Criteria

1. Users can switch between tab and sidebar layouts
2. Worktrees are automatically detected and displayed
3. Creating/removing worktrees works reliably
4. Active ports are detected and clickable
5. Playground section provides quick experimentation
6. Performance remains good with many workspaces (50+)

## Future Considerations

- **Workspace sync**: Sync workspace configuration across machines
- **Team workspaces**: Share workspace configurations via git
- **Remote workspaces**: SSH/WSL workspace support
- **Workspace templates**: Predefined multi-project setups
- **Workspace search**: Global search across all workspace files

---

*Document Version: 1.0*
*Created: 2025-12-24*
