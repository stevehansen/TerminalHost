# ConHoster (TerminalHost)

**TerminalHost** (executable: `host.exe`) is a WPF desktop application that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal (PowerShell), allowing easy switching between them without termination.

It's designed for developers who work with AI coding assistants and need to seamlessly switch between the AI and a regular shell without losing context.

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
| Ctrl+G           | Open git changes panel              |
| F5               | Start/Stop project run              |

## Configuration

The configuration file is located at `%APPDATA%\TerminalHost\config.json`. You can edit it directly or use the built-in settings editor (`Ctrl+,`).

The configuration allows you to customize:
- Terminal commands (e.g., `customCommand`, `shellCommand`)
- Window state and open folders
- Quick commands and their shortcuts
- Custom link detection patterns

## For Developers

### Technology Stack

- **Framework**: WPF on .NET 8
- **Terminal Control**: EasyWindowsTerminalControl
- **MVVM**: CommunityToolkit.Mvvm
- **Single Instance**: Mutex + named pipe IPC

### Build Commands

```bash
# Build the solution
dotnet build

# Run the application
dotnet run --project src/TerminalHost/TerminalHost

# Publish as a single executable
dotnet publish src/TerminalHost/TerminalHost -c Release -o publish
```
