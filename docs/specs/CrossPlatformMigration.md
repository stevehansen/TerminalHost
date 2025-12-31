# Cross-Platform Migration Plan: Option A Implementation

## Overview

This document details the migration plan to create a unified codebase with two platform-specific applications sharing a common core. The goal is to merge the `macos_only` branch Avalonia work into the `master` branch architecture.

**Target Architecture:**
```
TerminalHost/
├── src/
│   ├── TerminalHost.Core/           # Shared portable code (existing)
│   ├── TerminalHost.Windows/        # Windows services (existing)
│   ├── TerminalHost.macOS/          # macOS services (DONE)
│   ├── TerminalHost/                # WPF App - Windows (existing)
│   └── TerminalHost.Avalonia/       # Avalonia App - macOS/Linux (pending)
└── tests/
```

---

## Progress Summary

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1: Core Enhancements | **COMPLETED** | Added 7 interfaces, 10 domain models |
| Phase 2: TerminalHost.macOS | **COMPLETED** | 3 services + pty_helper.py |
| Phase 3: TerminalHost.Avalonia | **COMPLETED** | 346 files, structure created |
| Phase 4: Reconcile Services | **COMPLETED** | Both Windows & Avalonia build with 0 errors |
| Phase 5: Windows Abstraction | Pending | |

---

## Current State Analysis

### Master Branch (Windows - WPF)

| Component | Count | Location |
|-----------|-------|----------|
| Core Interfaces | 31 | `TerminalHost.Core/Interfaces/` |
| Core Services | 21 | `TerminalHost.Core/Services/` |
| Core ViewModels | 9 | `TerminalHost.Core/ViewModels/` |
| Core Domain | 67 | `TerminalHost.Core/Domain/` |
| Windows Services | 8 | `TerminalHost.Windows/Services/` |
| WPF Views | ~60 | `TerminalHost/Views/` |
| WPF ViewModels | ~31 | `TerminalHost/ViewModels/` |

### macos_only Branch (macOS - Avalonia)

| Component | Count | Location |
|-----------|-------|----------|
| Interfaces | 37 | `Services/I*.cs` (flat) |
| Services | ~40 | `Services/` (flat) |
| ViewModels | 36 | `ViewModels/` |
| Domain | 64 | `Domain/` |
| Avalonia Views | 38 | `Views/` |
| Custom Controls | 6 | `Controls/` |
| VtNetCore | 49 | `VtNetCore/` (embedded) |

---

## Files to Port from macos_only

### 1. New Interfaces (Add to Core)

These interfaces exist in macos_only but not in master Core:

| Interface | Purpose | Port To |
|-----------|---------|---------|
| `IPtyService.cs` | PTY abstraction for terminal processes | Core (cross-platform) |
| `ITerminalControl.cs` | Terminal control abstraction | Core (cross-platform) |
| `IScreenService.cs` | Screen/display information | Core |
| `ISystemInfoService.cs` | System information (OS, paths) | Core |
| `IClipboardService.cs` | Clipboard operations | Core |
| `IFilePickerService.cs` | File picker dialogs | Core |
| `IFileExplorerService.cs` | File explorer operations | Core |
| `IFilePreviewService.cs` | File preview generation | Core |
| `ITaskService.cs` | Focus mode task management | Core |
| `ISessionManager.cs` | Terminal session lifecycle | Core |
| `ITerminalControlFactory.cs` | Creates terminal controls | Core |

### 2. New Domain Models (Add to Core)

| Model | Purpose |
|-------|---------|
| `ITerminalControl.cs` | Terminal control interface + `TerminalMouseEventArgs` |
| `TerminalTheme.cs` | Terminal color themes |
| `FocusTask.cs` | Focus mode task model |
| `FocusModeState.cs` | Focus mode state |
| `FocusTaskStatus.cs` | Task status enum |
| `QuickNote.cs` | Quick notes model |
| `GitCommitDetails.cs` | Extended commit info |
| `GitCommitFile.cs` | Files in a commit |
| `CreateWorktreeDialogResult.cs` | Dialog result type |
| `SearchMatch.cs` | Search match details |

### 3. VtNetCore Terminal Emulation (49 files)

**Location in macos_only:** `src/TerminalHost/TerminalHost/VtNetCore/`

