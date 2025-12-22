# Cross-Platform Preparation - Product Requirements Document

## Overview

This document describes the architectural changes made to TerminalHost to separate platform-agnostic code from Windows-specific code. The goal is to make future cross-platform development easier, not to achieve cross-platform compatibility immediately.

**Status**: Phase 1 Complete - Code Separation

## Project Structure

The solution now contains four projects:

```
TerminalHost/
├── src/
│   ├── TerminalHost.Core/           # Platform-agnostic library (.NET 8)
│   │   ├── Domain/                  # 44 domain models
│   │   ├── Interfaces/              # 23 service interfaces
│   │   ├── Services/                # 17 service implementations
│   │   └── ViewModels/              # 5 portable ViewModels
│   │
│   ├── TerminalHost.Windows/        # Windows-specific library (.NET 8 Windows)
│   │   ├── Interfaces/              # 1 Windows-specific interface
│   │   ├── Services/                # 4 Windows service implementations
│   │   └── Platform/                # 1 P/Invoke helper
│   │
│   └── TerminalHost/                # Main WPF application (.NET 8 Windows)
│       ├── Domain/                  # WPF-coupled domain models
│       ├── Services/                # WPF-coupled services
│       ├── ViewModels/              # WPF-coupled ViewModels
│       ├── Views/                   # XAML views
│       └── Resources/               # XAML resources
│
└── tests/
    ├── TerminalHost.Tests/          # Unit tests
    └── TerminalHost.UITests/        # UI automation tests
```

## What's Portable (TerminalHost.Core)

### Domain Models (44 files)
All domain models are portable and contain no platform-specific code:
- Configuration: `AppConfiguration`, `AppSettings`, `DirectorySettings`, `Profile`
- Git: `GitStatus`, `GitFileStatus`, `GitBranch`, `GitPrDetails`, `PrComments`
- GitHub: `GitHubIssue`, `GitHubPullRequest`, `GitHubRepository`, `GitHubWorkflowRun`
- UI State: `RunConfiguration`, `ProjectType`, `QuickCommand`, `LinkPattern`
- Tasks: `FocusTask`, `FocusModeState`, `TestResult`
- Files: `DetectedLink`, `ParsedDiff`, `FileEditResult`, `FileSaveResult`

### Service Interfaces (23 files)
All service interfaces use only portable types:
- `IConfigurationService` - App configuration persistence
- `IFileSystem` - File system abstraction
- `IProcessService` - Process execution abstraction
- `IGitProcessRunner` - Git CLI wrapper
- `IGitStatusService` - Git status parsing
- `IProjectDetectionService` - Project type detection
- `IDialogService` - User dialog abstraction
- `IToastService` - Toast notification abstraction
- And 15 more...

### Service Implementations (17 files)
Platform-agnostic service implementations:
- `ConfigurationService` - JSON file-based configuration
- `GitProcessRunner` - Cross-platform git CLI execution
- `GitStatusService` - Git status parsing
- `ProfileRegistry` - Profile management
- `ProjectDetectionService` - Detects .NET, Node.js, Python projects
- `StatisticsService` - Usage tracking
- `MarkdownService` - Markdown to HTML conversion
- `DiffParserService` - Unified diff parsing
- And 9 more...

### ViewModels (5 files)
Portable ViewModels using CommunityToolkit.Mvvm:
- `SettingsTabViewModel` - Settings editor logic
- `ProfilesTabViewModel` - Profile management logic
- `StatisticsTabViewModel` - Usage statistics (uses LiveChartsCore)
- `ToastViewModel` - Toast notification state
- `ProjectStatViewModel` - Project statistics display

## What's Windows-Specific

### TerminalHost.Windows Project

**Interfaces (1 file)**:
- `ISystemTrayService` - System tray icon management (uses `Window`, `ToolTipIcon`)

**Services (4 files)**:
- `SystemTrayService` - NotifyIcon implementation (Windows Forms)
- `ToastService` - Toast notifications with DispatcherTimer
- `LinkDetectionService` - Opens links via `explorer.exe`
- `SingleInstanceService` - Mutex + Named Pipes for single instance

**Platform Helpers (1 file)**:
- `DarkModeHelper` - P/Invoke for Windows dark mode APIs

### Main App (TerminalHost)

**WPF-Coupled Domain**:
- `TerminalSession` - Uses `EasyTerminalControl`, P/Invoke
- `TerminalPair` - References `TerminalSession`
- `FileSystemNode` - Uses `System.Windows.Media.Brush`

**WPF-Coupled Services**:
- `DialogService` - Creates WPF dialog windows
- `TerminalControlFactory` - Creates `EasyTerminalControl`
- `FileExplorerService` - Uses `FileSystemNode` with WPF types
- `FilePreviewService` - FlowDocument, BitmapImage handling
- `SessionManager` - Manages `TerminalSession` lifecycle

**WPF-Coupled ViewModels**:
- `MainViewModel` - Uses `FolderBrowserDialog` (timers now abstracted via ITimerService)
- `TerminalPairTabViewModel` - Uses `GridLength`, `GridUnitType`
- `FileViewerViewModel` - Uses `FlowDocument`, `BitmapImage` (timers now abstracted)
- `GitBranchViewModel` - Uses `CollectionViewSource`
- `SetupViewModel` - Uses `System.Windows.Media.Fonts`
- And more...

## Implemented Abstractions

These abstractions have been implemented to improve cross-platform readiness:

