# TerminalHost

**TerminalHost** is a desktop application that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal, allowing easy switching between them without termination.

It's designed for developers who work with AI coding assistants and need to seamlessly switch between the AI and a regular shell without losing context.

## Platform Support

| Platform | Executable | UI Framework | Shell |
|----------|------------|--------------|-------|
| **Windows** | `host.exe` | WPF (.NET 8) | PowerShell |
| **macOS** | `host` | Avalonia (.NET 8) | zsh |

Both versions share the same core functionality and configuration format.

## Getting Started

### Opening Projects

From the command line:
```bash
# Windows
host              # Open/focus the application
host .            # Open project from current directory
host P:\MyProject # Open project from a specific path
host /setup       # Launch the setup/dependency checker

# macOS
host              # Open/focus the application
host .            # Open project from current directory
host ~/Projects   # Open project from a specific path
```

From within the application:
- `Ctrl+N` - Open new project via folder picker
- `Ctrl+Shift+O` - Quick repository switcher (favorites, recent, GitHub repos)

### The Three Terminals

Each project tab gives you three terminals:

| Terminal | Purpose | Toggle |
|----------|---------|--------|
| **Custom** | AI assistant (Claude Code by default) | Always visible (left/top) |
| **Shell** | PowerShell (Windows) or zsh (macOS) | `Ctrl+\`` to switch focus |
| **Run** | Development server output | `F5` to start, toggle visibility in toolbar |

Layout modes: Custom Full, Horizontal Split, Vertical Split (toggle in toolbar).

### Essential Shortcuts

| Action | Shortcut |
|--------|----------|
| Switch Custom/Shell focus | `Ctrl+\`` |
| Start/Stop project run | `F5` |
| Command palette | `Ctrl+Shift+P` |
| Git changes | `Alt+G` |
| File explorer | `Ctrl+Shift+F` |
| Settings | `Ctrl+,` |
| Help | `F1` |

### Workflows

**Per-Project Development** (default)
- Each tab is a project directory with paired terminals
- Split view keeps AI and shell visible simultaneously
- Quick commands (`Ctrl+Shift+C` commit, `Ctrl+Shift+U` push) for common actions

**Task/Focus Mode** (`Ctrl+T`)
- Create tasks linked to specific projects
- Focus mode hides unrelated tabs
- PR/branch integration with time tracking
- Quick notes that convert to tasks

**PR Review Mode** (`Ctrl+Shift+R`)
- View file changes with diff viewer
- Approve, request changes, or comment
- Integrated test runner (`F6`)

### Integrations

**Multiple AI Assistants**
- Built-in support for Claude Code, Gemini CLI, GitHub Copilot
- Per-project AI selection via toolbar dropdown
- Add custom AI assistants in Settings

**Claude Commands** (auto-detected)
- Global: `~/.claude/commands/*.md`
- Project: `.claude/commands/*.md`
- Commands appear in Command Palette with `Claude: /` prefix
- Live file watching - new commands appear automatically

**GitHub Integration**
- `Ctrl+Shift+H` - GitHub Dashboard (PRs, reviews, issues)
- `Ctrl+B` - Branch switcher with fetch/pull/delete
- `Alt+G` - Git changes with inline diffs

**File Tools**
- `Ctrl+O` - File preview with syntax highlighting
- `Ctrl+Shift+E` - File editor
- `Ctrl+Shift+F` - File explorer panel with git status
- `Ctrl+M` - Markdown preview (auto-reload)

**Project Runner**
- Auto-detects project type (.NET, Node.js, Python, Rust, Go)
- URL detection for localhost servers
- Custom run configurations per project

## Features

