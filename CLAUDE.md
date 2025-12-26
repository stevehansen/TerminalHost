# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important: Documentation Maintenance

**Always keep documentation updated** when making changes to the codebase:
- When adding new features, document them in the relevant PRD file
- When changing existing behavior, update the relevant sections
- When adding new configuration options, update the schema documentation
- **When adding new keyboard shortcuts:**
  - Update [SHORTCUTS.md](SHORTCUTS.md) - the authoritative documentation registry
  - Update `ShortcutConflictService.BuiltInShortcutSections` - the single source of truth in code (Help view and conflict detection derive from this)
- Keep CLAUDE.md, PRD files, and SHORTCUTS.md in sync with the actual implementation

## Important: Testing Requirements

**Always verify tests when modifying features that have test coverage:**

### Running Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/TerminalHost.Tests
dotnet test tests/TerminalHost.UITests

# Run specific test
dotnet test --filter "FullyQualifiedName~SmokeTest_CanOpenSettings"
```

### Test Maintenance Guidelines
- **Before modifying a feature**: Check if tests exist in `tests/` directory
- **During feature development**: Update tests to match new behavior
- **After making changes**: Run affected tests to verify they pass
- **When adding UI elements**: Add `AutomationProperties.AutomationId` attributes for testability
- **When changing UI structure**: Update UI tests that rely on element visibility or location
- **When adding new features**: Consider adding test coverage

### Test Projects
- **TerminalHost.Tests**: Unit tests for services and view models
- **TerminalHost.UITests**: Automated UI tests using FlaUI (require built executable)

### Common Test Failures
- **UI tests**: May fail if UI structure changes (e.g., elements moved, visibility changed, new modes added)
- **Element not found**: Check if AutomationId exists and element is visible in current state
- **Timing issues**: UI tests may need delays for data template loading or state transitions

## Important: Service Abstractions for Testability

**Do NOT use system APIs directly in ViewModels or Services.** Use the injected service abstractions instead to enable unit testing with mocks.

### Required Service Abstractions

| Instead of... | Use... |
|---------------|--------|
| `System.IO.File.*` / `System.IO.Directory.*` | `IFileSystem` |
| `MessageBox.Show()` | `IDialogService` |
| `Process.Start()` | `IProcessService` |
| Running git commands directly | `IGitProcessRunner` |
| User feedback for operations | `IToastService` |

### Examples

```csharp
// BAD - Direct IO, cannot be mocked in tests
if (File.Exists(path)) { ... }
var content = File.ReadAllText(path);

// GOOD - Uses injected service
if (_fileSystem.FileExists(path)) { ... }
var content = _fileSystem.ReadAllText(path);

// BAD - Direct MessageBox, blocks tests
MessageBox.Show("Error occurred", "Error", MessageBoxButton.OK);

// GOOD - Uses injected service
_dialogService.ShowError("Error occurred", "Error");

// BAD - Direct Process.Start
Process.Start("notepad.exe");

// GOOD - Uses injected service
_processService.Start("notepad.exe");

// User feedback with toasts (non-blocking, non-intrusive)
// Simple success feedback
_toastService.Show("Settings saved", ToastType.Success);

// Progress toast for multi-step operations
using var toast = _toastService.ShowProgress("Checking out PR...");
var success = await DoOperationAsync();
if (success)
    toast.Complete("PR checked out");
else
    toast.Fail("Checkout failed");
```

### Exceptions (OK to use directly)
- `System.IO.Path.*` methods (pure string manipulation, no IO)
- `FileSystemWatcher` for file change monitoring (special OS facility)

### Known Technical Debt (Views and Startup Code)
The following use direct system calls because they don't participate in DI or execute before DI is configured:

**Views (code-behind) - No DI available:**
- `SettingsView.xaml.cs` - Direct Process.Start and Directory operations
- `ProfileTerminalView.xaml.cs` - Direct Process.Start and Directory.Exists
- `FileViewerWindow.xaml.cs` - Direct MessageBox (unsaved changes prompt)

**Startup code (before DI):**
- `App.xaml.cs` - MessageBox for startup errors

**Non-DI ViewModels:**
- `SetupViewModel.cs` - Created with `new`, uses Process.Start for opening URLs

**Internal CLI services:**
- `GitHubService.cs`, `GitPrService.cs` - Direct Process.Start for running `gh` CLI (the process execution IS the service's purpose)

## Project Overview

**TerminalHost** (executable: `host.exe`) is a WPF desktop application (.NET 8) that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal (PowerShell), plus an optional run terminal for development servers. Allows easy switching between them without termination.

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

# Publish as single executable (self-contained, ~70MB)
dotnet publish src/TerminalHost/TerminalHost -c Release -o publish

# Output locations
# Debug: src/TerminalHost/TerminalHost/bin/Debug/net8.0-windows/win-x64/host.exe
# Publish: publish/host.exe (single file, no .NET runtime required)
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

# Launch the setup and dependency checker window
host /setup

# Force new tab even if directory already open
host --new P:\MyProject
host -n P:\MyProject
host -n .

# Advanced/Testing arguments
host --disable-single-instance  # Allow multiple instances (or -multi)
host --user-data-dir "C:\Path"  # Override configuration path (or -data)
host --no-setup                 # Skip first-run setup check
```