```
VtNetCore/
├── Exceptions/
│   └── EscapeSequenceException.cs
├── Resources/
│   └── AdditionalAssemblyInformation.cs
├── VirtualTerminal/
│   ├── ConsoleTerminal.cs
│   ├── VirtualTerminalController.cs
│   ├── KeyboardTranslation.cs
│   ├── KeyboardTranslations.cs
│   ├── IVirtualTerminalController.cs
│   ├── SendDataEventArgs.cs
│   ├── SizeEventArgs.cs
│   ├── TextEventArgs.cs
│   ├── TextPosition.cs
│   ├── TextRange.cs
│   ├── TerminalCursorState.cs
│   ├── Encodings/
│   │   └── Iso2022Encoding.cs
│   ├── Enums/ (10 files)
│   ├── Layout/
│   │   ├── LayoutRow.cs
│   │   └── LayoutSpan.cs
│   └── Model/
│       ├── TerminalAttribute.cs
│       ├── TerminalCharacter.cs
│       ├── TerminalColor.cs
│       ├── TerminalLine.cs
│       └── TerminalLines.cs
└── XTermParser/
    ├── DataConsumer.cs
    ├── XTermInputBuffer.cs
    ├── XTermSequenceHandlers.cs
    ├── XTermSequenceReader.cs
    └── SequenceType/ (15+ files)
```

**Port to:** `TerminalHost.Avalonia/Terminal/VtNetCore/` or separate `TerminalHost.Terminal` project

### 4. macOS Platform Services (New TerminalHost.macOS project)

| Service | Purpose |
|---------|---------|
| `MacPtyService.cs` | PTY implementation using Python helper |
| `pty_helper.py` | Python script for PTY management |
| `MacTerminalControl.cs` | Avalonia terminal control using VtNetCore |
| `ScreenService.cs` | macOS screen information |
| `SystemInfoService.cs` | macOS system paths and info |
| `SingleInstanceService.cs` | Unix domain socket implementation |
| `FolderPickerService.cs` | Avalonia folder picker |
| `FilePickerService.cs` | Avalonia file picker |
| `ClipboardService.cs` | Avalonia clipboard |
| `DispatcherService.cs` | Avalonia dispatcher |
| `TimerService.cs` | Avalonia timer |
| `ToastService.cs` | Avalonia toast notifications |
| `DialogService.cs` | MessageBox.Avalonia dialogs |
| `SystemTrayService.cs` | macOS menu bar (if needed) |

### 5. Avalonia Views (38 files)

**Main Views:**
- `MainWindow.axaml(.cs)` - Main application window
- `TabStrip.axaml(.cs)` - Tab bar

**Tab Content:**
- `Views/Tabs/TerminalPairView.axaml(.cs)`
- `Views/Tabs/ProfileTerminalView.axaml(.cs)`
- `Views/DashboardView.axaml(.cs)`
- `Views/SettingsView.axaml(.cs)`
- `Views/ProfilesView.axaml(.cs)`
- `Views/StatisticsView.axaml(.cs)`
- `Views/TimelineModeView.axaml(.cs)`
- `Views/WorkspaceSidebar.axaml(.cs)`

**Popups (20 files):**
- `Views/Popups/CommandPaletteView.axaml(.cs)`
- `Views/Popups/TabSwitcherView.axaml(.cs)`
- `Views/Popups/GitBranchView.axaml(.cs)`
- `Views/Popups/GitFilesView.axaml(.cs)`
- `Views/Popups/GitStashView.axaml(.cs)`
- `Views/Popups/CommitHistoryView.axaml(.cs)`
- `Views/Popups/ReflogView.axaml(.cs)`
- `Views/Popups/FileHistoryView.axaml(.cs)`
- `Views/Popups/FileBlameView.axaml(.cs)`
- `Views/Popups/FileViewerPopup.axaml(.cs)`
- `Views/Popups/FilePreviewView.axaml(.cs)`
- `Views/Popups/DetectedLinksView.axaml(.cs)`
- `Views/Popups/HelpView.axaml(.cs)`
- `Views/Popups/TaskPanelView.axaml(.cs)`
- `Views/Popups/SearchAcrossFilesView.axaml(.cs)`
- `Views/Popups/ManageWorktreesView.axaml(.cs)`
- `Views/Popups/RepositorySwitcherView.axaml(.cs)`
- `Views/Popups/PrReviewView.axaml(.cs)`
- `Views/Popups/TestResultsView.axaml(.cs)`
- `Views/Popups/QuickNoteView.axaml(.cs)`
- `Views/Popups/QuickTaskView.axaml(.cs)`
- `Views/Popups/TabDropdownView.axaml(.cs)`

