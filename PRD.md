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
4. **Split view option** - View both terminals simultaneously (60/40 layout)
5. **Full terminal emulation** - ANSI colors, interactive CLIs, nerd font support

## Current Implementation Status

### Completed Features

- [x] WPF application with tabbed interface
- [x] Terminal pairs (custom command + shell) per directory
- [x] Default split view with 60/40 layout
- [x] Terminal switching via buttons or Ctrl+`
- [x] Split view toggle via button or Ctrl+\
- [x] Tab management (Ctrl+Tab, Ctrl+1-9, Ctrl+W)
- [x] New project via folder picker (Ctrl+N)
- [x] Single-instance with named pipe IPC
- [x] CLI support: `host .`, `host P:\Path`, `host --workdir P:\Path`
- [x] Duplicate detection (focuses existing tab for same directory)
- [x] Cascadia Code NF font with Campbell color scheme
- [x] Close confirmation for running terminals
- [x] JSON configuration in `%APPDATA%\TerminalHost\config.json`
- [x] Window state persistence (position, size, maximized)
- [x] Session persistence (open folders restored on startup)
- [x] Per-directory settings persistence (split ratio, active terminal)
- [x] Git repository status display (branch, dirty status, ahead/behind)
- [x] Quick commands with keyboard shortcuts
- [x] Terminal activity indicators (animated spinner in tabs)

### Deferred Features

- [ ] Custom profiles beyond the default pair
- [ ] Profile management UI

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

### TerminalPair

A paired set of terminals for a project directory.

| Property        | Type            | Description                              |
|-----------------|-----------------|------------------------------------------|
| WorkingDirectory| string          | Project directory path                   |
| CustomTerminal  | TerminalSession | Custom command terminal (e.g., Claude)   |
| ShellTerminal   | TerminalSession | Shell terminal (e.g., PowerShell)        |
| ActiveTerminal  | enum            | Which terminal is currently active       |
| DirectoryName   | string          | Display name (directory name only)       |

### TerminalSession

A running terminal instance.

| Property        | Type                | Description                         |
|-----------------|---------------------|-------------------------------------|
| Profile         | Profile             | Configuration for this terminal     |
| TerminalControl | EasyTerminalControl | The WPF terminal control instance   |
| State           | SessionState        | Running or Exited                   |
| IsActive        | bool                | True if producing output (last 2s)  |
| LastOutputTime  | DateTime?           | When output was last received       |

## Command Line Usage

```bash
# Open/focus app with no arguments
host

# Open project from current directory
host .

# Open project from specific path
host P:\MyProject

# Using named argument
host --workdir P:\MyProject
host -w P:\MyProject
```

If a project tab for the specified directory already exists, it will be focused instead of creating a new tab.

## Keyboard Shortcuts

| Shortcut         | Action                              |
|------------------|-------------------------------------|
| Ctrl+N           | Open new project (folder picker)    |
| Ctrl+PageDown    | Next tab                            |
| Ctrl+PageUp      | Previous tab                        |
| Ctrl+1-9         | Jump to specific tab                |
| Ctrl+W           | Close current tab                   |
| Ctrl+`           | Switch between Custom/Shell terminal|
| Ctrl+\           | Toggle split view                   |
| Ctrl+Shift+C     | Quick command: Commit (Claude Code) |
| Ctrl+Shift+D     | Quick command: Git Pull (Shell)     |
| Ctrl+Shift+U     | Quick command: Git Push (Shell)     |

## Configuration

Config file: `%APPDATA%\TerminalHost\config.json`

```json
{
  "profiles": [],
  "settings": {
    "confirmOnClose": true,
    "showInSystemTray": false,
    "customCommand": "C:\\Users\\Administrator\\.local\\bin\\claude.exe",
    "customCommandName": "Claude Code",
    "customCommandIcon": "🤖",
    "shellCommand": "pwsh.exe",
    "shellCommandName": "PowerShell",
    "shellCommandIcon": "💻"
  },
  "windowState": {
    "left": 100,
    "top": 100,
    "width": 1200,
    "height": 800,
    "isMaximized": false
  },
  "openFolders": [
    "P:\\Project1",
    "P:\\Project2"
  ],
  "directorySettings": {
    "p:\\project1": {
      "isSplitView": true,
      "splitRatio": 0.6,
      "activeTerminal": "Custom"
    }
  },
  "quickCommands": [
    {
      "id": "commit",
      "label": "Commit",
      "icon": "💾",
      "text": "commit",
      "target": "Custom",
      "appendNewline": true,
      "useUserInput": true,
      "shortcut": "Ctrl+Shift+C"
    },
    {
      "id": "git-pull",
      "label": "Pull",
      "icon": "↓",
      "text": "git pull",
      "target": "Shell",
      "appendNewline": true,
      "shortcut": "Ctrl+Shift+D"
    },
    {
      "id": "git-push",
      "label": "Push",
      "icon": "↑",
      "text": "git push",
      "target": "Shell",
      "appendNewline": true,
      "shortcut": "Ctrl+Shift+U"
    }
  ]
}
```

