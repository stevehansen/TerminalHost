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
- [x] Settings editor tab with JSON syntax highlighting (Ctrl+,)
- [x] System tray support (minimize to tray, restore on click)
- [x] Profile management UI (Ctrl+P)
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

### Deferred Features

- [ ] Custom profiles beyond the default pair (launching profiles from UI)

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
| Ctrl+,           | Open settings editor                |
| Ctrl+P           | Open profile management             |
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
| Ctrl+Shift+C     | Quick command: Commit (Claude Code) |
| Ctrl+Shift+R     | Quick command: Rate Code (Claude Code) |
| Ctrl+Shift+V     | Quick command: Review PR (Claude Code) |
| Ctrl+Shift+D     | Quick command: Git Pull (Shell)     |
| Ctrl+Shift+U     | Quick command: Git Push (Shell)     |
| Ctrl+Shift+B     | Quick command: Dev Build (Shell)    |
| Links button     | View detected URLs and file paths from terminal output |

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
3. Up to 15 unique links are displayed (URLs, file paths, custom patterns)
4. Click a link in the popup to open it
5. Double-click or press Enter to open and close popup

**Popup Features:**
- Shows icon indicating link type (🔗 URL, 📄 file, 📁 directory, 🏷️ custom)
- Displays truncated preview and full URL/path
- Refresh button to manually rescan
- Keyboard navigation (arrows, Enter, Escape)

**Notes:**
- File paths are validated to exist before being shown
- Custom patterns are matched with higher priority first
- The popup closes automatically after opening a link

### Settings Editor

The Settings tab provides a JSON editor for the application configuration file with syntax highlighting.

**Features:**
- Opens as a special tab via the Settings button or `Ctrl+,`
- JSON syntax highlighting (keys, strings, numbers, booleans)
- Save, Reload, and Format buttons in toolbar
- Reset Quick Commands button to restore defaults (preserves other settings)
- JSON validation on save with error messages
- Dark theme matching the terminal interface

**Syntax Highlighting Colors:**
| Element      | Color                  |
|--------------|------------------------|
| Keys         | Light blue (#9CDCFE)   |
| Strings      | Orange (#CE9178)       |
| Numbers      | Light green (#B5CEA8)  |
| Booleans/null| Blue (#569CD6)         |
| Brackets     | Gray (#CCCCCC)         |

**Implementation:**
- Uses RichTextBox with FlowDocument for syntax highlighting
- Regex-based tokenization for JSON elements
- ConfigurationService provides raw JSON load/save with validation

### Profile Management

The Profiles tab (Ctrl+P) provides a UI for managing custom terminal profiles.

**Features:**
- Create, edit, and delete custom profiles
- Each profile has: name, command, icon, keyboard shortcut, and auto-start option
- Profiles are stored in the `profiles` array in config.json

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

**Note:** Currently profiles can be created and managed, but launching them as separate terminals is planned for a future update.

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
| Help             | Show keyboard shortcuts          |
| Scratch Pad      | Open notes panel                 |
| Git Changes      | View modified files and diffs    |

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
    │   ├── GitFileStatus.cs    # Git file-level status (modified, added, etc.)
    │   ├── QuickCommand.cs     # Quick command definition with shortcut
    │   ├── LinkPattern.cs      # Custom link pattern definition
    │   └── PaletteCommand.cs   # Command palette item definition
    ├── Services/
    │   ├── ConfigurationService.cs   # JSON config load/save (+ raw JSON methods)
    │   ├── ProfileRegistry.cs        # Profile and settings management
    │   ├── SessionManager.cs         # Session lifecycle tracking
    │   ├── SingleInstanceService.cs  # Mutex + named pipe IPC
    │   ├── SystemTrayService.cs      # System tray icon and menu
    │   ├── TerminalControlFactory.cs # Creates configured terminal controls
    │   ├── GitStatusService.cs       # Git command execution and parsing
    │   ├── FilePreviewService.cs     # File preview loading with syntax highlighting
    │   ├── FileEditService.cs        # File editing (load/save)
    │   ├── JsonSyntaxHighlighter.cs  # JSON syntax highlighting for settings
    │   └── LinkDetectionService.cs   # Clickable link detection and handling
    ├── ViewModels/
    │   ├── ITabViewModel.cs              # Interface for tab view models
    │   ├── MainViewModel.cs              # Main window logic
    │   ├── TerminalPairTabViewModel.cs   # Tab with paired terminals
    │   ├── SettingsTabViewModel.cs       # Settings editor tab
    │   └── ProfilesTabViewModel.cs       # Profile management tab
    └── Views/
        ├── SettingsView.xaml(.cs)        # Settings editor UI
        └── ProfilesView.xaml(.cs)        # Profile management UI
```

## Future Considerations

Items for future development:

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

*Document Version: 2.1*
*Last Updated: 2025-12-12*