**Dialogs:**
- `Views/Dialogs/InputDialog.axaml(.cs)`
- `Views/Dialogs/NotificationDialog.axaml(.cs)`
- `Views/Dialogs/CreateWorktreeDialog.axaml(.cs)`

**Standalone Windows:**
- `Views/FileViewerView.axaml(.cs)`
- `Views/FileViewerWindow.axaml(.cs)`
- `Views/FileExplorerView.axaml(.cs)`
- `Views/ScratchPadView.axaml(.cs)`
- `Views/MarkdownPreviewWindow.axaml(.cs)`
- `Views/SetupWindow.axaml(.cs)`
- `Views/ToastContainerView.axaml(.cs)`
- `Views/ToastItemView.axaml(.cs)`
- `Views/ToastWindow.axaml(.cs)`

### 6. Avalonia Controls (6 files)

| Control | Purpose |
|---------|---------|
| `MacTerminalControl.cs` | Terminal emulator control |
| `DiffViewer.axaml(.cs)` | Unified diff viewer |
| `SideBySideDiffViewer.axaml(.cs)` | Side-by-side diff |
| `MarkdownViewer.axaml(.cs)` | Markdown renderer |
| `PrCommentThread.axaml(.cs)` | PR comment display |
| `DraggablePopup.axaml(.cs)` | Draggable popup base |

### 7. ViewModels to Reconcile

ViewModels in macos_only need comparison with master. Most should work with both UI frameworks since they use CommunityToolkit.Mvvm.

**Already in Core (keep):**
- `SettingsTabViewModel`
- `ProfilesTabViewModel`
- `StatisticsTabViewModel`
- `TimelineTabViewModel`
- `ToastViewModel`
- `ProjectStatViewModel`
- `IntentRowViewModel`
- `SessionBlockViewModel`

**Need to move to Core (from macos_only or master app):**
- `MainViewModel` (complex - may need platform split)
- `GitBranchViewModel`
- `GitFilesViewModel`
- `GitStashViewModel`
- `CommitHistoryViewModel`
- `ReflogViewModel`
- `FileHistoryViewModel`
- `FileBlameViewModel`
- `FileExplorerViewModel`
- `FileViewerViewModel`
- `FilePreviewViewModel`
- `DetectedLinksViewModel`
- `SearchAcrossFilesViewModel`
- `ScratchPadViewModel`
- `TaskPanelViewModel`
- `ManageWorktreesViewModel`
- `WorkspaceSidebarViewModel`
- `PrReviewViewModel`
- `TestResultsViewModel`
- `MarkdownPreviewViewModel`
- `DashboardTabViewModel`
- `RepositorySwitcherViewModel`
- `TerminalPairTabViewModel`
- `TerminalTabViewModel`
- `ProfileTerminalTabViewModel`
- `SetupViewModel`
- `FileChangeViewModel`

---

## Migration Phases

### Phase 1: Core Enhancements - COMPLETED

**Goal:** Add missing interfaces and models to `TerminalHost.Core`

**Completed - Interfaces added:**
- `IPtyService` - PTY abstraction for terminal processes
- `ITerminalControl` - Terminal control abstraction
- `IClipboardService` - Clipboard operations
- `IFilePickerService` - File picker dialogs
- `IScreenService` - Screen/display information
- `ISystemInfoService` - System paths, fonts, shell
- `ITaskService` - Focus mode task management

**Skipped (kept in WPF app due to WPF-specific types):**
- `IFileExplorerService` - Uses WPF `Brush` type
- `IFilePreviewService` - Uses WPF `FlowDocument` type
- `ISessionManager` - Uses WPF control references
- `ITerminalControlFactory` - Platform-specific

**Completed - Domain models added:**
- `TerminalMouseEventArgs` - Terminal mouse events
- `FilePickerFilter` - File dialog filters
- `FileSystemNodeData` - File explorer node data
- `FilePreviewResult` - File preview result
- `TerminalSessionInfo` - Terminal session data
- `ScreenBounds` - Screen geometry
- `FocusTask` - Focus mode task model (full)
- `FocusTaskStatus` - Task status enum
- `FocusModeState` - Focus mode state
- `QuickNote` - Quick notes model

### Phase 2: Create TerminalHost.macOS Project - COMPLETED

**Goal:** Platform-specific macOS services