### Timer Abstraction (✅ Implemented)
```csharp
// In TerminalHost.Core.Interfaces
public interface ITimerService
{
    IAppTimer CreateTimer(TimeSpan interval, Action callback);
}

public interface IAppTimer : IDisposable
{
    void Start();
    void Stop();
    bool IsEnabled { get; set; }
    TimeSpan Interval { get; set; }
}
```
**Implementation**: `TimerService` in TerminalHost.Windows uses WPF `DispatcherTimer`
**Used by**: MainViewModel (4 timers), DashboardTabViewModel, FileViewerViewModel, ScratchPadViewModel

### UI Thread Dispatcher (✅ Implemented)
```csharp
// In TerminalHost.Core.Interfaces
public interface IDispatcherService
{
    void BeginInvoke(Action action);
    void Invoke(Action action);
    Task InvokeAsync(Func<Task> action);
    bool CheckAccess();
}
```
**Implementation**: `DispatcherService` in TerminalHost.Windows uses WPF `Application.Current.Dispatcher`
**Used by**: MainViewModel (command filtering), MarkdownPreviewViewModel (file watcher)

## Missing Abstractions for Cross-Platform

To achieve full cross-platform support, these additional abstractions would need to be created:

### Folder Picker
```csharp
// Replace FolderBrowserDialog
public interface IFolderPickerService
{
    string? PickFolder(string? initialPath = null);
}
```

### Document Rendering
```csharp
// Replace FlowDocument
public interface IDocumentRenderer
{
    object CreateDocument(string content, string contentType);
}
```

### Image Loading
```csharp
// Replace BitmapImage
public interface IImageService
{
    object LoadImage(string path);
    object LoadImage(Stream stream);
}
```

### Terminal Emulator
The biggest challenge - replacing `EasyWindowsTerminalControl`:
- Avalonia: Use `AvaloniaTerminal` or similar
- macOS: Use native `NSTerminal` wrapper
- Linux: Use `VTE` widget wrapper

## Platform Target Considerations

### macOS
- **UI Framework**: Avalonia UI or .NET MAUI
- **Terminal**: Native PTY via `forkpty()`, or use Avalonia terminal control
- **System Tray**: NSStatusItem via P/Invoke or Avalonia extension
- **Single Instance**: Unix domain sockets instead of named pipes

### Linux
- **UI Framework**: Avalonia UI (best GTK integration)
- **Terminal**: VTE widget or libvterm
- **System Tray**: AppIndicator/StatusNotifier via D-Bus
- **Single Instance**: Unix domain sockets or file locks

### Web (Future)
- **UI Framework**: Blazor WebAssembly
- **Terminal**: xterm.js with WebSocket backend
- **No System Tray**: Use browser notifications instead

## Migration Checklist

### Completed
- [x] Create TerminalHost.Core library
- [x] Move domain models to Core
- [x] Move service interfaces to Core
- [x] Move platform-agnostic services to Core
- [x] Move portable ViewModels to Core
- [x] Create TerminalHost.Windows library
- [x] Move Windows-specific services to Windows library
- [x] Update all references and usings
- [x] Verify build succeeds
- [x] Verify all tests pass (52/52)

### Future Work (for cross-platform)
- [x] Create ITimerService abstraction
- [x] Create IDispatcherService abstraction
- [ ] Create IFolderPickerService abstraction
- [ ] Abstract FlowDocument usage
- [ ] Abstract BitmapImage usage
- [ ] Research cross-platform terminal controls
- [ ] Create Avalonia UI project (TerminalHost.Avalonia)
- [ ] Implement platform-specific services for macOS/Linux

## Dependencies

### TerminalHost.Core
- `net8.0` (cross-platform)
- CommunityToolkit.Mvvm 8.4.0
- Markdig 0.44.0
- Markdig.SyntaxHighlighting 1.1.7
- LiveChartsCore.SkiaSharpView 2.0.0-rc4.5

### TerminalHost.Windows
- `net8.0-windows`
- TerminalHost.Core (project reference)
- Hardcodet.NotifyIcon.Wpf 2.0.1
- EasyWindowsTerminalControl 1.0.9

### TerminalHost (Main App)
- `net8.0-windows`
- TerminalHost.Core (project reference)
- TerminalHost.Windows (project reference)
- All Windows-specific NuGet packages

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        TerminalHost                              │
│                    (WPF Application)                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐ │
│  │   Views/    │  │ ViewModels/ │  │      Services/          │ │
│  │   (XAML)    │  │ (WPF-bound) │  │  (WPF-coupled impls)    │ │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    TerminalHost.Windows                          │
│                  (Windows Class Library)                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐ │
│  │ Interfaces/ │  │  Services/  │  │       Platform/         │ │
│  │ (Win APIs)  │  │ (Win impls) │  │  (P/Invoke helpers)     │ │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      TerminalHost.Core                           │
│                  (Platform-Agnostic Library)                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐ │
│  │   Domain/   │  │ Interfaces/ │  │       Services/         │ │
│  │  (Models)   │  │ (Contracts) │  │  (Portable impls)       │ │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘ │
│                  ┌─────────────────────────────────────────┐   │
│                  │            ViewModels/                   │   │
│                  │        (Portable logic)                  │   │
│                  └─────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

## Conclusion

The codebase has been restructured to clearly separate platform-agnostic code (~60%) from Windows-specific code (~40%). This separation:

1. **Improves maintainability** - Clear boundaries between platform code
2. **Enables testing** - Core logic can be unit tested without UI
3. **Prepares for cross-platform** - Core can be reused with different UI frameworks
4. **Documents dependencies** - Clear visibility into what's portable vs. platform-specific

The main effort for true cross-platform support would be:
1. Creating a cross-platform terminal emulator abstraction
2. Building Avalonia UI views equivalent to WPF XAML
3. Implementing platform-specific services for macOS/Linux