## Technical Implementation

### Technology Stack

- **Framework**: WPF on .NET 8
- **Terminal Control**: EasyWindowsTerminalControl (NuGet)
- **MVVM**: CommunityToolkit.Mvvm
- **Configuration**: JSON in `%APPDATA%\TerminalHost\`
- **Single Instance**: Mutex + named pipe IPC

### Terminal Control Configuration

- **Font**: Cascadia Code NF (for nerd font glyph support)
- **Theme**: Campbell color scheme (Windows Terminal default)
- **Process startup**: RestartTerm() called after control loads into visual tree

### Git Status Display

For git repositories, the application displays:
- **Tab title**: Directory name with branch and status (e.g., `MyProject [main *]`)
- **Status bar**: Full status with branch icon (e.g., `🌿 main • 2↑ 1↓ • modified`)

Status indicators:
- `*` or `modified` - Uncommitted changes present
- `↑N` - Commits ahead of remote
- `↓N` - Commits behind remote

Status refreshes automatically every 5 seconds for the active tab.

### Terminal Activity Indicators

Tabs display an animated spinning indicator (◌) when terminals are actively producing output:
- **Active state**: Spinner visible and rotating when output received within last 2 seconds
- **Idle state**: Spinner hidden when no output for 2+ seconds

This helps users see at a glance which tabs have terminals doing work vs waiting for input.

**Implementation:**
- Uses `ConPTYTerm.InterceptOutputToUITerminal` delegate to track output timing
- Activity state checked every 1 second to detect idle transitions
- Events fire immediately when transitioning from idle to active

### Quick Commands

Quick commands provide one-click buttons and keyboard shortcuts for common terminal operations. They appear in the status bar and can send text to either the custom terminal (Claude Code) or shell terminal.

**Default Commands:**
| Button | Shortcut       | Action                           |
|--------|----------------|----------------------------------|
| 💾     | Ctrl+Shift+C   | Send "commit" to Claude Code     |
| ↓      | Ctrl+Shift+D   | Run `git pull` in Shell          |
| ↑      | Ctrl+Shift+U   | Run `git push` in Shell          |

**QuickCommand Properties:**
| Property      | Type   | Description                                      |
|---------------|--------|--------------------------------------------------|
| id            | string | Unique identifier                                |
| label         | string | Display label for tooltip                        |
| icon          | string | Button display (emoji/symbol)                    |
| text          | string | Text to send to terminal                         |
| target        | enum   | "Custom" or "Shell"                              |
| appendNewline | bool   | Whether to append newline after text             |
| useUserInput  | bool   | Use internal key events (for raw mode apps)      |
| shortcut      | string | Keyboard shortcut (e.g., "Ctrl+Shift+C")         |

**Notes:**
- `useUserInput: true` is required for Claude Code to properly receive Enter key
- Shell commands work with standard `appendNewline: true`
- Shortcuts support Ctrl, Alt, Shift modifiers with any letter/number key

### Project Structure

```
TerminalHost/
├── TerminalHost.sln
└── src/TerminalHost/TerminalHost/
    ├── App.xaml(.cs)           # Application entry, single instance handling
    ├── MainWindow.xaml(.cs)    # Main window with tab bar and terminal content
    ├── Converters.cs           # XAML value converters
    ├── Domain/
    │   ├── Profile.cs          # Configuration template for terminal sessions
    │   ├── TerminalSession.cs  # Running terminal instance
    │   ├── TerminalPair.cs     # Paired custom + shell terminals
    │   ├── SessionState.cs     # Running/Exited enum
    │   ├── AppConfiguration.cs # Root config with settings
    │   ├── GitStatus.cs        # Git repository status model
    │   └── QuickCommand.cs     # Quick command definition with shortcut
    ├── Services/
    │   ├── ConfigurationService.cs   # JSON config load/save
    │   ├── ProfileRegistry.cs        # Profile and settings management
    │   ├── SessionManager.cs         # Session lifecycle tracking
    │   ├── SingleInstanceService.cs  # Mutex + named pipe IPC
    │   ├── TerminalControlFactory.cs # Creates configured terminal controls
    │   └── GitStatusService.cs       # Git command execution and parsing
    └── ViewModels/
        ├── MainViewModel.cs              # Main window logic
        └── TerminalPairTabViewModel.cs   # Tab with paired terminals
```

## Future Considerations

Items for future development:

- **Custom profile pairs**: Different command pairs for different project types
- **Drag-and-drop tabs**: Reorder tabs
- **SSH profiles**: Built-in SSH connection support
- **Multiple custom commands**: More than one custom command per pair

## Success Criteria

The application is successful when:

1. User can run `host .` and get a terminal pair for the current directory
2. User can switch between Claude Code and shell instantly
3. Split view shows both terminals with proper nerd font rendering
4. Running `host .` twice focuses the existing tab
5. All keyboard shortcuts work as documented
6. Configuration persists across restarts

---

*Document Version: 2.0*
*Last Updated: 2025-12-11*