**Project created:** `src/TerminalHost.macOS/`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifiers>osx-arm64;osx-x64</RuntimeIdentifiers>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>TerminalHost.macOS</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TerminalHost.Core\TerminalHost.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Resources\pty_helper.py">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

**Completed - Services implemented:**
- `MacPtyService` : `IPtyService` - PTY using Python helper with resize support
- `MacSingleInstanceService` : `ISingleInstanceService` - Named pipes + file lock
- `MacSystemInfoService` : `ISystemInfoService` - macOS paths, fonts, shell

**Completed - Resources:**
- `Resources/pty_helper.py` - Python PTY helper script with resize protocol

**Remaining for Phase 2 (can be done in Phase 3):**
- `MacLinkDetectionService` - Link detection (may not be needed if Core version works)

### Phase 3: Create TerminalHost.Avalonia Project - COMPLETED

**Goal:** Avalonia UI application for macOS/Linux

**Completed - Project structure created:**
- `TerminalHost.Avalonia.csproj` with Avalonia 11.2.1, references Core and macOS
- 346 files copied from macos_only branch via automated script
- Added to solution

**Completed - Files copied:**
- VtNetCore terminal emulation (49 files)
- Controls (MacTerminalControl, DiffViewer, etc. - 11 files)
- Views/Popups/Dialogs (87 .axaml files + code-behind)
- ViewModels (36 files)
- Services (86 files, excluding Mac-specific)
- Domain models (64 files)
- Resources and Assets

**Completed - Build fixes:**
- Added missing using statements (Avalonia, Avalonia.Layout, etc.)
- Created Program.cs entry point
- Excluded WPF-dependent SyntaxHighlighting services

**Excluded (duplicate interfaces now in Core):**
- `Services/IPtyService.cs` - Use `TerminalHost.Core.Interfaces.IPtyService`
- `Domain/ITerminalControl.cs` - Use `TerminalHost.Core.Interfaces.ITerminalControl`

**Script created:** `scripts/copy-avalonia-files.ps1` for automated file extraction from macos_only branch

**Project configuration:**
- Global usings for `TerminalHost.Core.Interfaces` and `TerminalHost.Core.Domain` added via csproj
- ~35 service interfaces excluded (use Core versions instead)
- ~55 Domain types excluded (use Core versions instead)
- Kept Avalonia-specific: TerminalSession, TerminalPair, FileSystemNode, TerminalTheme, SearchMatch, etc.

### Phase 4: Reconcile Services and ViewModels - COMPLETED

**Goal:** Align Avalonia service implementations with Core interfaces

**Final Status:** Both Windows (WPF) and macOS (Avalonia) projects build with **0 errors, 0 warnings**

**Approach Used:** Option 2 - Updated Avalonia services to use Core Domain property names

**Changes Made:**

1. **Type Resolution & Global Usings**
   - Added global usings for `TerminalHost.Core.Domain` and `TerminalHost.Core.Interfaces` in csproj
   - Excluded 55+ duplicate Domain/Interface files from Avalonia project compilation
   - Added type aliases for conflicting types (ITimerService, SearchMatch)

2. **Interface Implementations (15+ services updated)**
   - DispatcherService: Added BeginInvoke, Invoke, InvokeAsync methods
   - DialogService: Added ShowCustomButtons, ShowCreateWorktreeDialog, ShowCreateIntentDialog
   - FolderPickerService: Added sync PickFolder and PickFolders wrappers
   - FileEditService: Removed duplicate type definitions, uses Core types
   - SingleInstanceService: Added HookEventReceived event, IsMainInstanceRunning
   - SearchService: Added explicit interface implementations with type conversion
   - StatisticsService: Added RecordFocusTime, GetFocusTimeForPeriod, GetCharCountForPeriod
   - GitWorktreeService: Added 8 explicit interface implementations
   - GitStatusService: Major update - all methods now return GitOperationResult
   - TimelineService: Added 20+ interface methods, fixed return types

3. **Domain Type Alignment**
   - Updated Avalonia services to use Core property names:
     - `AuthorName` instead of `Author`
     - `CommitDate` instead of `Date`
     - `Insertions` instead of `Additions`
     - `LineContent` instead of `Content`
     - `IsFirstInGroup` instead of `IsGroupStart`
     - `Branch` instead of `BranchName`
   - Updated ViewModels to handle `GitOperationResult` return types instead of tuples/bools

