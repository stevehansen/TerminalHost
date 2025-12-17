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

- [x] WPF application with tabbed interface
- [x] Terminal pairs (custom command + shell) per directory
- [x] Always-on split view with 60/40 default layout (adjustable via splitter)
- [x] Terminal switching via buttons or Ctrl+`
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
- [x] Settings editor tab with Rich mode (form-based) and Raw mode (JSON) (Ctrl+,)
- [x] System tray support (minimize to tray, restore on click)
- [x] Profile management integrated into Settings (Ctrl+P)
- [x] Detected links button (scans terminal output for URLs, file paths, custom patterns)
- [x] File preview popup with syntax highlighting
- [x] Tab reordering via drag-and-drop
- [x] Middle-click to close tabs
- [x] Tab overflow handling (scroll arrows + dropdown when many tabs)
- [x] Tab switcher popup with search (Ctrl+Shift+T)
- [x] Help window with shortcuts and commands (F1)
- [x] Built-in quick commands (Explorer, Scratch Pad, Git Changes, Help) in status bar
- [x] CSV/TSV file preview with column colorization
- [x] File editor popup with save/reload (Ctrl+Shift+E)
- [x] Command palette for quick actions (Ctrl+Shift+P)
- [x] Scratch pad for per-project or global notes (Ctrl+Shift+N)
- [x] Git changes panel with file list and diff viewer (Ctrl+G)
- [x] Git branch management popup (Ctrl+B) - switch, create, delete branches
- [x] Project Runner - Run and manage development servers with F5, dedicated run terminal, URL detection
- [x] Setup Mode - A startup window to detect and guide installation of recommended dependencies.
- [x] Modular UI Architecture - MainWindow refactored into reusable components (TabStrip, TerminalPairView, popup views) with dedicated ViewModels for improved maintainability
- [x] Profile Launching - Launch custom profiles as standalone single-terminal tabs from Profiles UI, Command Palette, or keyboard shortcuts
- [x] **Robust JSON File Persistence with Backup:** Implemented a generic service (`JsonFileService`) for `.json` files (e.g., `config.json`, `stats.json`) that automatically creates a `.bak` file before overwriting and attempts to recover from the backup if the primary file is corrupted.
- [x] **Thread-Safe Configuration/Statistics Writes:** Ensured concurrent write access to `config.json` and `stats.json` is protected by `lock` primitives in `ConfigurationService` and `StatisticsService`, preventing data corruption from simultaneous save operations.
- [x] **File Explorer Panel (Ctrl+Shift+F):** Integrated file explorer as a toggleable right panel in terminal tabs with:
  - Tree view with lazy loading and file icons
  - Git status integration (badges M/A/D/? and row background tints)
  - File operations (create, rename, delete, copy path)
  - Terminal integration (cd to folder in shell)
  - Unified file viewer with preview/edit mode toggle
  - Pop-out support for multiple detached file viewers
  - Auto-refresh on file system changes
  - State persistence (visibility and split ratio per directory)
- [x] **Terminal Layout Modes:** Three-state layout toggle for terminal pair views:
  - **Custom Full Mode** - Custom terminal takes full width/height (Shell hidden but still running)
  - **Horizontal Split** (default) - Custom + Shell side by side
  - **Vertical Split** - Custom on top, Shell on bottom
  - Per-project layout mode persistence in `DirectorySettings`
  - Visual toggle buttons with grouped styling in toolbar
- [x] **Claude User Commands Detection:** Integration of Claude Code custom slash commands from `.md` files:
  - Scans `~/.claude/commands/*.md` for global commands
  - Scans `.claude/commands/*.md` for project-specific commands
  - Commands appear in Command Palette with "Claude: /{name}" prefix
  - Project commands override global commands with the same name
  - Live file watching for automatic updates when commands are added/removed
  - Optional keyboard shortcuts for commands (configurable in settings)
  - Execution sends `/{command-name}` to the Custom terminal
- [x] **Command Palette MRU Ordering:** Most recently used commands appear first in the Command Palette:
  - Tracks command usage when executed
  - Persists MRU list in config.json (up to 30 commands)
  - Commands not in MRU sorted alphabetically after MRU items
- [x] **Task/Focus Mode (Ctrl+T):** Task management system for organizing daily work:
  - Hierarchical task tree with parent/child relationships
  - Task statuses: NotStarted, InProgress, Completed, Deferred
  - Focus mode filters tabs to show only task-related projects
  - Quick notes that can be converted to tasks
  - Time tracking with elapsed time display
  - PR/Branch integration via GitHub CLI (`gh`)
  - Auto-detect PR numbers from task titles (`PR #123`, `#123`, `issues/123`)
  - Task panel UI with backlog, current task, and completed today sections
- [x] **Multiple AI Assistant Support:** Support for multiple AI CLI tools with per-project selection:
  - Built-in AI Assistants: Claude Code (default, enabled), Gemini CLI, OpenAI Codex, GitHub Copilot (disabled by default)
  - Per-project AI selection via toolbar dropdown
  - Settings UI for managing AI assistants (add/edit/remove/reorder)
  - Auto-detection of installed AI CLIs in Setup window
  - Immediate terminal restart when switching AI
  - Custom AI assistant support (user-defined command, name, icon)
  - Configuration:
    - Global: `aiAssistants[]` array in config.json with id, name, command, icon, detectionCommand, enabled, isDefault
    - Per-project: `directorySettings[path].activeAiAssistantId`
- [x] **GitHub Dashboard (Ctrl+Shift+H):** Centralized view of GitHub activity:
  - Review Requests: PRs where you're requested as reviewer (excludes drafts)
  - My PRs: Your open pull requests across all repositories
  - Issues: Issues assigned to you
  - Section sidebar with counts for each category
  - Open in browser and Checkout actions for PRs
  - Uses GitHub API via `gh api` for reliable data fetching (up to 100 items per section)
  - Auto-refresh capability with configurable interval
- [x] **Repository Quick Access (Ctrl+Shift+O):** Quick repository switcher popup:
  - Search/filter repositories by name
  - Shows open tabs, favorites, and GitHub repositories
  - Favorite toggle for quick access
  - Clone button for remote repositories
  - Status indicators (open, local, favorite)
- [x] **PR Review Mode (Ctrl+Shift+R):** Review PRs for the current branch:
  - File list with additions/deletions per file
  - Diff viewer for selected files
  - Review actions: Approve, Request Changes, Comment, Merge
  - Run tests integration
  - Opens PR details for the current branch's PR
- [x] **Quick Test Runner (F6):** Run project tests with visual results:
  - Auto-detects test framework from project type
  - Displays pass/fail/skip counts
  - Test tree view with status icons
  - Error details and stack traces for failed tests
  - Re-run failed tests option
- [x] **Markdown Preview (Ctrl+M):** Preview markdown files in a dedicated window:
  - Auto-finds README.md or prompts for file selection
  - Live preview with dark theme styling
  - Auto-reload on file changes (toggleable)
  - Resizable, draggable window with native controls
  - Refresh button (F5) for manual reload

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

# Launch the setup and dependency checker window
host /setup

# Advanced/Testing arguments
host --disable-single-instance  # Allow multiple instances (or -multi)
host --user-data-dir "C:\Path"  # Override configuration path (or -data)
```

If a project tab for the specified directory already exists, it will be focused instead of creating a new tab.

## Keyboard Shortcuts

| Shortcut         | Action                              |
|------------------|-------------------------------------|
| Ctrl+N           | Open new project (folder picker)    |
| Ctrl+,           | Open settings editor                |
| Ctrl+P           | Open settings (Profiles section)    |
| Ctrl+E           | Open current folder in Explorer     |
| Ctrl+O           | Open file preview dialog            |
| F1               | Show help window                    |
| Ctrl+Shift+T     | Open tab switcher (search tabs)     |
| Ctrl+PageDown    | Next tab                            |
| Ctrl+PageUp      | Previous tab                        |
| Ctrl+1-9         | Jump to specific tab                |
| Ctrl+W           | Close current tab                   |
| Middle-click     | Close tab under cursor              |
| Drag tab         | Reorder tabs                        |
| Ctrl+`           | Switch between Custom/Shell terminal|
| Ctrl+Shift+E     | Open file editor                    |
| Ctrl+Shift+P     | Open command palette                |
| Ctrl+Shift+N     | Open scratch pad (notes)            |
| Ctrl+G           | Open git changes panel              |
| Ctrl+B           | Open git branch switcher            |
| Ctrl+T           | Open task panel (focus mode)        |
| Ctrl+Shift+H     | Open GitHub Dashboard               |
| Ctrl+Shift+O     | Open Repository Quick Access        |
| Ctrl+Shift+R     | Open PR Review Mode                 |
| F6               | Run tests (Quick Test Runner)       |
| Ctrl+M           | Open Markdown Preview               |
| Ctrl+Shift+C     | Quick command: Commit (Claude Code) |
| Ctrl+Shift+V     | Quick command: Review (Claude Code) |
| Ctrl+Shift+D     | Quick command: Git Pull (Shell)     |
| Ctrl+Shift+U     | Quick command: Git Push (Shell)     |
| Ctrl+Shift+B     | Quick command: Dev Build (Shell)    |
| F5               | Start/Stop project run              |
| Shift+F5         | Force stop project run              |
| Links button     | View detected URLs and file paths from terminal output |

## Configuration

Config file: `%APPDATA%\TerminalHost\config.json`
(A backup file `config.json.bak` is automatically created and used for recovery in case of primary file corruption.)

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
      "text": "git pull --rebase",
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
  ],
  "linkPatterns": [
    {
      "id": "jira-ticket",
      "name": "JIRA Ticket",
      "pattern": "([A-Z]+-\\d+)",
      "urlTemplate": "https://jira.example.com/browse/$1",
      "enabled": true,
      "priority": 10
    }
  ],
  "scratchPads": {
    "p:\\project1": "Project-specific notes here..."
  },
  "globalScratchPad": "Global notes shared across all projects..."
}
```

## Technical Implementation

### Technology Stack

- **Framework**: WPF on .NET 8
- **Terminal Control**: EasyWindowsTerminalControl (NuGet)
- **MVVM**: CommunityToolkit.Mvvm
- **Configuration**: JSON in `%APPDATA%\TerminalHost\` (managed by `JsonFileService` for resilience and thread-safety)
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

Tabs display visual indicators to show terminal activity status with three states:

- **Active (yellow spinner ◐)**: Terminal producing output - spinner visible and rotating when output received within last 2 seconds
- **Completed (green dot ●)**: Activity stopped but tab hasn't been focused yet - indicates "something finished, check me"
- **Idle (no indicator)**: Tab has been focused since last activity, clearing the green indicator

This creates a simple workflow: yellow = working, green = done/unread, nothing = reviewed. Users can see at a glance which tabs have active terminals and which have completed work waiting to be reviewed.

**Implementation:**
- Uses `ConPTYTerm.InterceptOutputToUITerminal` delegate to track output timing
- Activity state checked every 1 second to detect idle transitions
- Events fire immediately when transitioning from idle to active
- `HasUnreadActivity` property tracks completed-but-unread state
- Selecting/focusing a tab clears its unread activity indicator

### Tab Management

Enhanced tab interaction features:

**Drag-and-Drop Reordering:**
- Drag tabs to reorder them in the tab bar
- Visual feedback shows drop position with blue highlight
- Tab order is persisted in `openFolders` config on save

**Middle-Click to Close:**
- Middle-click any tab to close it (same as clicking × button)
- Triggers close confirmation if terminals are running

**Tab Overflow:**
When many tabs are open and they overflow the tab bar:
- **Scroll arrows**: `‹` and `›` buttons appear to scroll through tabs
- **Dropdown button**: `▼` button shows a searchable list of all tabs
- Clicking a tab in the dropdown switches to that tab

**Tab Switcher (Ctrl+Shift+T):**
A centered popup for quickly finding and switching tabs:
- Opens with `Ctrl+Shift+T`
- Type to filter tabs by name or working directory
- Arrow keys to navigate, Enter to select
- Escape to cancel
- Shows tab icon, title, and full working directory path

### Quick Commands

Quick commands provide one-click buttons and keyboard shortcuts for common terminal operations. They appear in the status bar and can send text to either the custom terminal (Claude Code) or shell terminal.

**Default Commands:**

*Claude Code Commands:*
| Button | Shortcut       | Action                                              |
|--------|----------------|-----------------------------------------------------|
| 💾     | Ctrl+Shift+C   | Send "commit" to Claude Code                        |
| ⭐     | Ctrl+Shift+R   | Send "rate my code" to Claude Code                  |
| 🔍     | Ctrl+Shift+V   | Send "review the current PR" to Claude Code         |

*Git Commands:*
| Button | Shortcut       | Action                           |
|--------|----------------|----------------------------------|
| ↓      | Ctrl+Shift+D   | Run `git pull --rebase` in Shell |
| ↑      | Ctrl+Shift+U   | Run `git push` in Shell          |

*Dev Tool Commands:*
| Button | Shortcut       | Action                           |
|--------|----------------|----------------------------------|
| b      | Ctrl+Shift+B   | Run `dev b` (build) in Shell     |
| vc     | (none)         | Run `dev vc` (version+commit) in Shell |
| c      | (none)         | Run `dev c` (clean) in Shell     |
| f      | (none)         | Run `dev f` (frontend) in Shell  |

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

### Detected Links Button

The toolbar displays a links button that scans terminal output for clickable content. This provides an alternative to Ctrl+Click which doesn't work reliably with the terminal control.

**Button Display:**
- Shows a link icon (🔗) with a count badge (e.g., "🔗 5")
- Only visible when links are detected in terminal output
- Count updates automatically every 3 seconds

**Built-in Link Types:**
- **HTTP/HTTPS URLs**: Automatically detected and opened in default browser
- **File paths**: Shows syntax-highlighted preview popup
  - "Open in Editor" button to open with default application
  - Press Escape to close preview
  - Supports line numbers: `src/file.ts:42` opens at line 42
  - Relative paths resolved against working directory

**Custom Link Patterns:**
Configure regex patterns to convert text into clickable links (e.g., ticket numbers):

| Property    | Type   | Description                                           |
|-------------|--------|-------------------------------------------------------|
| id          | string | Unique identifier                                     |
| name        | string | Display name (shown in settings)                      |
| pattern     | string | Regex pattern with capturing groups                   |
| urlTemplate | string | URL template with $1, $2, etc. for captured groups    |
| enabled     | bool   | Whether this pattern is active                        |
| priority    | int    | Higher priority patterns matched first (default: 0)   |

**Example Configuration:**
```json
{
  "linkPatterns": [
    {
      "id": "jira-ticket",
      "name": "JIRA Ticket",
      "pattern": "([A-Z]+-\\d+)",
      "urlTemplate": "https://jira.example.com/browse/$1",
      "enabled": true,
      "priority": 10
    },
    {
      "id": "github-issue",
      "name": "GitHub Issue",
      "pattern": "#(\\d+)",
      "urlTemplate": "https://github.com/myorg/myrepo/issues/$1",
      "enabled": true,
      "priority": 5
    }
  ]
}
```

**How It Works:**
1. Terminal output is continuously buffered (~50KB of recent content)
2. Every 3 seconds, the buffer is scanned for links
3. Up to 20 unique links are displayed using FIFO ordering (most recent links shown)
4. Click a link in the popup to open it
5. Double-click or press Enter to open and close popup

**Popup Features:**
- Shows icon indicating link type (🔗 URL, 📄 file, 📁 directory, 🏷️ custom)
- Displays truncated preview and full URL/path
- Refresh button to manually rescan
- Keyboard navigation (arrows, Enter, Escape)
- **Preview button**: Opens file in built-in syntax-highlighted preview (files only)
- **Open button**: Opens link in default application (browser for URLs, default editor for files)
- Double-click or Enter to open and close popup

**Notes:**
- File paths are validated to exist before being shown
- Custom patterns are matched with higher priority first
- The popup closes automatically after opening a link

### Settings Editor

The Settings tab provides both a rich form-based editor and a raw JSON editor for the application configuration.

**View Modes:**
- **Rich Mode** (default): Form-based UI with sidebar navigation, proper controls, validation, and help text
- **Raw Mode**: JSON editor with syntax highlighting for advanced users or copy-pasting configurations

**Rich Mode Sections:**
| Section            | Description                                          |
|--------------------|------------------------------------------------------|
| General            | App behavior (confirm on close, system tray)         |
| Terminals          | Custom/Shell command paths, names, icons             |
| Profiles           | Terminal profiles with add/edit/delete/reorder       |
| Quick Commands     | Command shortcuts with add/edit/delete/reorder       |
| Link Patterns      | Custom URL patterns with live regex test panel       |
| Project Types      | Project detection rules with file patterns           |
| Claude Commands    | Info about auto-detected commands + folder links     |
| Directory Settings | Per-project layout, run terminal, explorer settings  |

**Rich Mode Features:**
- Sidebar navigation (VS Code style)
- Form controls: text inputs, file browsers, checkboxes, dropdowns, sliders
- List editors with add/delete/apply/reorder buttons
- Help text and tooltips for each field
- Dark-themed ComboBox controls
- Real-time sync between Rich and Raw modes

**Link Patterns Section:**
- List view with pattern name, regex preview, and enabled toggle
- Edit panel with name, regex pattern, URL template, priority, enabled checkbox
- **Live regex test panel**: Enter test input and see match results + generated URL in real-time
- Regex validation with error messages for invalid patterns

**Project Types Section:**
- List view with name, default command, and priority badge
- Edit panel with ID, name, detection files (one per line), default/watch commands, URL pattern, priority
- Add/Reset/Reorder buttons for managing project types

**Claude Commands Section:**
- Informational view explaining auto-detection from `.claude/commands/` folders
- **Clickable folder paths**: Opens folder in Explorer, prompts to create if missing
  - `~/.claude/commands/` (global commands)
  - `.claude/commands/` (project commands, relative to current project)
- **Documentation link**: Opens official Claude Code slash commands documentation
- Instructions for assigning shortcuts via `settings.claudeCommandShortcuts`

**Directory Settings Section:**
- Directory dropdown to select which project to configure
- **Layout settings**: Radio buttons (Custom Only / Horizontal / Vertical) + split ratio slider
- **Run terminal settings**: Visibility toggle + size slider
- **File explorer settings**: Visibility toggle + width slider
- **Run configurations editor**: Nested list with add/edit/delete for custom run configs
- Apply button to save changes

**Raw Mode Features:**
- JSON syntax highlighting (keys, strings, numbers, booleans)
- Save, Reload, and Format buttons in toolbar
- Reset Quick Commands button to restore defaults
- JSON validation on save with error messages

**Syntax Highlighting Colors (Raw Mode):**
| Element      | Color                  |
|--------------|------------------------|
| Keys         | Light blue (#9CDCFE)   |
| Strings      | Orange (#CE9178)       |
| Numbers      | Light green (#B5CEA8)  |
| Booleans/null| Blue (#569CD6)         |
| Brackets     | Gray (#CCCCCC)         |

**Implementation:**
- Uses RichTextBox with FlowDocument for syntax highlighting in Raw mode
- Form-based controls in Rich mode with two-way binding
- ConfigurationService provides raw JSON load/save with validation
- `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` for readable shortcuts (no `\u002B` for `+`)

### Profile Management

Profile management is integrated into the Settings editor (Ctrl+P navigates directly to the Profiles section).

**Features:**
- Create, edit, and delete custom profiles
- Reorder profiles with up/down buttons
- Each profile has: name, command, icon, keyboard shortcut, and auto-start option
- Profiles are stored in the `profiles` array in config.json
- Auto-start badge shows which profiles launch on startup

**Profile Properties:**
| Property   | Description                                     |
|------------|-------------------------------------------------|
| id         | Unique identifier (auto-generated)              |
| name       | Display name for the profile                    |
| command    | Command to execute (e.g., `pwsh.exe`, `ssh user@host`) |
| icon       | Emoji or symbol for display                     |
| shortcut   | Keyboard shortcut to launch (e.g., `Ctrl+Shift+P`) |
| autoStart  | Whether to launch on app startup                |

**Configuration:**
```json
{
  "profiles": [
    {
      "id": "profile-20251211120000",
      "name": "SSH Server",
      "command": "ssh user@server.example.com",
      "icon": "\uD83D\uDD12",
      "shortcut": "Ctrl+Shift+S",
      "autoStart": false
    }
  ]
}
```

### Profile Launching

Custom profiles can be launched as standalone single-terminal tabs (unlike project tabs which have paired terminals).

**Launch Methods:**
| Method | Description |
|--------|-------------|
| Profiles Tab | Click "Launch" button for selected profile |
| Profiles Tab | Click "In Folder..." to pick working directory |
| Command Palette | Search "Launch: {ProfileName}" |
| Keyboard Shortcut | Use profile's configured shortcut |

**Working Directory Behavior:**
- Uses profile's configured `WorkingDir` by default
- If `WorkingDir` is empty, uses user's home directory
- "In Folder..." opens folder picker to choose directory

**Profile Tab Features:**
- Single terminal (not paired like project tabs)
- Title shows profile name and directory
- Icon from profile configuration
- Activity indicator when terminal producing output
- Open in Explorer button in toolbar

**Command Palette Integration:**
Profile launch commands appear dynamically based on configured profiles:
- "Launch: {ProfileName}" for each profile
- Shows profile's keyboard shortcut if configured
- Shows profile's command in description

### System Tray

When `showInSystemTray` is enabled in settings, the application supports system tray functionality:

**Behavior:**
- **Minimize to tray**: When minimizing the window (or clicking X), the app hides to the system tray instead of closing
- **Tray icon**: Shows the app icon in the system notification area
- **Double-click**: Restores the window from tray
- **Context menu**: Right-click the tray icon for options:
  - **Show TerminalHost**: Restore the window
  - **Exit**: Fully close the application

**Configuration:**
```json
{
  "settings": {
    "showInSystemTray": true
  }
}
```

**Notes:**
- When disabled (default), the app closes normally when the window is closed
- Setting takes effect immediately when changed in Settings editor
- The tray icon uses the same app icon as the window

### File Editor

The File Editor (`Ctrl+Shift+E`) provides a built-in text editor for quick file edits without leaving the application.

**Features:**
- Line numbers with scroll synchronization
- Cursor position display (Line/Column)
- Modified indicator (*) in title bar
- Save (`Ctrl+S`), Reload, and Close buttons
- Go to line (`Ctrl+G`)
- Prompts for unsaved changes on close
- Draggable and resizable popup
- Preserves original file encoding on save

**Access Methods:**
- `Ctrl+Shift+E` to open file picker
- Click "Edit" button in file preview popup
- From command palette

**Limitations:**
- 1MB maximum file size
- Plain text editing (no syntax highlighting in editor)

### Command Palette

The Command Palette (`Ctrl+Shift+P`) provides VS Code-style quick access to all application commands.

**Features:**
- Searchable list of all available commands
- Shows keyboard shortcuts for each command
- Filters by name, description, or category
- Up/Down arrow navigation, Enter to execute
- Context-aware (some commands only available when applicable)

**Available Commands:**
| Command          | Description                      |
|------------------|----------------------------------|
| New Project      | Open folder as new project       |
| Close Tab        | Close current tab                |
| Switch Tab       | Open tab switcher                |
| Preview File     | Open file preview dialog         |
| Edit File        | Open file editor                 |
| Open in Explorer | Open folder in file explorer     |
| Switch Terminal  | Toggle custom/shell terminal     |
| Settings         | Open settings editor             |
| Profiles         | Manage terminal profiles         |
| Setup            | Check dependencies and setup     |
| Help             | Show keyboard shortcuts          |
| Scratch Pad      | Open notes panel                 |
| Git Changes      | View modified files and diffs    |
| Claude: /{name}  | Execute Claude slash command     |

### Claude User Commands

Claude Code custom slash commands are automatically detected and available in the Command Palette.

**Command Sources:**
- **Global commands**: `~/.claude/commands/*.md`
- **Project commands**: `.claude/commands/*.md` (relative to project root)

Project commands override global commands with the same name.

**Features:**
- Commands appear in Command Palette with "Claude: /{name}" prefix
- Search by command name or description
- Descriptions extracted from file frontmatter or first line
- Live file watching - new commands appear automatically
- Optional keyboard shortcuts per command

**How Commands Work:**
- Claude Code slash commands are markdown files
- The filename (without `.md`) becomes the command name
- When executed, TerminalHost sends `/{command-name}` to the Custom terminal

**Keyboard Shortcuts:**
Configure shortcuts in `config.json` under `settings.claudeCommandShortcuts`:

```json
{
  "settings": {
    "claudeCommandShortcuts": {
      "review-pr": "Ctrl+Alt+R",
      "test-coverage": "Ctrl+Alt+T"
    }
  }
}
```

**Example Command File:**
```markdown
---
description: Review the current PR and provide feedback
---

Review the current pull request. Check the git diff and provide:
1. Code quality feedback
2. Potential bugs or issues
3. Suggestions for improvement
```

### Scratch Pad

The Scratch Pad (`Ctrl+Shift+N`) provides a notes panel for jotting down TODOs, commands, or other information while working.

**Features:**
- **Per-project notes**: Each project directory has its own scratch pad
- **Global notes**: Shared notes accessible from any project
- **Auto-save**: Content saved automatically after 500ms of inactivity
- **Persistent storage**: Notes saved to config.json
- Toggle between "Project" and "Global" scope
- Draggable and resizable popup

**Storage:**
- Project notes stored in `scratchPads` object keyed by directory path
- Global notes stored in `globalScratchPad` string

### Git Changes Panel

The Git Changes Panel (`Ctrl+G`) provides a visual interface to view modified files and their diffs without leaving the application.

**Features:**
- **File list**: Shows all modified, added, deleted, and untracked files
- **Status icons**: Color-coded status indicators (M=Modified, A=Added, D=Deleted, ?=Untracked)
- **Diff viewer**: Syntax-highlighted diff view with additions (green) and deletions (red)
- **File actions**: Preview, Edit, or open in Explorer for each file
- **Refresh**: Manual refresh button to update file list
- Draggable and resizable popup
- Split-pane layout with adjustable divider

**File Status Types:**
| Icon | Color  | Status      |
|------|--------|-------------|
| M    | Yellow | Modified    |
| A    | Green  | Added       |
| D    | Red    | Deleted     |
| R    | Blue   | Renamed     |
| C    | Blue   | Copied      |
| ?    | Gray   | Untracked   |
| U    | Red    | Conflicted  |
| T    | Purple | Type changed|

**Actions:**
- **Preview**: Open file in syntax-highlighted preview popup
- **Edit**: Open file in built-in editor
- **Explorer**: Open containing folder in Windows Explorer

**Notes:**
- Only available when a project tab is selected
- Shows changes in the project's working directory
- For untracked files, shows entire file content as additions
- Deleted files cannot be previewed or edited (only shown in Explorer)

### Git Branch Management

The Git Branch popup (`Ctrl+B`) provides quick branch operations without using the terminal.

**Features:**
- **Branch list**: Shows all local and remote branches grouped by type
- **Current branch**: Highlighted with a filled indicator (●)
- **Search/filter**: Type to filter branches by name
- **One-click checkout**: Double-click or press Enter to switch branches
- **Ahead/behind status**: Shows sync status with remote
- **Keyboard navigation**: Arrow keys + Enter for quick switching

**Branch Actions:**
| Button       | Action                                          |
|--------------|-------------------------------------------------|
| ✓ Switch     | Checkout selected branch                        |
| + New Branch | Create new branch from current HEAD             |
| 🗑 Delete    | Delete selected branch (with confirmation)      |
| ↓ Fetch      | Fetch all remotes (`git fetch --all --prune`)   |
| ⟳ Pull       | Pull current branch                             |

**Delete Behavior:**
- **Local branches**: Asks for confirmation, offers force delete if not fully merged
- **Remote branches**: Extra confirmation warning ("cannot be undone")
- Cannot delete the current branch (switch first)

**Checkout Behavior:**
- Local branches: Direct checkout
- Remote branches: Creates local tracking branch if needed

### Task/Focus Mode

The Task Panel (`Ctrl+T`) provides a task management system for organizing daily work with focus mode to filter visible project tabs.

**Core Concepts:**
- **Task tree**: Hierarchical task structure (supports parent/child for subtasks or deferred work)
- **Focus mode**: When active, only shows project tabs linked to the current task
- **Quick notes**: Capture ideas quickly, convert to tasks later

**Task Properties:**
| Property      | Type     | Description                                    |
|---------------|----------|------------------------------------------------|
| id            | string   | Unique identifier (auto-generated)             |
| title         | string   | Short task title (required)                    |
| description   | string?  | Longer description (optional)                  |
| notes         | string?  | Scratch notes for this task                    |
| parentTaskId  | string?  | Parent task ID for hierarchy (null = root)     |
| projectPaths  | string[] | Associated project directories                 |
| status        | enum     | NotStarted, InProgress, Completed, Deferred    |
| priority      | int      | Higher = more important (default: 0)           |
| tags          | string[] | Optional tags for categorization               |
| linkedBranch  | string?  | Associated git branch name                     |
| linkedPrNumber| string?  | Associated PR number                           |
| linkedPrUrl   | string?  | Full PR URL for quick access                   |

**Task Status Icons:**
| Status      | Icon | Color  | Description                    |
|-------------|------|--------|--------------------------------|
| NotStarted  | ○    | Gray   | Task in backlog                |
| InProgress  | ●    | Yellow | Currently working on           |
| Completed   | ✓    | Green  | Task finished                  |
| Deferred    | ◐    | Blue   | Paused/deferred for later      |

**Task Panel Sections:**
1. **Current Task** (highlighted): Shows active task with elapsed time, linked projects, PR info
2. **Backlog**: Tasks not yet started, with priority badges
3. **Quick Notes**: Captured notes that can be converted to tasks
4. **Completed Today**: Tasks finished today with strikethrough

**Task Operations:**
| Button/Action    | Description                                    |
|------------------|------------------------------------------------|
| ▶ Start          | Begin working on a backlog task                |
| ✓ Complete       | Mark current task as done                      |
| ⏸ Pause          | Pause current task (returns to backlog)        |
| + Subtask        | Create a child task under current task         |
| 🔄 PR            | Refresh PR/branch info from GitHub             |
| + New Task       | Add a new task to backlog                      |
| Add & Start      | Create task and immediately start it           |

**Focus Mode:**
- Toggle with the "Focus Mode" / "Exit Focus" button
- When enabled, only project tabs linked to the current task are visible
- Other tabs remain open but hidden until focus mode is disabled
- Visual indicator shows when focus mode is active

**PR/Branch Integration:**
Automatic detection of PR and issue numbers from task titles:
- `PR #123` or `PR123` → Links to PR #123
- `#123` → Could be PR or issue
- `pull/123` → Links to PR #123
- `issues/123` or `issue/123` → Links to issue

When a PR number is detected:
- Searches for matching branches (e.g., `issues/123`, `feature/123`, `123-fix-auth`)
- Fetches PR details via GitHub CLI (`gh pr view`)
- Displays PR title, author, state, and change stats

**Quick Notes:**
- Capture quick thoughts without creating a full task
- Convert to task with one click (→ Task button)
- Useful for ideas that come up while working

**Configuration:**
Tasks and focus mode state are stored in `config.json`:
```json
{
  "focusMode": {
    "isEnabled": false,
    "currentTaskId": "task-20251217120000-abc12345",
    "taskHistory": ["task-20251217120000-abc12345"]
  },
  "tasks": [
    {
      "id": "task-20251217120000-abc12345",
      "title": "Implement PR #123",
      "description": null,
      "notes": null,
      "parentTaskId": null,
      "projectPaths": ["P:\\MyProject"],
      "status": "InProgress",
      "priority": 0,
      "tags": [],
      "createdAt": "2025-12-17T12:00:00Z",
      "startedAt": "2025-12-17T12:05:00Z",
      "completedAt": null,
      "linkedBranch": "issues/123",
      "linkedPrNumber": "123",
      "linkedPrUrl": null,
      "prDetails": null
    }
  ],
  "quickNotes": [
    {
      "id": "note-20251217164814-eb58e2f0",
      "text": "Quick note text",
      "createdAt": "2025-12-17T16:48:14Z",
      "convertedToTaskId": null,
      "projectPath": "P:\\MyProject"
    }
  ]
}
```

**Command Palette Commands:**
| Command          | Description                      |
|------------------|----------------------------------|
| Tasks            | Open task panel (Ctrl+T)         |
| Task: New        | Create a new task                |
| Task: Complete   | Complete current task            |
| Task: Focus Mode | Toggle focus mode                |

### Help Window

Press `F1` to open the Help window, which displays:

**Keyboard Shortcuts:**
- Tab navigation (Ctrl+PageDown/Up, Ctrl+1-9, Ctrl+Shift+T, Ctrl+W)
- Terminal switching (Ctrl+`), detected links button
- File operations (Ctrl+N, Ctrl+E, Ctrl+O)
- Application (Ctrl+,, Ctrl+P, F1)
- Default quick commands (Ctrl+Shift+C/D/U)

**Tips:**
- Drag tabs to reorder
- Splitter ratio saved per directory
- Supported syntax highlighting formats
- Custom link pattern configuration

**Command Line Usage:**
- `host` - Open app
- `host .` - Open current directory
- `host P:\Path` - Open specific project

**Important Paths:**
- Config file location

### Themed Dialogs

The application uses custom themed dialogs (`DialogService`) instead of standard Windows MessageBox for a consistent dark-theme experience.

**Dialog Types:**
| Type        | Icon   | Color  | Usage                           |
|-------------|--------|--------|----------------------------------|
| Error       | ⛔     | Red    | Error messages                   |
| Warning     | ⚠      | Yellow | Warnings and cautions            |
| Information | ℹ      | Blue   | Informational messages           |
| Question    | ❓     | Blue   | Confirmation prompts             |

**Button Configurations:**
- **OK**: Single acknowledgment button
- **OKCancel**: OK (primary) + Cancel (secondary)
- **YesNo**: Yes (primary) + No (secondary)

**Features:**
- Matches app dark theme (#252525 background, #0078D4 accent)
- Draggable via header bar
- Keyboard support (Enter to confirm, Escape to cancel)
- Centers on parent window

**Usage in Code:**
```csharp
// Simple error/warning/info (OK button only)
DialogService.ShowError("Error message", "Title");
DialogService.ShowWarning("Warning message", "Title");
DialogService.ShowInfo("Info message", "Title");

// Confirmation (Yes/No, returns bool)
if (DialogService.ShowConfirmation("Are you sure?", "Confirm"))
{
    // User clicked Yes
}
```

### File Preview Syntax Highlighting

File preview (Ctrl+O or Ctrl+Click on file paths) supports syntax highlighting for:

| Extension(s)           | Description                                |
|------------------------|--------------------------------------------|
| `.json`                | JSON with keys, strings, numbers, booleans |
| `.cs`                  | C# with keywords, types, methods, comments |
| `.js`, `.jsx`, `.ts`, `.tsx`, `.mjs`, `.cjs` | JavaScript/TypeScript |
| `.py`                  | Python with keywords, decorators, strings  |
| `.xml`, `.xaml`, `.html`, `.htm`, `.svg`, `.xsd`, `.config`, `.csproj` | XML/HTML |
| `.md`, `.markdown`     | Markdown with headers, links, code blocks  |
| `.csv`                 | CSV with column colorization               |
| `.tsv`                 | TSV with column colorization               |
| `.diff`, `.patch`      | Git diff/patch with additions, deletions, headers |

**CSV/TSV Colorization:**
- Each column is assigned a distinct color for easy visual differentiation
- Header row (first line) is displayed in white/bold
- Supports quoted values with escaped quotes
- 10 distinct column colors that cycle for files with many columns

### Project Structure

The codebase follows a modular architecture with reusable components extracted into dedicated views and view models. See `PRD.MainWindowRefactor.md` for the detailed refactoring history.

```
TerminalHost/
├── TerminalHost.sln
└── src/TerminalHost/TerminalHost/
        ├── App.xaml(.cs)                 # Application entry, single instance handling, shared styles, global exception handling
        ├── MainWindow.xaml               # Main window layout (tab strip + content + popup hosts)
        ├── MainWindow.xaml.cs            # Core window logic, keyboard shortcuts, popup coordination
        ├── Converters.cs                 # XAML value converters
        ├── Resources/
        │   └── TabContentTemplates.xaml  # DataTemplates for tab content (terminal, settings, etc.)
        ├── Domain/
        │   ├── Profile.cs          # Configuration template for terminal sessions
        │   ├── TerminalSession.cs  # Running terminal instance
        │   ├── TerminalPair.cs     # Paired custom + shell + run terminals
        │   ├── SessionState.cs         # Running/Exited enum
        │   ├── AppConfiguration.cs # Root config with settings
        │   ├── GitStatus.cs        # Git repository status model
        │   ├── GitFileStatus.cs    # Git file-level status (modified, added, etc.)
        │   ├── GitBranch.cs        # Git branch model for branch switcher
        │   ├── QuickCommand.cs     # Quick command definition with shortcut
        │   ├── LinkPattern.cs      # Custom link pattern definition
        │   ├── PaletteCommand.cs   # Command palette item definition
        │   ├── RunConfiguration.cs # Run configuration for project runner
        │   ├── ProjectType.cs      # Project type detection model
        │   ├── RunState.cs         # Run terminal state enum
        │   ├── FocusTask.cs        # Task model for focus mode
        │   ├── FocusTaskStatus.cs  # Task status enum (NotStarted, InProgress, etc.)
        │   ├── FocusModeState.cs   # Focus mode state container
        │   ├── QuickNote.cs        # Quick note model
        │   └── GitPrDetails.cs     # GitHub PR details model
        ├── Services/
        │   ├── ConfigurationService.cs   # JSON config load/save (+ raw JSON methods)
        │   ├── JsonFileService.cs        # Generic service for robust JSON file persistence with backup
        │   ├── IDialogService.cs         # Interface for themed dialogs
        │   ├── DialogService.cs          # Themed dialog service (replaces MessageBox)
        │   ├── ProfileRegistry.cs        # Profile and settings management
        │   ├── SessionManager.cs         # Session lifecycle tracking
        │   ├── SingleInstanceService.cs  # Mutex + named pipe IPC
        │   ├── SystemTrayService.cs      # System tray icon and menu
        │   ├── TerminalControlFactory.cs # Creates configured terminal controls
        │   ├── GitStatusService.cs       # Git command execution and parsing
        │   ├── FilePreviewService.cs     # File preview loading with syntax highlighting
        │   ├── FileEditService.cs        # File editing (load/save)
        │   ├── JsonSyntaxHighlighter.cs  # JSON syntax highlighting for settings
        │   ├── LinkDetectionService.cs   # Clickable link detection and handling
        │   ├── ProjectDetectionService.cs # Auto-detect project type for runner
        │   ├── RunUrlDetectionService.cs # Detect localhost URLs from run output
        │   ├── ITaskService.cs           # Interface for task/focus mode service
        │   ├── TaskService.cs            # Task management and focus mode
        │   ├── IGitPrService.cs          # Interface for GitHub PR service
        │   └── GitPrService.cs           # GitHub PR detection and fetching
    ├── ViewModels/
    │   ├── ITabViewModel.cs              # Interface for tab view models
    │   ├── MainViewModel.cs              # Main window logic, popup state
    │   ├── TerminalPairTabViewModel.cs   # Tab with paired terminals
    │   ├── ProfileTerminalTabViewModel.cs # Tab with single profile terminal
    │   ├── SettingsTabViewModel.cs       # Settings editor tab
    │   ├── ProfilesTabViewModel.cs       # Profile management tab
    │   ├── StatisticsTabViewModel.cs     # Usage statistics tab
    │   ├── SetupViewModel.cs             # Setup/dependency checker
    │   ├── ScratchPadViewModel.cs        # Scratch pad notes
    │   ├── GitBranchViewModel.cs         # Git branch operations
    │   ├── GitFilesViewModel.cs          # Git changed files + diff
    │   ├── DetectedLinksViewModel.cs     # Terminal link detection
    │   ├── FilePreviewViewModel.cs       # File preview with syntax highlighting
    │   ├── FileEditViewModel.cs          # File editor
    │   └── TaskPanelViewModel.cs         # Task panel for focus mode
    └── Views/
        ├── TabStrip.xaml(.cs)            # Tab bar with drag-drop, overflow, buttons
        ├── SettingsView.xaml(.cs)        # Settings editor UI
        ├── ProfilesView.xaml(.cs)        # Profile management UI
        ├── StatisticsView.xaml(.cs)      # Usage statistics UI
        ├── SetupWindow.xaml(.cs)         # Setup/dependency checker window
        ├── ScratchPadView.xaml(.cs)      # Scratch pad popup content
        ├── Dialogs/
        │   └── NotificationDialog.xaml(.cs)  # Themed dialog window
        ├── Tabs/
        │   ├── TerminalPairView.xaml(.cs)    # Terminal pair layout (custom + shell + run)
        │   └── ProfileTerminalView.xaml(.cs) # Single profile terminal layout
        └── Popups/
            ├── TabDropdownView.xaml(.cs)     # Tab overflow dropdown
            ├── TabSwitcherView.xaml(.cs)     # Tab search/switcher (Ctrl+Shift+T)
            ├── CommandPaletteView.xaml(.cs)  # Command palette (Ctrl+Shift+P)
            ├── HelpView.xaml(.cs)            # Help/shortcuts popup (F1)
            ├── GitBranchView.xaml(.cs)       # Git branch switcher (Ctrl+B)
            ├── GitFilesView.xaml(.cs)        # Git changes panel (Ctrl+G)
            ├── DetectedLinksView.xaml(.cs)   # Detected links popup
            ├── FilePreviewView.xaml(.cs)     # File preview popup (Ctrl+O)
            ├── FileEditView.xaml(.cs)        # File editor popup (Ctrl+Shift+E)
            ├── TaskPanelView.xaml(.cs)       # Task panel popup (Ctrl+T)
            └── QuickTaskView.xaml(.cs)       # Quick task input popup
```

### Setup Mode

To help users configure their environment correctly, the application includes a setup mode for checking dependencies and configuration.

**Access Methods:**
- Command line: `host /setup` (shows setup before main window)
- Command palette: Search for "Setup" (opens setup from within the app)

**Features:**
- **Setup Window**: A dedicated window for checking environment setup.
- **Dependency Detection**: Automatically checks for the presence and version of required and recommended tools:
  - Git (for version control integration)
  - A Nerd Font (for proper icon/glyph rendering in the terminal)
  - Claude Code CLI
  - GitHub CLI (for PR/issue integration in Tasks)
  - HC.Dev Tool
- **Installation Help**: Provides one-click buttons to either run the installation command (for CLIs) or open the download homepage (for fonts and Git).
- **Debug Information**: For troubleshooting, users can view the detailed output and exit code of each detection command.

**Button Behavior:**
- **From CLI (`/setup`)**: "Continue to App" proceeds to main app, "Close" exits
- **From Command Palette**: Both buttons just close the window (app continues running)

### Project Runner

Run and manage development servers/applications directly from the application with a dedicated run terminal.

**Features:**
- **Dedicated Run Terminal**: Third terminal panel (alongside Custom and Shell) for running projects
- **Auto-detection**: Automatically detects project type from marker files (.csproj, package.json, etc.)
- **Status bar controls**: Run/Stop button, status indicator, URL detection, and run terminal toggle
- **Keyboard shortcuts**: F5 to start/stop, Shift+F5 to force stop
- **URL detection**: Automatically detects localhost URLs from terminal output and displays as clickable link

**Supported Project Types:**
| Type       | Detect Files                        | Default Command      | Watch Command        |
|------------|-------------------------------------|----------------------|----------------------|
| .NET       | `*.csproj`, `*.sln`                 | `dotnet run`         | `dotnet watch run`   |
| Node.js    | `package.json`                      | `npm start`          | `npm run dev`        |
| Python     | `requirements.txt`, `pyproject.toml`| `python main.py`     | `flask run`          |
| Rust       | `Cargo.toml`                        | `cargo run`          | `cargo watch -x run` |
| Go         | `go.mod`                            | `go run .`           | -                    |

**Run Controls:**
| Control           | Description                                          |
|-------------------|------------------------------------------------------|
| Status indicator  | Colored dot: Gray (stopped), Yellow (starting), Green (running), Orange (stopping) |
| Run/Stop button   | Click to start or stop the project                   |
| Config dropdown   | Select which run configuration to use (if multiple)  |
| URL button        | Click to open detected localhost URL in browser      |
| Terminal toggle   | Show/hide the run terminal panel                     |

**Configuration:**
Run configurations are stored per-directory in `directorySettings`:
```json
{
  "directorySettings": {
    "p:\\myproject": {
      "runConfigurations": [
        {
          "id": "dev",
          "name": "Development",
          "command": "dotnet watch run",
          "isDefault": true,
          "urlPattern": "Now listening on: (https?://[^\\s]+)"
        }
      ],
      "isRunTerminalVisible": false,
      "runSplitRatio": 0.3,
      "activeRunConfigurationId": "dev"
    }
  },
  "projectTypes": [
    {
      "id": "dotnet",
      "name": ".NET",
      "detectFiles": ["*.csproj", "*.sln"],
      "defaultCommand": "dotnet run",
      "watchCommand": "dotnet watch run",
      "urlPattern": "Now listening on: (https?://[^\\s]+)",
      "priority": 10
    }
  ]
}
```

**Command Palette Commands:**
| Command            | Description                               |
|--------------------|-------------------------------------------|
| Run: Start         | Start the project (F5)                    |
| Run: Stop          | Stop the running project (Shift+F5)       |
| Run: Restart       | Restart the running project               |
| Run: Toggle Terminal | Show/hide run terminal panel            |
| Run: Open URL      | Open detected localhost URL in browser    |

### Usage Statistics Tab

A dedicated tab provides insights into terminal usage across different projects.

**Features:**
- **Data Source**: Automatically tracks character counts for Custom, Shell, and Run terminals. Data is saved daily to `stats.json` in the user's application data folder.
- **Project List**: Displays a list of all projects with recorded activity, sorted by total characters typed.
  - Each project shows a total character count.
  - A horizontal stacked bar provides a visual breakdown of characters typed in the Custom, Shell, and Run terminals.
- **Daily Activity Chart**: When a project is selected from the list, a detailed chart shows the character count for each of the last 30 days, providing a view of recent activity trends.

This provides both a high-level overview of which projects are most active and a detailed, day-by-day breakdown for any specific project.

## Future Considerations

Items for future development:

### Versioning and Auto-Updates

See **[PRD.Versioning.md](PRD.Versioning.md)** for detailed implementation plan.

**Summary**: Implement application versioning using MinVer (git tag-based) and automatic updates using Updatum (GitHub Releases integration). This enables tracking feature releases, displaying version in UI, and notifying users of available updates with optional auto-install.

### Activity Indicator Debugging

The current activity indicator implementation sometimes shows false positives due to terminal control rendering events. To improve reliability, consider adding debugging capabilities:

- **Activity log panel**: Show recent terminal output events with timestamps and character counts per terminal
- **Visual debug mode**: Display what triggered activity state changes (which terminal, output length, timing)
- **Filtering options**: Allow configuring minimum output threshold or duration before counting as "activity"

This would help identify the source of false triggers and tune the detection logic.

### Tab Management
- **Multiple Tabs for Same Folder**: An option to allow opening multiple tabs for the same directory (opt-in setting or forced shortcut like Ctrl+Shift+N)

### First-Run Setup Experience
- **Auto-Launch Setup on First Run**: When config file is empty/missing, show setup dialog before main window
- **Detect Missing Dependencies**: Run dependency checks automatically
- **Command Line Skip**: Add `--no-setup` flag to disable for unit tests and automation
- **Implementation Notes**:
  - `App.xaml.cs`: Check if config exists/is empty before showing main window
  - `ConfigurationService.cs`: Add `IsFirstRun()` method to check for empty/default config
  - `CommandLineArgs`: Add `SkipSetupCheck` flag (`--no-setup`, `-nosetup`)
  - Consider: Auto-skip if `DisableSingleInstance` is set (likely testing scenario)

### Single Instance Behavior Improvements
- **Show Message When No Arguments**: When `host` runs without arguments but process is already running:
  - Instead of silently closing, show a themed dialog explaining the situation
  - Message: "TerminalHost is already running. Use `host <path>` to open a project or `host -multi` to allow multiple instances."
- **Implementation Notes**:
  - `App.xaml.cs` lines 58-67: When `!startupArgs.HasValidRequest()` but another instance exists
  - Use `DialogService.ShowInfo()` or a simple `MessageBox` (since services not yet configured)
  - Include hint about `-multi` flag for developers who want multiple instances

### Advanced Panel Management
- **Fixed Claude panel**: Left panel (Claude terminal) should always be visible at full height
- **Right panel variants**: Right side can switch between different views:
  - Shell terminal (current behavior)
  - Project Runner terminal
  - Dedicated markdown viewer (auto-updated on file change) for PRD/progress tracking
- **Split right panel**: Alternative layout with right side split in half:
  - Top: Shell/console terminal
  - Bottom: Runner/PRD/other content panel
- **File explorer panel**: Simple tree-based folder/file browser
- **Persistent file viewer panel**: Auto-reloading file panel with multiple modes:
  - Preview mode (syntax highlighted, read-only)
  - Edit mode (editable)
  - Diff mode (show changes)
  - Auto-reload from disk when file changes externally

### Other Features
- **Custom profile pairs**: Different command pairs for different project types
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

*Document Version: 2.9*
*Last Updated: 2025-12-18*
