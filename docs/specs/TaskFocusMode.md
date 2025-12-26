# Task/Focus Mode Specification

Task/Focus Mode is a task management system integrated into TerminalHost that helps developers organize their work by capturing tasks, linking them to git branches/PRs, and filtering the workspace to stay focused.

## Features

- **Quick capture**: Rapidly enqueue tasks with minimal details (Ctrl+Shift+Q) or notes (Ctrl+Shift+M).
- **Focus mode**: Constrain visible project tabs to only those relevant to the current task.
- **Branch/PR linking**: Auto-detect and link tasks to git branches and pull requests based on title patterns (e.g., "#123", "PR #123").
- **Meeting notes**: Lightweight scribbles that can be converted into proper tasks.
- **Task Hierarchy**: Organize work into parent/child relationships.

## User Stories

- **Quick Capture**: Single keyboard shortcut to add tasks/notes to backlog without breaking flow.
- **Focus Mode**: Hide unrelated tabs to reduce cognitive load during active work.
- **Context Integration**: Automatically fetch PR details and matching branches for task context.
- **Hierarchy**: Track prerequisites and subtasks for complex work.

## Data Model

- `FocusTask`: Core model containing title, status, notes, project associations, and PR/Branch links.
- `QuickNote`: Simple text snippet that can be promoted to a task.
- `FocusModeState`: Tracks if focus mode is active and what the current task is.

## UI Design

### Task Panel (Ctrl+T)
A central hub for managing the backlog, current task, quick notes, and completed work.

### Focus Mode Indicator
Visual indication in the status bar showing the current active task and quick actions (Complete, Pause, Exit Focus).

### Quick Capture Popups
Minimal overlays for rapid entry of tasks or thoughts.

## Configuration

Tasks and focus state are persisted in `config.json`:

```json
{
  "focusMode": {
    "isEnabled": false,
    "currentTaskId": "task-id"
  },
  "tasks": [
    {
      "id": "task-id",
      "title": "My Task",
      "status": "InProgress",
      "projectPaths": ["P:\\MyProject"]
    }
  ],
  "quickNotes": []
}
```

## Services

- `ITaskService`: Handles task lifecycle, focus mode state, and persistence.
- `IGitPrService`: Detects PR numbers in titles, matches local branches, and fetches PR metadata via GitHub CLI.