If a project tab for the specified directory already exists, it will be focused instead of creating a new tab (unless `--new` is used).

## Project Structure

The codebase follows a modular architecture with reusable components extracted into dedicated views and view models.

```
TerminalHost/
├── TerminalHost.sln
└── src/TerminalHost/TerminalHost/
    ├── App.xaml(.cs)                 # Application entry, single instance handling, shared styles
    ├── MainWindow.xaml               # Main window layout (tab strip + content + popup hosts)
    ├── MainWindow.xaml.cs            # Core window logic, keyboard shortcuts, popup coordination
    ├── Converters.cs                 # XAML value converters
    ├── Resources/
    │   └── TabContentTemplates.xaml  # DataTemplates for tab content (terminal, settings, etc.)
    ├── Domain/
    │   ├── Profile.cs          # Configuration template for terminal sessions
    │   ├── TerminalSession.cs  # Running terminal instance
    │   ├── TerminalPair.cs     # Paired custom + shell + run terminals
    │   ├── SessionState.cs     # Running/Exited enum
    │   ├── AppConfiguration.cs # Root config with settings
    │   ├── GitStatus.cs        # Git repository status model
    │   ├── GitFileStatus.cs    # Git file-level status (modified, added, etc.)
    │   ├── GitBranch.cs        # Git branch model for branch switcher
    │   ├── QuickCommand.cs     # Quick command with shortcut
    │   ├── LinkPattern.cs      # Custom link pattern definition
    │   ├── PaletteCommand.cs   # Command palette item definition
    │   ├── RunConfiguration.cs # Run configuration for project runner
    │   ├── ProjectType.cs      # Project type detection model
    │   ├── ClaudeCommand.cs    # Claude slash command from .md files
    │   ├── RunState.cs         # Run terminal state enum
    │   ├── FileSystemNode.cs   # File explorer tree node with git status
    │   └── FileIconMapper.cs   # File extension to icon mapping
    ├── Services/
    │   ├── ConfigurationService.cs   # JSON config load/save
    │   ├── DialogService.cs          # Themed dialog service (replaces MessageBox)
    │   ├── ProfileRegistry.cs        # Profile management
    │   ├── SessionManager.cs         # Session lifecycle
    │   ├── SingleInstanceService.cs  # Mutex + named pipe IPC
    │   ├── SystemTrayService.cs      # System tray icon and menu
    │   ├── TerminalControlFactory.cs # Creates EasyTerminalControl instances
    │   ├── GitStatusService.cs       # Git command execution
    │   ├── FilePreviewService.cs     # File preview loading
    │   ├── FileEditService.cs        # File editing (load/save)
    │   ├── JsonSyntaxHighlighter.cs  # JSON syntax highlighting
    │   ├── LinkDetectionService.cs   # Clickable link detection
    │   ├── ProjectDetectionService.cs # Auto-detect project type
    │   ├── RunUrlDetectionService.cs # Detect localhost URLs from run output
    │   ├── FileExplorerService.cs    # File explorer operations + file watcher
    │   ├── ClaudeCommandService.cs   # Claude slash commands detection + file watching
    │   ├── IToastService.cs          # Toast notification interface
    │   └── ToastService.cs           # Toast notification service
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
    │   ├── FileViewerViewModel.cs        # Unified file preview/edit viewer (with image support)
    │   ├── FileExplorerViewModel.cs      # File explorer tree + operations
    │   └── ToastViewModel.cs             # Individual toast state
    └── Views/
        ├── TabStrip.xaml(.cs)            # Tab bar with drag-drop, overflow, buttons
        ├── SettingsView.xaml(.cs)        # Settings editor UI
        ├── ProfilesView.xaml(.cs)        # Profile management UI
        ├── StatisticsView.xaml(.cs)      # Usage statistics UI
        ├── SetupWindow.xaml(.cs)         # Setup/dependency checker window
        ├── ScratchPadView.xaml(.cs)      # Scratch pad popup content
        ├── FileExplorerView.xaml(.cs)    # File explorer panel
        ├── FileViewerView.xaml(.cs)      # Unified file preview/edit view
        ├── FileViewerWindow.xaml(.cs)    # Detached/pop-out file viewer window
        ├── ToastContainerView.xaml(.cs)  # Toast notification container
        ├── ToastItemView.xaml(.cs)       # Individual toast UI
        ├── ToastWindow.xaml(.cs)         # Overlay window for toasts (airspace fix)
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
            └── FileViewerPopup.xaml(.cs)     # Unified file viewer popup (Ctrl+O/Ctrl+Shift+E)
```

