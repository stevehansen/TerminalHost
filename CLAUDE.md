# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Problem Statement

Developers working with AI coding assistants like Claude Code need to:
- Run the AI assistant in a project directory
- Quickly switch to a shell for manual commands (git, npm, etc.)
- Return to the AI assistant without losing context

Current solutions require multiple terminal windows or tabs that must be manually configured and navigated. TerminalHost pairs terminals per project directory, making it effortless to switch between AI assistant and shell.

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
- [x] **Git Integration**: Status display, Branch switcher (Ctrl+B), Changes panel with diff and hunk staging (Alt+G), Stash manager (Ctrl+Shift+S), Commit graph, Merge conflict resolution, Tree view toggle, Advanced commit filters.
- [x] **File Tools**: File explorer panel (Ctrl+Shift+F), syntax-highlighted preview (Ctrl+O), built-in editor (Ctrl+Shift+E).
- [x] **Productivity**: Command palette (Ctrl+Shift+P), Tab switcher (Ctrl+Shift+T), Scratch pad (Ctrl+Shift+N).
- [x] **Project Runner**: F5 to run projects with auto-detection and dedicated run terminal.
- [x] **Timeline Mode**: Visual timeline of AI development sessions, intents, and worktrees (Ctrl+Shift+I).
- [x] **AI Assistant Support**: Multi-AI CLI support (Claude, Gemini, etc.) with per-project selection.
- [x] **GitHub Integration**: Dashboard (Ctrl+Shift+H), PR Review Mode (Ctrl+Shift+R).
- [x] **UI Enhancements**: Toast notifications, themed dialogs, system tray support, Markdown preview (Ctrl+M).
- [x] **Touch Mode**: Touch-friendly UI mode with larger touch targets, icon-only toolbar, narrower sidebar, and sidebar collapse button. Ideal for mobile RDP and demos.
- [x] **Resilience**: Robust JSON persistence with automatic backups and thread-safe writes.
- [x] **Panel-Based Layout**: Center panels replace terminals for Git GUI, PR Review, Test Results, File Viewer, Search, Markdown Preview, Branch Comparison. Right sidebar hosts File Explorer, Claude Tasks, Detected Links, Scratch Pad. All panel state persisted across restarts. Terminals continue running in background when center panel is active.
- [x] **What's New Page**: Empty state shows recently added features grouped by week with NEW badges. Also available as center panel via Ctrl+F1 or command palette. All palette commands have `IntroducedOn` dates.
- [x] **Voice Commands**: Hands-free control via speech recognition (F4). Floating bar shows transcript, matched command preview with confidence-based countdown, "Send to AI" fallback for unmatched speech, and meta-commands (confirm/cancel/send-to-AI keywords). Settings in Ctrl+, General section.

### Deferred Features

(None currently)

## Important: Documentation Maintenance

**Always keep documentation updated** when making changes to the codebase:
- When adding new features, update the "Current Implementation Status" section above
- When changing existing behavior, update the relevant sections
- When adding new configuration options, update the schema documentation
- **When adding new keyboard shortcuts:**
  - Update [SHORTCUTS.md](SHORTCUTS.md) - the authoritative documentation registry
  - Update `ShortcutConflictService.BuiltInShortcutSections` - the single source of truth in code (Help view and conflict detection derive from this)
- **When using XAML converters:** Reference [CONVERTERS.md](CONVERTERS.md) for exact names and parameters
- **When adding new actions**: Register them in `InitializeCommandPalette()` in `MainViewModel.cs`. The command palette must contain ALL invocable actions (toolbar buttons, keyboard shortcuts, context menu items, settings toggles). Set `IntroducedOn = new DateOnly(year, month, day)` to the current date so the feature appears in "What's New".

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

**TerminalHost** is a cross-platform desktop application (.NET 8) that manages terminal pairs for project directories. Each project tab contains two terminals: a custom command terminal (default: Claude Code) and a shell terminal, plus an optional run terminal for development servers. Allows easy switching between them without termination.

| Platform | Executable | UI Framework | Shell |
|----------|------------|--------------|-------|
| Windows | `host.exe` | WPF | PowerShell |
| macOS | `host` | Avalonia | zsh |

## Technology Stack

