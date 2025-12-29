# CLAUDE.md (macOS - Avalonia)

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important: Documentation Maintenance

**Always keep PRD.md updated** when making changes to the codebase:
- When adding new features, document them in PRD.md
- When changing existing behavior, update the relevant sections
- When adding new configuration options, update the schema documentation
- When adding new keyboard shortcuts, update the shortcuts list
- Keep both CLAUDE.md and PRD.md in sync with the actual implementation

## Important: Testing Requirements

**Always verify tests when modifying features that have test coverage:**

### Running Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/TerminalHost.Tests

# Run specific test
dotnet test --filter "FullyQualifiedName~ConfigurationServiceTests"
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
  - `Services/ConfigurationServiceTests.cs`
  - `Services/GitStatusServiceTests.cs`
  - `Services/JsonFileServiceTests.cs`
  - `Services/ProcessServiceTests.cs`
  - `Services/ProjectDetectionServiceTests.cs`
  - `Services/SystemInfoServiceTests.cs`
  - `ViewModels/MainViewModelTests.cs`
  - `ViewModels/SettingsTabViewModelTests.cs`
  - `Integration/TerminalIntegrationTests.cs`

### Common Test Failures
- **Element not found**: Check if AutomationId exists and element is visible in current state
- **Timing issues**: Tests may need delays for data template loading or state transitions

## Important: Service Abstractions for Testability

**Do NOT use system APIs directly in ViewModels or Services.** Use the injected service abstractions instead to enable unit testing with mocks.

### Required Service Abstractions

| Instead of... | Use... |
|---------------|--------|
| `System.IO.File.*` / `System.IO.Directory.*` | `IFileSystem` |
| `MessageBox.Show()` / dialogs | `IDialogService` |
| `Process.Start()` | `IProcessService` |
| Running git commands directly | `IGitProcessRunner` |
| User feedback for operations | `IToastService` |
| Clipboard operations | `IClipboardService` |
| Timer/delay operations | `ITimerService` |
| UI thread dispatch | `IDispatcherService` |
| File/folder pickers | `IFilePickerService` / `IFolderPickerService` |
| Screen/display info | `IScreenService` |

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
Process.Start("open", "/Applications/TextEdit.app");

// GOOD - Uses injected service
_processService.Start("open", "/Applications/TextEdit.app");

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
- `SettingsView.axaml.cs` - Direct Process.Start and Directory operations
- `ProfileTerminalView.axaml.cs` - Direct Process.Start and Directory.Exists
- `FileViewerWindow.axaml.cs` - Direct MessageBox (unsaved changes prompt)

**Startup code (before DI):**
- `App.axaml.cs` - MessageBox for startup errors

**Non-DI ViewModels:**
- `SetupViewModel.cs` - Created with `new`, uses Process.Start for opening URLs