## Architecture

### Terminal Pairs
Each project directory opens as a `TerminalPair` containing:
- **Custom Terminal**: Runs configured command (default: Claude Code)
- **Shell Terminal**: Runs shell (default: PowerShell)
- **Run Terminal**: Optional third terminal for running development servers (created on demand)

Custom and Shell terminals are created simultaneously and always visible in a split view layout. The Run terminal appears on the right when activated.

### Working Directory Handling
EasyTerminalControl doesn't have a native working directory property. The factory wraps commands:
- PowerShell: `pwsh.exe -NoExit -Command "Set-Location 'C:\path'"`
- CMD: `cmd.exe /K "cd /d C:\path"`

### Single Instance Behavior
- First instance acquires mutex and starts named pipe server
- Subsequent instances send args via pipe and exit
- Running `host .` twice for the same directory focuses existing tab

## Keyboard Shortcuts

> **Note**: For a complete registry of all shortcuts including available slots, see [SHORTCUTS.md](SHORTCUTS.md). Keep that file updated when adding new shortcuts.

### Tab Navigation
- `Ctrl+PageDown` / `Ctrl+PageUp`: Cycle through project tabs
- `Ctrl+1-9`: Jump to specific tab
- `Ctrl+Shift+T`: Open tab switcher (search and switch tabs)
- `Ctrl+W`: Close current tab
- `Middle-click tab`: Close tab
- `Drag tab`: Reorder tabs

### Terminal
- `Ctrl+\``: Switch between Custom/Shell terminal
- `Links button`: Shows detected URLs and file paths from terminal output (toolbar)

### File Operations
- `Ctrl+N`: Open new project (folder picker)
- `Ctrl+E`: Open current folder in Explorer
- `Ctrl+O`: Open file viewer (preview mode, supports images)
- `Ctrl+Shift+E`: Open file viewer (edit mode)
- `Ctrl+Shift+F`: Toggle file explorer panel (tree view with git status)

### Application
- `Ctrl+,`: Open settings editor
- `Ctrl+P`: Open settings (Profiles section)
- `Ctrl+Shift+P`: Open command palette
- `Ctrl+Shift+N`: Open scratch pad (notes)
- `Ctrl+G`: Open git changes panel (modified files + diffs + staging + commit UI)
- `Ctrl+H`: Open commit history viewer
- `Ctrl+F3`: Search across files (full-text search with replace)
- `Ctrl+B`: Open git branch switcher
- `Ctrl+T`: Open task panel (focus mode)
- `F1`: Show help window

### Project Runner
- `F5`: Start/Stop project run
- `Shift+F5`: Force stop project run

### Default Quick Commands
- `Ctrl+Shift+C`: Quick command - Commit (Claude Code)
- `Ctrl+Shift+D`: Quick command - Git Pull (Shell)
- `Ctrl+Shift+U`: Quick command - Git Push (Shell)

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
      "layoutMode": "HorizontalSplit",
      "splitRatio": 0.6,
      "activeTerminal": "Custom",
      "isRunTerminalVisible": false,
      "runSplitRatio": 0.3,
      "isExplorerVisible": false,
      "explorerSplitRatio": 0.25
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
    }
  ]
}
```

### Persistence Features
- **Window State**: Position, size, and maximized state are saved on close and restored on startup
- **Open Folders**: Previously open project tabs are automatically restored on startup
- **Directory Settings**: Layout mode (CustomFull/HorizontalSplit/VerticalSplit), split ratio, and active terminal are saved per directory

### Terminal Activity Indicators
Tabs show an animated spinning indicator when terminals are producing output:
- Uses `ConPTYTerm.InterceptOutputToUITerminal` to track output
- Terminal is "active" if output received within last 2 seconds
- Spinner appears/animates when active, hidden when idle