| Component | Windows | macOS |
|-----------|---------|-------|
| **UI Framework** | WPF (.NET 8) | Avalonia (.NET 8) |
| **Terminal Control** | EasyWindowsTerminalControl | Native PTY via Python helper |
| **MVVM** | CommunityToolkit.Mvvm | CommunityToolkit.Mvvm |
| **Single Instance** | Mutex + Named Pipes | Unix Domain Sockets |
| **Config Location** | `%APPDATA%\TerminalHost\` | `~/.config/TerminalHost/` |

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

## Build Commands

```bash
# Build the solution (all projects)
dotnet build

# Windows - Run the WPF application
dotnet run --project src/TerminalHost/TerminalHost

# Windows - Publish as single executable (~70MB)
dotnet publish src/TerminalHost/TerminalHost -c Release -o publish

# macOS - Run the Avalonia application (on macOS)
dotnet run --project src/TerminalHost.Avalonia

# macOS - Publish for Apple Silicon
dotnet publish src/TerminalHost.Avalonia -c Release -r osx-arm64 -o publish

# macOS - Publish for Intel
dotnet publish src/TerminalHost.Avalonia -c Release -r osx-x64 -o publish
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

## Cross-Platform Architecture

The codebase is split into platform-agnostic and platform-specific projects:

```
src/
├── TerminalHost.Core/        # Platform-agnostic (.NET 8)
│   ├── Domain/               # All domain models (44 files)
│   ├── Interfaces/           # Service contracts (23 interfaces)
│   ├── Services/             # Portable service implementations
│   └── ViewModels/           # Portable ViewModels (5 files)
│
├── TerminalHost.Windows/     # Windows-specific (.NET 8 Windows)
│   ├── Services/             # TimerService, ToastService, SingleInstanceService
│   └── Platform/             # DarkModeHelper (P/Invoke)
│
├── TerminalHost.macOS/       # macOS-specific (.NET 8)
│   ├── Services/             # MacSingleInstanceService, MacTimerService
│   └── Resources/            # pty_helper.py for PTY support
│
├── TerminalHost/             # Windows WPF application
│   ├── Views/                # WPF XAML views
│   ├── ViewModels/           # WPF-coupled ViewModels
│   └── Services/             # WPF-coupled services (DialogService, etc.)
│
└── TerminalHost.Avalonia/    # macOS Avalonia application
    ├── Views/                # Avalonia AXAML views
    ├── ViewModels/           # Avalonia-coupled ViewModels
    └── Services/             # Avalonia-coupled services
```

### Important: Cross-Platform Code Changes

**When modifying code, consider which project(s) need updates:**

| Change Type | Where to Update |
|-------------|-----------------|
| Domain models (data classes) | `TerminalHost.Core/Domain/` |
| Service interfaces | `TerminalHost.Core/Interfaces/` |
| Platform-agnostic logic | `TerminalHost.Core/Services/` or `ViewModels/` |
| Windows-only features | `TerminalHost/` and/or `TerminalHost.Windows/` |
| macOS-only features | `TerminalHost.Avalonia/` and/or `TerminalHost.macOS/` |
| UI views (both platforms) | `TerminalHost/Views/` (XAML) AND `TerminalHost.Avalonia/Views/` (AXAML) |
| Platform services (timers, dialogs) | Both `TerminalHost.Windows/` AND `TerminalHost.macOS/` |

**Examples:**
- Adding a new setting → Update `AppSettings.cs` in Core, then update Settings views in both WPF and Avalonia
- New git feature → Add interface to Core, implement in Core if portable, update views in both apps
- Windows-specific fix → Only update `TerminalHost/` or `TerminalHost.Windows/`

## Project Structure (Windows WPF App)