- **Directory-centric terminal pairs**: Each project directory gets a paired custom + shell terminal.
- **Single-instance with CLI**: `host .` opens or focuses a terminal pair for the current directory.
- **Easy terminal switching**: Toggle between custom and shell terminals without terminating the process.
- **Always-on split view**: Both terminals are visible simultaneously with an adjustable splitter.
- **Full terminal emulation**: Supports ANSI colors, interactive CLIs, and nerd fonts.
- **Tab management**: Standard tab controls (`Ctrl+Tab`, `Ctrl+W`, etc.), drag-and-drop reordering, and middle-click to close.
- **Persistence**: Remembers window state, open folders, and per-directory settings across sessions.
- **Git integration**: Displays the current Git branch and status in the tab and status bar.
- **Quick commands**: Configurable one-click buttons and keyboard shortcuts for common commands.
- **Detected links**: Scans terminal output for URLs and file paths, making them clickable.
- **Built-in tools**: Includes a file previewer, file editor, command palette, and scratch pad.
- **Project Runner**: Run and manage development servers with a dedicated run terminal and URL detection.
- **Setup Mode**: A startup window to detect and guide installation of recommended dependencies.

## Command Line Usage

```bash
# Open or focus the application
host

# Open a project from the current directory
host .

# Open a project from a specific path
host P:\MyProject

# Using a named argument
host --workdir P:\MyProject
host -w P:\MyProject

# Launch the setup and dependency checker window
host /setup

# Advanced/Testing arguments
host --disable-single-instance  # Allow multiple instances (or -multi)
host --user-data-dir "C:\Path"  # Override configuration path (or -data)
```

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
| Ctrl+`           | Switch between Custom/Shell terminal|
| Ctrl+Shift+P     | Open command palette                |
| Alt+G            | Open git changes panel              |
| F5               | Start/Stop project run              |

## Configuration

Configuration file location:
- **Windows**: `%APPDATA%\TerminalHost\config.json`
- **macOS**: `~/.config/TerminalHost/config.json`

You can edit it directly or use the built-in settings editor (`Ctrl+,`).

The configuration allows you to customize:
- Terminal commands (e.g., `customCommand`, `shellCommand`)
- Window state and open folders
- Quick commands and their shortcuts
- Custom link detection patterns

## For Developers

### Technology Stack

| Component | Windows | macOS |
|-----------|---------|-------|
| **UI Framework** | WPF (.NET 8) | Avalonia (.NET 8) |
| **Terminal Control** | EasyWindowsTerminalControl | Native PTY via Python helper |
| **MVVM** | CommunityToolkit.Mvvm | CommunityToolkit.Mvvm |
| **Single Instance** | Mutex + Named Pipes | Unix Domain Sockets |

### Project Structure

```
src/
├── TerminalHost.Core/      # Platform-agnostic (domain, interfaces, services)
├── TerminalHost.Windows/   # Windows-specific services (system tray, timers)
├── TerminalHost.macOS/     # macOS-specific services (PTY, Unix sockets)
├── TerminalHost/           # Windows WPF application
└── TerminalHost.Avalonia/  # macOS Avalonia application
```

### Build Commands

```bash
# Build for Windows
dotnet build
dotnet run --project src/TerminalHost/TerminalHost
dotnet publish src/TerminalHost/TerminalHost -c Release -o publish

# Build for macOS (on macOS)
dotnet build src/TerminalHost.Avalonia
dotnet publish src/TerminalHost.Avalonia -c Release -r osx-arm64 -o publish

# Build macOS app bundle with DMG installer
./scripts/build-macos.sh --dmg
```

### macOS Troubleshooting

**Cannot access Dropbox/iCloud/cloud storage folders**

If TerminalHost cannot access folders in `~/Library/CloudStorage/` (Dropbox, iCloud, OneDrive, etc.) after installing to `/Applications`, try these fixes:

1. **Remove quarantine and re-sign the app:**
   ```bash
   # Remove quarantine attribute
   sudo xattr -rd com.apple.quarantine /Applications/TerminalHost.app

   # Re-sign with entitlements
   codesign --force --deep --entitlements src/TerminalHost.Avalonia/TerminalHost.entitlements --sign - /Applications/TerminalHost.app
   ```

2. **Reset and re-grant Full Disk Access:**
   ```bash
   # Reset TCC permissions for the app
   tccutil reset All com.terminalhost.app
   ```
   Then go to **System Settings → Privacy & Security → Full Disk Access**, remove TerminalHost if listed, re-add it, and restart the app.

3. **Rebuild with correct entitlements:**
   ```bash
   ./scripts/build-macos.sh --dmg
   # Then reinstall from the new DMG
   ```