**Internal CLI services:**
- `GitHubService.cs`, `GitPrService.cs` - Direct Process.Start for running `gh` CLI (the process execution IS the service's purpose)

## Project Overview

**TerminalHost** (executable: `host`) is a macOS desktop application that manages terminal pairs for project directories. Each project tab contains two terminals: an AI assistant terminal (default: Claude Code, supports multiple AI CLIs) and a shell terminal (zsh), plus an optional run terminal for development servers. Allows easy switching between them without termination.

### Key Features
- **Multiple AI Assistant Support**: Claude Code, Gemini CLI, OpenAI Codex, GitHub Copilot (configurable)
- **GitHub Dashboard**: View PRs to review, your PRs, issues, and CI status
- **PR Review Mode**: Side-by-side diff viewer with comment threads
- **Task/Focus Mode**: Track work sessions with project linking
- **Search Across Files**: Full-text search with regex support and replace functionality
- **Test Runner Integration**: Run and view test results
- **Markdown Preview**: Live preview of markdown files
- **Timeline Mode**: Visualize Claude Code sessions with intent tracking and file changes
- **Git History Tools**: Commit history, file blame, file history, and reflog viewers
- **Git Worktrees**: Create and manage git worktrees for parallel development
- **Workspace Sidebar**: Alternative layout with sidebar navigation for workspaces
- **App Layout Modes**: Switch between Tabs mode and Sidebar mode

## Technology Stack

- **Framework**: Avalonia UI 11.2 on .NET 8
- **Terminal Control**: Custom MacTerminalControl with PTY via Python helper
- **MVVM**: CommunityToolkit.Mvvm for view models
- **Markdown**: Markdig with syntax highlighting
- **Charts**: LiveCharts for statistics
- **Dialogs**: MessageBox.Avalonia
- **Configuration**: JSON file stored in `~/Library/Application Support/TerminalHost/config.json`
- **Single Instance**: Unix domain socket IPC

## Build Commands

```bash
# Build the solution
dotnet build

# Run the application
dotnet run --project src/TerminalHost/TerminalHost

# Build for release
dotnet build -c Release

# Publish as macOS app bundle (ARM64)
dotnet publish src/TerminalHost/TerminalHost -c Release -r osx-arm64 --self-contained -o publish

# Publish for Intel Mac
dotnet publish src/TerminalHost/TerminalHost -c Release -r osx-x64 --self-contained -o publish

# Output locations
# Debug: src/TerminalHost/TerminalHost/bin/Debug/net8.0/osx-arm64/host
# Publish: publish/host
```

## Command Line Usage

```bash
# Open/focus app with no arguments
host

# Open project from current directory
host .

# Open project from specific path
host /Users/username/Projects/MyProject

# Using named argument
host --workdir /Users/username/Projects/MyProject
host -w /Users/username/Projects/MyProject

# Launch the setup and dependency checker window
host --setup

# Advanced/Testing arguments
host --disable-single-instance  # Allow multiple instances (or -multi)
host --user-data-dir "/path/to/config"  # Override configuration path (or -data)
```

If a project tab for the specified directory already exists, it will be focused instead of creating a new tab.

## Project Structure

The codebase follows a modular architecture with reusable components extracted into dedicated views and view models. Note: `.xaml` files are legacy WPF files excluded from compilation; `.axaml` files are the active Avalonia UI files.

```
TerminalHost/
├── TerminalHost.sln
├── src/TerminalHost/TerminalHost/
│   ├── App.axaml(.cs)                # Application entry, single instance handling
│   ├── MainWindow.axaml(.cs)         # Main window with native macOS menu, keyboard shortcuts
│   ├── MainWindow.CommandPalette.cs  # Partial class: command palette logic
│   ├── MainWindow.Tabs.cs            # Partial class: tab management logic
│   ├── GlobalUsings.cs               # Global using statements
│   ├── AssemblyInfo.cs               # Assembly metadata
│   ├── Converters/
│   │   └── Converters.cs             # Avalonia value converters
│   ├── Resources/
│   │   ├── TabContentTemplates.axaml # DataTemplates for tab content
│   │   ├── Converters.axaml          # Converter resources
│   │   └── pty_helper.py             # Python PTY helper for terminal
│   ├── Styles/
│   │   ├── Colors.axaml              # Color definitions
│   │   ├── Buttons.axaml             # Button styles
│   │   ├── Controls.axaml            # Control styles
│   │   ├── ScrollBars.axaml          # ScrollBar styles
│   │   └── Typography.axaml          # Font/text styles
│   ├── Assets/
│   │   └── Fonts/                    # Nerd Font for terminal icons
│   ├── Controls/
│   │   ├── MacTerminalControl.cs     # macOS terminal control with PTY
│   │   ├── DraggablePopup.axaml(.cs) # Draggable popup base control
│   │   ├── DiffViewer.axaml(.cs)     # Unified diff viewer
│   │   ├── SideBySideDiffViewer.axaml(.cs) # Side-by-side diff viewer
│   │   ├── MarkdownViewer.axaml(.cs) # Markdown rendering control
│   │   └── PrCommentThread.axaml(.cs) # PR comment thread display
│   ├── Domain/
│   │   ├── Profile.cs                # Configuration template for terminal sessions
│   │   ├── TerminalSession.cs        # Running terminal instance
│   │   ├── TerminalPair.cs           # Paired AI + shell + run terminals
│   │   ├── SessionState.cs           # Running/Exited enum
│   │   ├── AppConfiguration.cs       # Root config with settings, layout modes
│   │   ├── AppConstants.cs           # Application constants
│   │   ├── AppLayoutMode.cs          # App layout mode enum (Tabs/Sidebar)
│   │   ├── GitStatus.cs              # Git repository status model
│   │   ├── GitFileStatus.cs          # Git file-level status
│   │   ├── GitBranch.cs              # Git branch model
│   │   ├── GitStashEntry.cs          # Git stash entry model
│   │   ├── GitBlame.cs               # Git blame information model
│   │   ├── GitCommit.cs              # Git commit model
│   │   ├── GitCommitDetails.cs       # Detailed commit information
│   │   ├── GitCommitFile.cs          # File changed in a commit
│   │   ├── GitReflogEntry.cs         # Git reflog entry model
│   │   ├── WorktreeInfo.cs           # Git worktree information
│   │   ├── CreateWorktreeDialogResult.cs # Worktree creation result
│   │   ├── QuickCommand.cs           # Quick command with shortcut
│   │   ├── LinkPattern.cs            # Custom link pattern definition
│   │   ├── PaletteCommand.cs         # Command palette item
│   │   ├── RunConfiguration.cs       # Run configuration for project runner
│   │   ├── ProjectType.cs            # Project type detection model
│   │   ├── ClaudeCommand.cs          # Claude slash command from .md files
│   │   ├── RunState.cs               # Run terminal state enum
│   │   ├── FileSystemNode.cs         # File explorer tree node with git status
│   │   ├── FileIconMapper.cs         # File extension to icon mapping
│   │   ├── ITerminalControl.cs       # Terminal control interface
│   │   ├── TerminalTheme.cs          # Terminal color theme
│   │   ├── UsageStats.cs             # Usage statistics model
│   │   ├── DirectoryUsageStats.cs    # Per-directory statistics
│   │   ├── Dependency.cs             # Dependency checker model
│   │   ├── AiAssistant.cs            # AI CLI assistant configuration
│   │   ├── AiAssistantSwitchEventArgs.cs # AI switch event args
│   │   ├── FocusModeState.cs         # Focus mode state
│   │   ├── FocusTask.cs              # Focus task model
│   │   ├── FocusTaskStatus.cs        # Task status enum
│   │   ├── QuickNote.cs              # Quick note model
│   │   ├── GitHubPullRequest.cs      # GitHub PR model
│   │   ├── GitHubIssue.cs            # GitHub issue model
│   │   ├── GitHubWorkflowRun.cs      # GitHub CI workflow run
│   │   ├── GitHubPrFile.cs           # PR changed file model
│   │   ├── GitHubRepository.cs       # GitHub repository model
│   │   ├── GitPrDetails.cs           # PR details model
│   │   ├── PrComments.cs             # PR comments container
│   │   ├── PrReviewComment.cs        # PR review comment model
│   │   ├── RepositoryItem.cs         # Repository quick access item
│   │   ├── DiffViewMode.cs           # Diff view mode enum
│   │   ├── ParsedDiff.cs             # Parsed diff model
│   │   ├── TestResult.cs             # Test result model
│   │   ├── SearchResult.cs           # Search result model
│   │   ├── SearchMatch.cs            # Search match model
│   │   ├── Workspace.cs              # Workspace model for sidebar mode
│   │   ├── TimelineState.cs          # Timeline mode state
│   │   ├── ClaudeCodeSession.cs      # Claude Code session for timeline
│   │   ├── ClaudeSessionStatus.cs    # Session status enum
│   │   ├── Intent.cs                 # Intent model for timeline
│   │   ├── IntentStatus.cs           # Intent status enum
│   │   ├── TimelineFileChange.cs     # File change in timeline
│   │   ├── TimeScale.cs              # Timeline scale enum
│   │   ├── OrphanSession.cs          # Orphan session model
│   │   ├── FilePreviewRequestedEventArgs.cs  # File preview event args
│   │   └── FileEditRequestedEventArgs.cs     # File edit event args
│   ├── Services/
│   │   ├── IConfigurationService.cs / ConfigurationService.cs    # JSON config load/save
│   │   ├── IDialogService.cs / DialogService.cs                  # Themed dialog service
│   │   ├── IProfileRegistry.cs / ProfileRegistry.cs              # Profile management
│   │   ├── ISessionManager.cs / SessionManager.cs                # Session lifecycle
│   │   ├── ISingleInstanceService.cs / SingleInstanceService.cs  # Unix domain socket IPC (interface in App.axaml.cs)
│   │   ├── ISystemTrayService.cs / SystemTrayService.cs          # Menu bar icon
│   │   ├── ITerminalControlFactory.cs / TerminalControlFactory.cs # Terminal control factory
│   │   ├── IPtyService.cs / MacPtyService.cs                     # PTY service for macOS
│   │   ├── IGitStatusService.cs / GitStatusService.cs            # Git command execution
│   │   ├── IGitProcessRunner.cs / GitProcessRunner.cs            # Git process runner
│   │   ├── IGitHubService.cs / GitHubService.cs                  # GitHub CLI integration
│   │   ├── IGitPrService.cs / GitPrService.cs                    # PR operations
│   │   ├── IGitWorktreeService.cs / GitWorktreeService.cs        # Git worktree operations
│   │   ├── IFilePreviewService.cs / FilePreviewService.cs        # File preview loading
│   │   ├── IFileEditService.cs / FileEditService.cs              # File editing
│   │   ├── IFileExplorerService.cs / FileExplorerService.cs      # File explorer + watcher
│   │   ├── ILinkDetectionService.cs / LinkDetectionService.cs    # Clickable link detection
│   │   ├── IProjectDetectionService.cs / ProjectDetectionService.cs # Project type detection
│   │   ├── IRunUrlDetectionService.cs / RunUrlDetectionService.cs   # Localhost URL detection
│   │   ├── IClaudeCommandService.cs / ClaudeCommandService.cs    # Claude commands detection
│   │   ├── IToastService.cs / ToastService.cs                    # Toast notifications
│   │   ├── ITaskService.cs / TaskService.cs                      # Task/focus mode service
│   │   ├── IAiAssistantService.cs / AiAssistantService.cs        # AI assistant management
│   │   ├── IMarkdownService.cs / MarkdownService.cs              # Markdown rendering
│   │   ├── ISearchService.cs / SearchService.cs                  # File search service
│   │   ├── ITestRunnerService.cs / TestRunnerService.cs          # Test runner integration
│   │   ├── ITimelineService.cs / TimelineService.cs              # Timeline mode service
│   │   ├── TranscriptParserService.cs                            # Claude transcript parsing
│   │   ├── ShortcutConflictService.cs                            # Keyboard shortcut conflict detection
│   │   ├── ITimerService.cs / TimerService.cs                    # Timer abstractions
│   │   ├── IDispatcherService.cs / DispatcherService.cs          # UI thread dispatch
│   │   ├── IFilePickerService.cs / FilePickerService.cs          # File picker dialog
│   │   ├── IFolderPickerService.cs / FolderPickerService.cs      # Folder picker dialog
│   │   ├── IScreenService.cs / ScreenService.cs                  # Screen/display info
│   │   ├── IClipboardService.cs / ClipboardService.cs            # Clipboard operations
│   │   ├── IStatisticsService.cs / StatisticsService.cs          # Usage statistics
│   │   ├── ISystemInfoService.cs / SystemInfoService.cs          # System information
│   │   ├── IFileSystem.cs                                        # File system abstraction (impl in same file)
│   │   ├── IProcessService.cs                                    # Process abstraction (impl in same file)
│   │   ├── JsonFileService.cs                                    # JSON file operations
│   │   ├── JsonSyntaxHighlighter.cs                              # JSON highlighting
│   │   ├── DiffParserService.cs                                  # Diff parsing
│   │   ├── GitOperationResult.cs                                 # Git operation result
│   │   └── SyntaxHighlighting/                                   # Syntax highlighters
│   │       ├── ISyntaxHighlighter.cs / SyntaxHighlighterBase.cs
│   │       ├── CSharpHighlighter.cs
│   │       ├── CsvHighlighter.cs
│   │       ├── DiffHighlighter.cs
│   │       ├── JavaScriptHighlighter.cs
│   │       ├── JsonHighlighter.cs
│   │       ├── MarkdownHighlighter.cs
│   │       ├── PlainTextHighlighter.cs
│   │       ├── PythonHighlighter.cs
│   │       └── XmlHighlighter.cs
│   ├── ViewModels/
│   │   ├── ITabViewModel.cs                  # Interface for tab view models
│   │   ├── MainViewModel.cs                  # Main window logic, popup state
│   │   ├── TerminalPairTabViewModel.cs       # Tab with paired terminals
│   │   ├── TerminalTabViewModel.cs           # Base terminal tab
│   │   ├── ProfileTerminalTabViewModel.cs    # Tab with single profile terminal
│   │   ├── DashboardTabViewModel.cs          # GitHub Dashboard tab
│   │   ├── SettingsTabViewModel.cs           # Settings editor tab
│   │   ├── ProfilesTabViewModel.cs           # Profile management tab
│   │   ├── StatisticsTabViewModel.cs         # Usage statistics tab
│   │   ├── TimelineTabViewModel.cs           # Timeline mode tab
│   │   ├── ProjectStatViewModel.cs           # Project statistics
│   │   ├── SetupViewModel.cs                 # Setup/dependency checker
│   │   ├── ScratchPadViewModel.cs            # Scratch pad notes
│   │   ├── GitBranchViewModel.cs             # Git branch operations
│   │   ├── GitFilesViewModel.cs              # Git changed files + diff
│   │   ├── GitStashViewModel.cs              # Git stash management
│   │   ├── CommitHistoryViewModel.cs         # Git commit history viewer
│   │   ├── FileBlameViewModel.cs             # Git file blame viewer
│   │   ├── FileHistoryViewModel.cs           # Git file history viewer
│   │   ├── FileChangeViewModel.cs            # File change display
│   │   ├── ReflogViewModel.cs                # Git reflog viewer
│   │   ├── ManageWorktreesViewModel.cs       # Git worktree management
│   │   ├── DetectedLinksViewModel.cs         # Terminal link detection
│   │   ├── FileViewerViewModel.cs            # Unified file preview/edit
│   │   ├── FilePreviewViewModel.cs           # File preview state
│   │   ├── FileExplorerViewModel.cs          # File explorer tree
│   │   ├── MarkdownPreviewViewModel.cs       # Markdown preview
│   │   ├── TaskPanelViewModel.cs             # Task/focus mode panel
│   │   ├── PrReviewViewModel.cs              # PR review mode
│   │   ├── RepositorySwitcherViewModel.cs    # Repository quick access
│   │   ├── TestResultsViewModel.cs           # Test results display
│   │   ├── SearchAcrossFilesViewModel.cs     # Search across files
│   │   ├── WorkspaceSidebarViewModel.cs      # Workspace sidebar for sidebar mode
│   │   ├── SessionBlockViewModel.cs          # Timeline session block
│   │   ├── IntentRowViewModel.cs             # Timeline intent row
│   │   └── ToastViewModel.cs                 # Individual toast state
│   └── Views/
│       ├── TabStrip.axaml(.cs)               # Tab bar with drag-drop, overflow
│       ├── SettingsView.axaml(.cs)           # Settings editor UI
│       ├── ProfilesView.axaml(.cs)           # Profile management UI
│       ├── StatisticsView.axaml(.cs)         # Usage statistics UI
│       ├── DashboardView.axaml(.cs)          # GitHub Dashboard UI
│       ├── SetupWindow.axaml(.cs)            # Setup/dependency checker
│       ├── ScratchPadView.axaml(.cs)         # Scratch pad content
│       ├── FileExplorerView.axaml(.cs)       # File explorer panel
│       ├── FileViewerView.axaml(.cs)         # File preview/edit view
│       ├── FileViewerWindow.axaml(.cs)       # Detached file viewer window
│       ├── MarkdownPreviewWindow.axaml(.cs)  # Markdown preview window
│       ├── TimelineModeView.axaml(.cs)       # Timeline visualization view
│       ├── WorkspaceSidebar.axaml(.cs)       # Workspace sidebar for sidebar mode
│       ├── ToastContainerView.axaml(.cs)     # Toast container
│       ├── ToastItemView.axaml(.cs)          # Individual toast UI
│       ├── ToastWindow.axaml(.cs)            # Toast overlay window
│       ├── Dialogs/
│       │   ├── InputDialog.axaml(.cs)        # Input dialog
│       │   ├── NotificationDialog.axaml(.cs) # Themed notification dialog
│       │   └── CreateWorktreeDialog.axaml(.cs) # Git worktree creation dialog
│       ├── Tabs/
│       │   ├── TerminalPairView.axaml(.cs)   # Terminal pair layout
│       │   └── ProfileTerminalView.axaml(.cs) # Single profile terminal
│       └── Popups/
│           ├── TabDropdownView.axaml(.cs)    # Tab overflow dropdown
│           ├── TabSwitcherView.axaml(.cs)    # Tab search/switcher (Cmd+Shift+T)
│           ├── CommandPaletteView.axaml(.cs) # Command palette (Cmd+Shift+P)
│           ├── HelpView.axaml(.cs)           # Help/shortcuts popup (F1)
│           ├── GitBranchView.axaml(.cs)      # Git branch switcher (Cmd+B)
│           ├── GitFilesView.axaml(.cs)       # Git changes panel (Cmd+G)
│           ├── GitStashView.axaml(.cs)       # Git stash panel (Cmd+Shift+S)
│           ├── CommitHistoryView.axaml(.cs)  # Git commit history (Cmd+Shift+H)
│           ├── FileBlameView.axaml(.cs)      # Git file blame viewer
│           ├── FileHistoryView.axaml(.cs)    # Git file history viewer
│           ├── ReflogView.axaml(.cs)         # Git reflog viewer (Cmd+Shift+G)
│           ├── ManageWorktreesView.axaml(.cs) # Git worktree management
│           ├── DetectedLinksView.axaml(.cs)  # Detected links popup
│           ├── FileViewerPopup.axaml(.cs)    # File viewer popup (Cmd+O)
│           ├── FilePreviewView.axaml(.cs)    # File preview popup content
│           ├── TaskPanelView.axaml(.cs)      # Task/focus mode (Cmd+T)
│           ├── QuickTaskView.axaml(.cs)      # Quick task input
│           ├── QuickNoteView.axaml(.cs)      # Quick note input
│           ├── PrReviewView.axaml(.cs)       # PR review popup (Cmd+Shift+R)
│           ├── TestResultsView.axaml(.cs)    # Test results popup
│           ├── RepositorySwitcherView.axaml(.cs) # Repository switcher
│           └── SearchAcrossFilesView.axaml(.cs)  # Search across files (Cmd+F)
└── tests/
    └── TerminalHost.Tests/
        ├── Services/                         # Service unit tests
        ├── ViewModels/                       # ViewModel unit tests
        └── Integration/                      # Integration tests
```

## Architecture

### Terminal Pairs
Each project directory opens as a `TerminalPair` containing:
- **AI Terminal**: Runs configured AI CLI (default: Claude Code, supports multiple AI assistants)
- **Shell Terminal**: Runs shell (default: zsh)
- **Run Terminal**: Optional third terminal for running development servers (created on demand)

AI and Shell terminals are created simultaneously and always visible in a split view layout. The Run terminal appears on the right when activated.

### AI Assistant Support
The application supports multiple AI CLI assistants that can be configured and switched per-project:
- **Claude Code** (default): `~/.local/bin/claude`
- **Gemini CLI**: `gemini`
- **OpenAI Codex**: `codex`
- **GitHub Copilot**: `gh copilot`
- **Custom**: User-defined AI CLI

### Working Directory Handling
The terminal control supports setting the working directory directly. Commands are launched with the appropriate working directory:
- zsh: Launched in the project directory
- AI commands: Launched with working directory set to project path

### Single Instance Behavior
- First instance creates Unix domain socket server
- Subsequent instances connect to socket and send args, then exit
- Running `host .` twice for the same directory focuses existing tab

## Keyboard Shortcuts

### Tab Navigation
- `Ctrl+Tab` / `Ctrl+Shift+Tab`: Cycle through project tabs
- `Cmd+1-9`: Jump to specific tab
- `Cmd+Shift+T`: Open tab switcher (search and switch tabs)
- `Cmd+W`: Close current tab
- `Middle-click tab`: Close tab
- `Drag tab`: Reorder tabs

### Terminal
- `Cmd+\``: Switch between AI/Shell terminal
- `Links button`: Shows detected URLs and file paths from terminal output (toolbar)

### File Operations
- `Cmd+N`: Open new project (folder picker)
- `Cmd+E`: Open current folder in Finder
- `Cmd+O`: Open file viewer (preview mode, supports images)
- `Cmd+Shift+E`: Open file viewer (edit mode)
- `Cmd+Shift+F`: Toggle file explorer panel (tree view with git status)
- `Cmd+F`: Open search across files (full-text search with replace)

### Application
- `Cmd+,`: Open settings editor
- `Cmd+Shift+P`: Open command palette
- `Cmd+Shift+N`: Open scratch pad (notes)
- `Cmd+G`: Open git changes panel (modified files + diffs)
- `Cmd+B`: Open git branch switcher
- `Cmd+Shift+H`: Open commit history viewer
- `Cmd+Shift+S`: Open git stash panel
- `Cmd+Shift+G`: Open git reflog viewer
- `Cmd+Shift+R`: Open PR review popup
- `Cmd+Shift+L`: Toggle app layout mode (Tabs/Sidebar)
- `Cmd+Shift+I`: Toggle timeline mode
- `Cmd+T`: Open task panel (focus mode)
- `Cmd+Ctrl+F`: Toggle full screen
- `Cmd+M`: Minimize window
- `F1`: Show help window
- `Escape`: Close all popups

### Project Runner
- `F5`: Start/Stop project run
- `Shift+F5`: Force stop project run

### Default Quick Commands
- `Ctrl+Shift+C`: Quick command - Commit (Claude Code)
- `Ctrl+Shift+R`: Quick command - Rate Code (Claude Code)
- `Ctrl+Shift+V`: Quick command - Review PR (Claude Code)
- `Ctrl+Shift+D`: Quick command - Git Pull (Shell)
- `Ctrl+Shift+U`: Quick command - Git Push (Shell)
- `Ctrl+Shift+L`: Quick command - Launch IDE (Shell)
- `Ctrl+Shift+B`: Quick command - Build (Shell)

## Configuration Schema

Config file: `~/Library/Application Support/TerminalHost/config.json`

```json
{
  "profiles": [],
  "settings": {
    "confirmOnClose": true,
    "showInSystemTray": false,
    "customCommand": "/Users/username/.local/bin/claude",
    "customCommandName": "Claude Code",
    "customCommandIcon": "",
    "shellCommand": "/bin/zsh",
    "shellCommandName": "Zsh",
    "shellCommandIcon": "",
    "claudeCommandShortcuts": {},
    "customPaths": [],
    "dashboard": {
      "enabled": true,
      "refreshIntervalMinutes": 5,
      "watchedOrgs": [],
      "excludedRepos": [],
      "showCIStatus": true,
      "showOnStartup": false
    },
    "repositories": {
      "scanPaths": [],
      "favorites": [],
      "cloneDirectory": "",
      "recentPaths": [],
      "maxRecentItems": 20
    },
    "testing": {
      "runOnSave": false,
      "showResultsPanel": true,
      "autoFocusOnFailure": true,
      "defaultTestCommand": null
    },
    "markdown": {
      "autoReload": true,
      "defaultPanelPosition": "right",
      "syncScroll": true
    },
    "layoutMode": "Tabs",
    "sidebarWidth": 250,
    "gitAutoFetch": true,
    "gitAutoFetchIntervalSeconds": 60
  },
  "windowState": {
    "left": 100,
    "top": 100,
    "width": 1200,
    "height": 800,
    "isMaximized": false
  },
  "openFolders": [
    "/Users/username/Projects/Project1",
    "/Users/username/Projects/Project2"
  ],
  "lastSelectedFolder": "/Users/username/Projects/Project1",
  "directorySettings": {
    "/users/username/projects/project1": {
      "layoutMode": "HorizontalSplit",
      "splitRatio": 0.6,
      "activeTerminal": "Custom",
      "isRunTerminalVisible": false,
      "runSplitRatio": 0.3,
      "isExplorerVisible": false,
      "explorerSplitRatio": 0.25,
      "activeAiAssistantId": null,
      "runConfigurations": [],
      "activeRunConfigurationId": null,
      "detectedProjectType": null
    }
  },
  "quickCommands": [
    {
      "id": "commit",
      "label": "Commit",
      "icon": "",
      "text": "commit",
      "target": "Custom",
      "appendNewline": true,
      "useUserInput": true,
      "shortcut": "Ctrl+Shift+C"
    }
  ],
  "linkPatterns": [],
  "scratchPads": {},
  "globalScratchPad": "",
  "projectTypes": [],
  "commandPaletteMru": [],
  "focusMode": {
    "isEnabled": false,
    "currentTaskId": null
  },
  "tasks": [],
  "quickNotes": [],
  "aiAssistants": [
    {
      "id": "claude",
      "name": "Claude Code",
      "command": "/Users/username/.local/bin/claude",
      "icon": "",
      "detectionCommand": "claude --version",
      "enabled": true,
      "isDefault": true
    }
  ],
  "layoutMode": "Tabs",
  "sidebarWidth": 250,
  "recentWorkspaces": [],
  "maxRecentWorkspaces": 20,
  "gitAutoFetch": true,
  "gitAutoFetchIntervalSeconds": 60,
  "timelineState": {
    "scale": "Minutes",
    "showFileChanges": true,
    "expandedSessions": []
  }
}
```

### Persistence Features
- **Window State**: Position, size, and maximized state are saved on close and restored on startup
- **Open Folders**: Previously open project tabs are automatically restored on startup
- **Last Selected Folder**: The active tab is restored on startup
- **Directory Settings**: Layout mode, split ratios, active terminal, explorer visibility, and AI assistant selection are saved per directory
- **Focus Mode State**: Task state and focus mode settings persist across sessions

### Terminal Activity Indicators
Tabs show an animated spinning indicator when terminals are producing output:
- Uses terminal control's output events to track output
- Terminal is "active" if output received within last 2 seconds
- Spinner appears/animates when active, hidden when idle

### Task/Focus Mode
Focus mode allows you to concentrate on a specific task:
- Create and track tasks with time logging
- Link tasks to projects (directories)
- Focus mode filters visible tabs to task-linked projects
- Tasks can be linked to GitHub PRs/branches
- Quick notes for capturing ideas

### GitHub Dashboard
The dashboard provides an overview of GitHub activity (requires `gh` CLI):
- PRs awaiting your review
- Your open PRs and their status
- Issues assigned to you
- Failed CI workflow runs
- Quick checkout and review mode access

### Timeline Mode
Timeline mode (`Cmd+Shift+I`) visualizes Claude Code sessions and their activity:
- **Session Blocks**: Shows Claude Code sessions as timeline blocks
- **Intent Tracking**: Displays user intents/prompts with their status (pending, in-progress, completed, failed)
- **File Changes**: Shows files modified during each session
- **Time Scale**: Adjustable scale (Minutes, Hours, Days)
- **Transcript Parsing**: Parses Claude Code transcripts from `~/.claude/projects/`

### Git History Tools
Comprehensive git history exploration:
- **Commit History** (`Cmd+Shift+H`): Browse repository commits with details and file changes
- **File Blame**: View line-by-line blame information for any file
- **File History**: Track changes to a specific file over time
- **Reflog** (`Cmd+Shift+G`): View and recover from git reference log

### Git Worktrees
Manage multiple working directories for the same repository:
- **Create Worktree**: Create new worktree from branch or commit
- **Manage Worktrees**: View, open, and remove existing worktrees
- **Parallel Development**: Work on multiple branches simultaneously

### App Layout Modes
Switch between two application layout modes (`Cmd+Shift+L`):
- **Tabs Mode** (default): Traditional tab-based navigation with TabStrip
- **Sidebar Mode**: Workspace sidebar on the left for project navigation

### Workspace Sidebar Git Actions
In Sidebar mode, the Open Projects context menu provides git operations:
- **Git Fetch**: Fetch from all remotes
- **Git Pull (Rebase)**: Pull with rebase
- **Git Push**: Push to remote
- **Open in Finder**: Open project folder in Finder

### Git Auto-fetch
Automatically fetches from git remotes periodically to keep behind counts up to date:
- **Enabled by default**: `gitAutoFetch: true`
- **Configurable interval**: `gitAutoFetchIntervalSeconds: 60` (minimum 30 seconds)
- Runs for all open project tabs in the background
- Silently ignores network errors