The Windows application follows a modular architecture with reusable components extracted into dedicated views and view models.

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
        ├── ScratchPadContentView.xaml(.cs)  # Scratch pad content (for panel system)
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
            ├── GitFilesView.xaml(.cs)        # Git changes panel (Alt+G)
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
- `Ctrl+``: Switch between Custom/Shell terminal
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
- `Alt+G`: Open git changes panel (modified files + diffs + staging + commit UI)
- `Ctrl+H`: Open commit history viewer
- `Ctrl+F3`: Search across files (full-text search with replace)
- `Ctrl+B`: Open git branch switcher
- `Ctrl+Shift+I`: Open Timeline Mode (visual timeline of AI development)
- `Ctrl+Shift+K`: Open Claude Tasks Panel (view Claude Code task activity)
- `F1`: Show help window
- `Ctrl+F1`: What's New / Recent Features
- `F4`: Toggle voice commands (start/stop listening)

### Project Runner
- `F5`: Start/Stop project run
- `Shift+F5`: Force stop project run

### Default Quick Commands
- `Ctrl+Shift+C`: Quick command - Commit (Claude Code)
- `Ctrl+Shift+D`: Git Pull (stash, pull --rebase, pop)
- `Ctrl+Shift+U`: Git Push

## Configuration Schema

Config file location:
- **Windows**: `%APPDATA%\TerminalHost\config.json`
- **macOS**: `~/.config/TerminalHost/config.json`

```json
{
  "profiles": [],
  "settings": {
    "confirmOnClose": true,
    "showInSystemTray": false,
    "touchMode": false,
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

## Specifications Index

All specifications are documented in `docs/specs/`. Status legend:
- **Completed**: Fully implemented
- **Partial**: Core features done, some items remaining
- **Draft**: Specified but not started

### Feature Specifications

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| [GitAdvanced.md](docs/specs/GitAdvanced.md) | Commit history, staging, stash, blame, reflog, cherry-pick, branch compare, submodules | **Partial** | Tags, merge conflicts remaining |
| [WorkspaceLayout.md](docs/specs/WorkspaceLayout.md) | Sidebar layout, git worktree management, playgrounds | **Partial** | Active ports detection remaining |
| [SearchAndProductivity.md](docs/specs/SearchAndProductivity.md) | File search (Ctrl+F3), snippets, session management | **Partial** | Search implemented; snippets/sessions draft |
| [MultiAiAssistants.md](docs/specs/MultiAiAssistants.md) | Claude, Gemini, Codex, Copilot support per-project | **Completed** | Full per-project AI selection |
| [GitHubWorkflows.md](docs/specs/GitHubWorkflows.md) | Dashboard, PR review, test runner, markdown preview | **Completed** | All features implemented |
| [ToastNotifications.md](docs/specs/ToastNotifications.md) | Non-intrusive toast notifications with progress support | **Completed** | WPF airspace workaround included |
| [TimelineIDE.md](docs/specs/TimelineIDE.md) | Visual timeline for AI sessions, intents, worktrees | **Partial** | Core UI done; context files, stats remaining |
| [RemainingFeatures.md](docs/specs/RemainingFeatures.md) | Consolidated roadmap of remaining items | **Tracking** | ~54% complete (7/13 features) |
| [GitGuiParity.md](docs/specs/GitGuiParity.md) | Git GUI feature parity (vs GitKraken/Fork) | **Completed** | Phase 1-4 all complete |

### Architecture Specifications

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| [Panels.md](docs/specs/Panels.md) | Unified panel system (dock/popup/window states) | **Completed** | Panel transitions, .gitignore support |
| [CrossPlatform.md](docs/specs/CrossPlatform.md) | Cross-platform support (Windows + macOS) | **Completed** | Core/Windows/macOS/Avalonia projects |
| [Testing.md](docs/specs/Testing.md) | Unit tests (xUnit) and UI tests (FlaUI) strategy | **Partial** | Infrastructure done; coverage ongoing |
| [Versioning.md](docs/specs/Versioning.md) | Git tag versioning (MinVer) and auto-updates | **Draft** | Specified but not implemented |

## Remaining Work Summary

| Priority | Feature | Spec |
|----------|---------|------|
| **Medium** | Active Ports Detection | RemainingFeatures.md |
| **Low** | Playground Templates | RemainingFeatures.md |
| **Future** | Timeline IDE (remaining) | TimelineIDE.md |
| **Future** | Versioning & Auto-Updates | Versioning.md |

## Future Considerations

- Custom Profile Pairs: Different command pairs for different project types
- Plugin System: Extensible architecture for third-party integrations

## Success Criteria

1. User can run `host .` and get a terminal pair for the current directory.
2. User can switch between AI assistant and shell instantly.
3. Configuration and session state persist across restarts.
4. UI remains responsive and provides clear activity indicators.
