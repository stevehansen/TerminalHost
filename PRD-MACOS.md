# Product Requirements Document: TerminalHost (macOS)

## Overview

**TerminalHost** (executable: `host` or `TerminalHost.app`) is an Avalonia desktop application for macOS that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal (zsh/bash), allowing easy switching between them without termination.

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

## Technology Stack

- **Framework**: .NET 8.0 with Avalonia UI 11.x
- **Terminal Control**: VtNetCore + MacPtyService (custom PTY implementation)
- **MVVM**: CommunityToolkit.Mvvm
- **Configuration**: JSON in `~/Library/Application Support/TerminalHost/`
- **Single Instance**: Named mutex + Unix sockets for IPC
- **Platform**: macOS 12.0+ (Apple Silicon and Intel)

## Terminal Control Configuration

- **Font**: Menlo (macOS default monospace) with Nerd Font fallback for glyphs
- **Theme**: Dark theme matching Avalonia Fluent Dark
- **Shell**: Uses system shell from `$SHELL` environment variable (typically `/bin/zsh`)

### Custom Terminal Shell Wrapping

The Custom terminal (left panel) runs the AI CLI inside a shell rather than directly. This provides better UX:
- The configured shell (e.g., zsh, bash) starts first
- After the shell initializes, the AI CLI command is automatically sent
- When you exit the AI CLI, you return to the shell prompt
- You can restart the AI CLI by typing its command again

This allows users to exit and restart the AI CLI without losing their terminal session.

## Command Line Usage

```bash
# Open/focus app with no arguments
host

# Open project from current directory
host .

# Open project from specific path
host ~/Projects/MyApp

# Using named argument
host --workdir ~/Projects/MyApp
host -w ~/Projects/MyApp

# Launch the setup and dependency checker window
host /setup

# Advanced/Testing arguments
host --disable-single-instance  # Allow multiple instances (or -multi)
host --user-data-dir "~/CustomPath"  # Override configuration path (or -data)
```

If a project tab for the specified directory already exists, it will be focused instead of creating a new tab.

## Keyboard Shortcuts