4. **Core Domain Extensions**
   - Added properties to existing Core types: FirstRunCompleted, FirstRunDate, CustomPaths, StartupCommand, etc.
   - Extended ITabViewModel with: ShowActivitySpinner, ShowCompletedIndicator, IsTerminalInitialized, InitializeTerminalsAsync

5. **XAML Namespace Updates (21 files)**
   - Updated XAML files to use `clr-namespace:TerminalHost.Core.Domain;assembly=TerminalHost.Core`
   - Updated ITabViewModel references to use `TerminalHost.Core.Interfaces`
   - Created stub style files (Controls.axaml, Buttons.axaml, ScrollBars.axaml)

6. **Windows Build Fixes**
   - Added new ITabViewModel members to Windows ViewModels:
     - DashboardTabViewModel
     - ProfileTerminalTabViewModel
     - TerminalPairTabViewModel

### Phase 5: Windows Terminal Abstraction

**Goal:** Make Windows also use the `ITerminalControl` abstraction

Create `WindowsTerminalControl` wrapper around `EasyWindowsTerminalControl`:
```csharp
public class WindowsTerminalControl : ITerminalControl
{
    private readonly EasyTerminalControl _control;
    // Implement interface wrapping the existing control
}
```

---

## File Mapping Summary

| Source (macos_only) | Target | Action |
|---------------------|--------|--------|
| `Domain/*.cs` | `Core/Domain/` | Merge missing |
| `Services/I*.cs` | `Core/Interfaces/` | Merge missing |
| `Services/*.cs` (portable) | `Core/Services/` | Merge missing |
| `Services/*.cs` (macOS) | `macOS/Services/` | New |
| `ViewModels/*.cs` (portable) | `Core/ViewModels/` | Merge |
| `ViewModels/*.cs` (UI-specific) | `Avalonia/ViewModels/` | New |
| `Views/*.axaml` | `Avalonia/Views/` | New |
| `Controls/*.axaml` | `Avalonia/Controls/` | New |
| `VtNetCore/**` | `Avalonia/Terminal/` | New |
| `Resources/pty_helper.py` | `macOS/Resources/` | New |

---

## Estimated vs Actual Effort

| Phase | Est. Files | Actual Files | Complexity | Status |
|-------|------------|--------------|------------|--------|
| Phase 1 | ~20 | 17 | Low | ✅ Complete |
| Phase 2 | ~8 | 5 | Medium | ✅ Complete |
| Phase 3 | ~100 | 346 | High | ✅ Complete |
| Phase 4 | ~30 | ~80 | High | ✅ Complete |
| Phase 5 | ~3 | - | Low | Pending |

**Actual Total:** ~450 files created/modified

---

## Commands to Start

```bash
# Create new branch from master
git checkout master
git checkout -b feature/cross-platform

# Create new projects
dotnet new classlib -n TerminalHost.macOS -o src/TerminalHost.macOS
dotnet new avalonia.app -n TerminalHost.Avalonia -o src/TerminalHost.Avalonia

# Add to solution
dotnet sln add src/TerminalHost.macOS
dotnet sln add src/TerminalHost.Avalonia

# Add references
dotnet add src/TerminalHost.macOS reference src/TerminalHost.Core
dotnet add src/TerminalHost.Avalonia reference src/TerminalHost.Core
dotnet add src/TerminalHost.Avalonia reference src/TerminalHost.macOS
```

---

## Open Questions

1. ~~**VtNetCore licensing:** Need to verify MIT/Apache compatible~~ ✅ Included in Avalonia project
2. **Windows terminal:** Keep EasyWindowsTerminalControl or port VtNetCore? (Phase 5)
3. **Linux:** Test Avalonia + VtNetCore on Linux
4. ~~**Single solution or separate?**~~ ✅ Single solution with all projects
5. **CI/CD:** How to build both platforms?

## Next Steps (Phase 5)

1. Create `WindowsTerminalControl` wrapper around `EasyWindowsTerminalControl` implementing `ITerminalControl`
2. Test Avalonia build on actual macOS hardware
3. Set up CI/CD for cross-platform builds
4. Runtime testing of Avalonia app functionality

---

## References

- [CrossPlatform.md](CrossPlatform.md) - Original separation spec
- [macos_only branch](../../) - Source Avalonia implementation
- [Avalonia Documentation](https://docs.avaloniaui.net/)
- [VtNetCore](https://github.com/9a4gl/VtNetCore) - Terminal emulation library
