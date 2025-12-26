# Product Requirements Document: TerminalHost

## Overview

**TerminalHost** (executable: `host.exe`) is a WPF desktop application that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal (PowerShell), allowing easy switching between them without termination.

## Problem Statement

Developers working with AI coding assistants like Claude Code need to:
- Run the AI assistant in a project directory
- Quickly switch to a shell for manual commands (git, npm, etc.)
- Return to the AI assistant without losing context

Current solutions require multiple terminal windows or tabs that must be manually configured and navigated. We need an application that pairs terminals per project directory, making it effortless to switch between AI assistant and shell.

## Goals

1. **Directory-centric terminal pairs** - Each project directory gets a paired custom + shell terminal
2. **Single-instance with CLI** - `host .` opens/focuses a terminal pair for current directory
3. **Easy terminal switching** - Toggle between custom and shell without termination
4. **Always-on split view** - Both terminals visible simultaneously (60/40 default layout, adjustable via splitter)
5. **Full terminal emulation** - ANSI colors, interactive CLIs, nerd font support

## Current Implementation Status

### Completed Features

- [x] **Core Terminal Pairing**: Custom command + Shell terminal per project with 60/40 split view.
- [x] **Tab Management**: Ctrl+Tab, Ctrl+1-9, Ctrl+W, drag-and-drop reordering, middle-click to close.
- [x] **CLI & Single Instance**: `host .` support with named pipe IPC and duplicate tab detection.
- [x] **Terminal Features**: ANSI colors, Interactive CLI support, Nerd Font (Cascadia Code NF), Activity indicators.
- [x] **Settings & Persistence**: Form-based (Rich) and JSON (Raw) settings editor (Ctrl+,), window/session state persistence.
- [x] **Git Integration**: Status display, Branch switcher (Ctrl+B), Changes panel with diff (Ctrl+G), Stash manager (Ctrl+Shift+S).
- [x] **File Tools**: File explorer panel (Ctrl+Shift+F), syntax-highlighted preview (Ctrl+O), built-in editor (Ctrl+Shift+E).
- [x] **Productivity**: Command palette (Ctrl+Shift+P), Tab switcher (Ctrl+Shift+T), Scratch pad (Ctrl+Shift+N).
- [x] **Project Runner**: F5 to run projects with auto-detection and dedicated run terminal.
- [x] **Task & Focus Mode**: Hierarchical tasks, time tracking, and PR integration (Ctrl+T).
- [x] **AI Assistant Support**: Multi-AI CLI support (Claude, Gemini, etc.) with per-project selection.
- [x] **GitHub Integration**: Dashboard (Ctrl+Shift+H), PR Review Mode (Ctrl+Shift+R).
- [x] **UI Enhancements**: Toast notifications, themed dialogs, system tray support, Markdown preview (Ctrl+M).
- [x] **Resilience**: Robust JSON persistence with automatic backups and thread-safe writes.

### Deferred Features

(None currently)

## Domain Model

```
┌─────────────────────────────────────────────────────────────┐
│                      TerminalHost                           │
│                                                             │
│  ┌─────────────────┐       ┌─────────────────────────────┐ │
│  │ ProfileRegistry │       │      SessionManager         │ │
│  │                 │       │                             │ │
│  │ - settings      │──────▶│ - activeSessions[]          │ │
│  │ - customCommand │       │ - trackSession(session)     │ │
│  │ - shellCommand  │       │ - closeSession(session)     │ │
│  └─────────────────┘       └─────────────────────────────┘ │
│          │                              │                   │
│          ▼                              ▼                   │
│  ┌─────────────────┐       ┌─────────────────────────────┐ │
│  │  TerminalPair   │       │      TerminalSession        │ │
│  │                 │       │                             │ │
│  │ - workingDir    │◀─────▶│ - profile                   │ │
│  │ - customTerminal│       │ - terminalControl           │ │
│  │ - shellTerminal │       │ - state (Running|Exited)    │ │
│  │ - activeTerminal│       │                             │ │
│  └─────────────────┘       └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Command Line Usage

```bash
# Open project from current directory
host .

# Open project from specific path
host P:\MyProject

# Launch the setup and dependency checker window
host /setup

# Advanced/Testing arguments
host --disable-single-instance  # Allow multiple instances
host --user-data-dir "C:\Path"  # Override configuration path
```

## Keyboard Shortcuts

| Shortcut         | Action                              |
|------------------|-------------------------------------|
| Ctrl+N           | Open new project (folder picker)    |
| Ctrl+,           | Open settings editor                |
| Ctrl+`           | Switch between Custom/Shell terminal|
| Ctrl+Shift+T     | Open tab switcher (search tabs)     |
| Ctrl+Shift+P     | Open command palette                |
| Ctrl+G / Ctrl+B  | Git Changes / Git Branch switcher   |
| Ctrl+T           | Open task panel (focus mode)        |
| F5 / F6          | Start project / Run tests           |
| Ctrl+Shift+F     | Toggle File Explorer panel          |
| Ctrl+Shift+H / R | GitHub Dashboard / PR Review Mode   |

## Configuration

Config file: `%APPDATA%\TerminalHost\config.json`

```json
{
  "profiles": [],
  "settings": {
    "confirmOnClose": true,
    "showInSystemTray": false,
    "customCommand": "claude.exe",
    "shellCommand": "pwsh.exe"
  },
  "openFolders": ["P:\\Project1"],
  "quickCommands": [
    {
      "id": "commit",
      "label": "Commit",
      "icon": "💾",
      "text": "commit",
      "target": "Custom",
      "shortcut": "Ctrl+Shift+C"
    }
  ]
}
```

## Planned Features

Detailed specifications for planned features are documented in separate files in `docs/specs/`:

- **[Advanced Git Features](docs/specs/GitAdvanced.md)**: Interactive staging, submodule support, merge conflict resolution.
- **[Search & Productivity](docs/specs/SearchAndProductivity.md)**: Terminal output search, snippet manager.
- **[Workspace Sidebar](docs/specs/WorkspaceLayout.md)**: Tree-based project navigation and git worktree management.

### Implementation Priority Summary

| Priority | Feature | Specification |
|----------|---------|--------------|
| **Medium** | Active Ports Detection | docs/specs/RemainingFeatures.md |
| **Medium** | Manage Worktrees Panel | docs/specs/RemainingFeatures.md |
| **Medium** | Terminal Search | docs/specs/SearchAndProductivity.md |
| **Low** | Submodule Support | docs/specs/RemainingFeatures.md |
| **Low** | Merge Conflict Resolution | docs/specs/RemainingFeatures.md |

---

## Future Considerations

- **[Versioning & Auto-Updates](docs/specs/Versioning.md)**: Automatic updates via GitHub Releases.
- **[Unified Panel System](docs/specs/Panels.md)**: Dockable/floating panels for various tools.
- Custom Profile Pairs: Different command pairs for different project types.

## Success Criteria

1. User can run `host .` and get a terminal pair for the current directory.
2. User can switch between AI assistant and shell instantly.
3. Configuration and session state persist across restarts.
4. UI remains responsive and provides clear activity indicators.

---

*Document Version: 3.0*
*Last Updated: 2025-12-26*