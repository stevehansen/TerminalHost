# Workspace Sidebar & Git Worktree Support

TerminalHost supports an alternative **Workspace Sidebar** layout mode (Ctrl+L) that organizes projects in a left sidebar tree with integrated git worktree management.

## Key Features

- **Project Tree**: All open projects listed in a collapsible sidebar with activity indicators and git status.
- **Git Worktree Support**: Worktrees are displayed as children of their parent project. Create, switch, and manage worktrees directly from the UI.
- **Layout Toggle**: Seamlessly switch between Tabs and Sidebar modes (Ctrl+L). Sidebar can be toggled independently (Ctrl+Shift+L).
- **Playground Section**: Dedicated area for temporary projects and quick experiments with auto-cleanup support.
- **Active Ports Bar**: Real-time detection of listening ports from run terminals with one-click access to localhost URLs.

## UI Components

- **WORKSPACES Header**: Add new projects, toggle path visibility, and manage overall workspace.
- **Project Entry**: Shows current branch, ahead/behind status, and activity spinner.
- **Worktree Entry**: Branch icon with name and activity indicator.
- **Context Menu**: Open in Explorer/VS Code, New Worktree, Manage Worktrees, etc.

## Git Worktree Integration

The app uses `git worktree` commands to manage parallel development directories from a single repository.

### Operations
- **Create Worktree**: Dialog (Ctrl+Alt+N) to pick branch and target location.
- **Manage Worktrees**: Centralized view for listing, locking, and removing worktrees.
- **Auto-detection**: Automatically scans for and displays linked worktrees for any added project.

## Configuration

```json
{
  "settings": {
    "layoutMode": "Tabs",
    "sidebarWidth": 250,
    "sidebarCollapsed": false
  }
}
```

## Planned Enhancements
- **Playground Templates**: Common project scaffolds (Node.js, .NET, etc.).
- **Auto-cleanup**: Optional automatic deletion for old playground projects.
- **Active Ports Refinement**: More robust process name detection.