| Shortcut         | Action                              |
|------------------|-------------------------------------|
| Cmd+N            | Open new project (folder picker)    |
| Cmd+,            | Open settings editor                |
| Cmd+P            | Open settings (Profiles section)    |
| Cmd+E            | Open current folder in Finder       |
| Cmd+O            | Open file preview dialog            |
| F1               | Show help window                    |
| Cmd+Shift+T      | Open tab switcher (search tabs)     |
| Ctrl+PageDown    | Next tab                            |
| Ctrl+PageUp      | Previous tab                        |
| Cmd+1-9          | Jump to specific tab                |
| Cmd+W            | Close current tab                   |
| Middle-click     | Close tab under cursor              |
| Drag tab         | Reorder tabs                        |
| Ctrl+`           | Switch between Custom/Shell terminal|
| Cmd+Shift+E      | Open file editor                    |
| Cmd+Shift+P      | Open command palette                |
| Cmd+Shift+N      | Open scratch pad (notes)            |
| Cmd+G            | Open git changes panel              |
| Cmd+B            | Open git branch switcher            |
| Cmd+T            | Open task panel (focus mode)        |
| Cmd+Shift+H      | Open GitHub Dashboard               |
| Cmd+Shift+O      | Open Repository Quick Access        |
| Cmd+Shift+R      | Open PR Review Mode                 |
| F6               | Run tests (Quick Test Runner)       |
| Cmd+M            | Open Markdown Preview               |
| Cmd+Shift+C      | Quick command: Commit (Claude Code) |
| Cmd+Shift+V      | Quick command: Review (Claude Code) |
| Cmd+Shift+D      | Quick command: Git Pull (Shell)     |
| Cmd+Shift+U      | Quick command: Git Push (Shell)     |
| Cmd+Shift+B      | Quick command: Dev Build (Shell)    |
| F5               | Start/Stop project run              |
| Shift+F5         | Force stop project run              |
| Links button     | View detected URLs and file paths   |

## Configuration

Config file: `~/Library/Application Support/TerminalHost/config.json`
(A backup file `config.json.bak` is automatically created and used for recovery in case of primary file corruption.)

```json
{
  "profiles": [],
  "settings": {
    "confirmOnClose": true,
    "showInSystemTray": false,
    "customCommand": "~/.local/bin/claude",
    "customCommandName": "Claude Code",
    "customCommandIcon": "🤖",
    "shellCommand": "/bin/zsh",
    "shellCommandName": "Zsh",
    "shellCommandIcon": "💻",
    "customPaths": [
      "/usr/local/bin",
      "/opt/homebrew/bin"
    ]
  },
  "windowState": {
    "left": 100,
    "top": 100,
    "width": 1200,
    "height": 800,
    "isMaximized": false
  },
  "openFolders": [
    "~/Projects/Project1",
    "~/Projects/Project2"
  ],
  "directorySettings": {
    "~/projects/project1": {
      "layoutMode": "HorizontalSplit",
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
      "shortcut": "Cmd+Shift+C"
    }
  ]
}
```

### Custom PATH Configuration

The `customPaths` setting allows adding directories to the terminal's PATH:
- Paths are prepended to the system PATH
- Useful for ensuring tools like `dotnet`, `node`, or Homebrew binaries are available
- Example paths: `/usr/local/share/dotnet`, `/opt/homebrew/bin`

## Domain Model

### TerminalPair

A paired set of terminals for a project directory.

| Property        | Type            | Description                              |
|-----------------|-----------------|------------------------------------------|
| WorkingDirectory| string          | Project directory path                   |
| CustomTerminal  | TerminalSession | Custom command terminal (e.g., Claude)   |
| ShellTerminal   | TerminalSession | Shell terminal (e.g., zsh)               |
| RunTerminal     | TerminalSession | Optional run terminal for dev servers    |
| ActiveTerminal  | enum            | Which terminal is currently active       |
| DirectoryName   | string          | Display name (directory name only)       |

### TerminalSession

A running terminal instance.

| Property        | Type              | Description                         |
|-----------------|-------------------|-------------------------------------|
| Profile         | Profile           | Configuration for this terminal     |
| TerminalControl | MacTerminalControl| The Avalonia terminal control       |
| State           | SessionState      | Running or Exited                   |
| IsActive        | bool              | True if producing output (last 2s)  |
| LastOutputTime  | DateTime?         | When output was last received       |

### Profile

Configuration template for terminal sessions.

| Property       | Description                                     |
|----------------|-------------------------------------------------|
| id             | Unique identifier (auto-generated)              |
| name           | Display name for the profile                    |
| command        | Command to execute (e.g., `/bin/zsh`)           |
| startupCommand | Optional command sent after terminal starts     |
| workingDir     | Working directory (supports `~` expansion)      |
| icon           | Emoji or symbol for display                     |
| shortcut       | Keyboard shortcut to launch                     |
| autoStart      | Whether to launch on app startup                |

## Features

### Terminal Activity Indicators

Tabs display visual indicators to show terminal activity status:
- **Active (yellow spinner ◐)**: Terminal producing output
- **Completed (green dot ●)**: Activity stopped but tab not focused
- **Idle (no indicator)**: Tab has been focused since last activity

### Tab Management

- **Drag-and-Drop Reordering**: Drag tabs to reorder
- **Middle-Click to Close**: Middle-click any tab to close
- **Tab Overflow**: Scroll arrows and dropdown for many tabs
- **Tab Switcher (Cmd+Shift+T)**: Searchable popup for switching tabs

### Quick Commands

Quick commands provide keyboard shortcuts for common terminal operations:
- Claude Code: Commit (Cmd+Shift+C), Review (Cmd+Shift+R)
- Git: Pull (Cmd+Shift+D), Push (Cmd+Shift+U)
- Dev Tools: Build (Cmd+Shift+B)

### Detected Links

Terminal output is scanned for clickable content:
- HTTP/HTTPS URLs: Opened in default browser
- File paths: Preview with syntax highlighting
- Custom patterns: Configurable regex-to-URL mapping

### File Operations

- **File Preview (Cmd+O)**: Syntax-highlighted preview
- **File Editor (Cmd+Shift+E)**: Built-in text editor
- **File Explorer (Cmd+Shift+F)**: Tree view with git status

### Git Integration

- **Git Status Display**: Branch and status in tab title
- **Git Changes Panel (Cmd+G)**: View modified files and diffs
- **Git Branch Switcher (Cmd+B)**: Switch, create, delete branches

### Project Runner

- **Auto-detection**: Detects project type from marker files
- **F5 to Run**: Start/stop development servers
- **URL Detection**: Clickable localhost URLs from output

### Multiple AI Assistants

Support for multiple AI CLI tools:
- Claude Code (default), Gemini CLI, OpenAI Codex, GitHub Copilot
- Per-project AI selection via toolbar dropdown
- Immediate terminal restart when switching

## Project Structure

```
TerminalHost/
├── TerminalHost.sln
└── src/TerminalHost/TerminalHost/
    ├── App.axaml(.cs)              # Application entry, Avalonia setup
    ├── MainWindow.axaml(.cs)       # Main window layout
    ├── Controls/
    │   └── MacTerminalControl.cs   # VtNetCore-based terminal control
    ├── Domain/
    │   ├── Profile.cs              # Terminal profile with StartupCommand
    │   ├── TerminalSession.cs      # Running terminal instance
    │   ├── TerminalPair.cs         # Paired terminals
    │   └── AppConfiguration.cs     # Configuration model
    ├── Services/
    │   ├── MacPtyService.cs        # POSIX PTY implementation
    │   ├── TerminalControlFactory.cs # Creates terminal controls
    │   ├── ConfigurationService.cs  # JSON config management
    │   └── ...
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   ├── TerminalPairTabViewModel.cs
    │   └── ...
    └── Views/
        ├── Tabs/
        │   └── TerminalPairView.axaml(.cs)
        └── Popups/
            └── ...
```

## Building

### Prerequisites
- .NET 8.0 SDK
- Xcode Command Line Tools (for codesigning)

### Build Commands
```bash
# Quick build (debug)
dotnet build

# Release build with app bundle
./build-macos.sh

# Manual publish
dotnet publish src/TerminalHost/TerminalHost -c Release -r osx-arm64 -o publish/osx-arm64
```

## Installation

### From Release
1. Download `TerminalHost.app.zip` or `TerminalHost.dmg`
2. Extract/mount and drag to Applications
3. Right-click and select "Open" (first time only, due to Gatekeeper)

### Command Line Alias
Add to your `~/.zshrc`:
```bash
alias host='open /Applications/TerminalHost.app --args'
```

Or create a shell wrapper script in your PATH:
```bash
#!/bin/bash
/Applications/TerminalHost.app/Contents/MacOS/host "$@"
```

## Success Criteria

1. User can run `host .` and get a terminal pair for the current directory
2. User can switch between Claude Code and shell instantly
3. Split view shows both terminals with proper nerd font rendering
4. Running `host .` twice focuses the existing tab
5. All keyboard shortcuts work as documented
6. Configuration persists across restarts

---

*Document Version: 1.0 (macOS)*
*Last Updated: 2025-12-25*
