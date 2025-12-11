# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important: Documentation Maintenance

**Always keep PRD.md updated** when making changes to the codebase:
- When adding new features, document them in PRD.md
- When changing existing behavior, update the relevant sections
- When adding new configuration options, update the schema documentation
- When adding new keyboard shortcuts, update the shortcuts list
- Keep both CLAUDE.md and PRD.md in sync with the actual implementation

## Project Overview

**TerminalHost** (executable: `host.exe`) is a WPF desktop application (.NET 8) that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal (PowerShell), allowing easy switching between them without termination.

## Technology Stack

- **Framework**: WPF on .NET 8
- **Terminal Control**: EasyWindowsTerminalControl (NuGet package)
- **MVVM**: CommunityToolkit.Mvvm for view models
- **Configuration**: JSON file stored in `%APPDATA%\TerminalHost\config.json`
- **Single Instance**: Mutex detection with named pipe IPC

## Build Commands

```bash
# Build the solution
dotnet build

# Run the application
dotnet run --project src/TerminalHost/TerminalHost

# Build for release
dotnet build -c Release

# Output executable location
# bin/Debug/net8.0-windows/host.exe
```

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

## Project Structure

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
    │   ├── TerminalPair.cs     # Paired custom + shell terminals for a directory
    │   ├── SessionState.cs     # Running/Exited enum
    │   └── AppConfiguration.cs # Root config with settings
    ├── Services/
    │   ├── ConfigurationService.cs   # JSON config load/save
    │   ├── ProfileRegistry.cs        # Profile management
    │   ├── SessionManager.cs         # Session lifecycle
    │   ├── SingleInstanceService.cs  # Mutex + named pipe IPC
    │   └── TerminalControlFactory.cs # Creates EasyTerminalControl instances
    └── ViewModels/
        ├── MainViewModel.cs              # Main window logic
        └── TerminalPairTabViewModel.cs   # Tab with paired terminals
```

## Architecture

### Terminal Pairs
Each project directory opens as a `TerminalPair` containing:
- **Custom Terminal**: Runs configured command (default: Claude Code)
- **Shell Terminal**: Runs shell (default: PowerShell)

Both terminals are created simultaneously but only one is visible at a time (unless split view is enabled).

### Working Directory Handling
EasyTerminalControl doesn't have a native working directory property. The factory wraps commands:
- PowerShell: `pwsh.exe -NoExit -Command "Set-Location 'C:\path'"`
- CMD: `cmd.exe /K "cd /d C:\path"`

### Single Instance Behavior
- First instance acquires mutex and starts named pipe server
- Subsequent instances send args via pipe and exit
- Running `host .` twice for the same directory focuses existing tab

## Keyboard Shortcuts

- `Ctrl+N`: Open new project (folder picker)
- `Ctrl+PageDown` / `Ctrl+PageUp`: Cycle through project tabs
- `Ctrl+1-9`: Jump to specific tab
- `Ctrl+W`: Close current tab
- `Ctrl+\``: Switch between Custom/Shell terminal
- `Ctrl+\`: Toggle split view

## Configuration Schema

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
  }
}
```

### Persistence Features
- **Window State**: Position, size, and maximized state are saved on close and restored on startup
- **Open Folders**: Previously open project tabs are automatically restored on startup
- **Directory Settings**: Split view state, split ratio, and active terminal are saved per directory
