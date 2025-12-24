# Stage 9: Wire Existing Avalonia Components

## Problem Statement

The Avalonia migration (Stages 1-8) has created all necessary Views, ViewModels, and services, but MainWindow only shows a placeholder UI with "New Terminal" and "Clear" buttons. The existing components need to be wired together to restore full functionality.

## Current State

| Component | Status |
|-----------|--------|
| MainWindow.axaml | ✅ Phase 2 complete - TabStrip, ContentControl, PopupHost |
| MainWindow.axaml.cs | ✅ Phase 1 complete - MainViewModel connected |
| MainViewModel | ✅ Connected via DI, DataContext set |
| TabStrip.axaml | ✅ Wired to MainViewModel |
| App.axaml | ✅ Phase 3 complete - Implicit DataTemplates added |
| TabContentTemplates.axaml | Has explicit keyed DataTemplates (fallback) |
| Popup Views (6 hosted) | ✅ CommandPalette, Help, TabSwitcher, TabDropdown, QuickTask, QuickNote |
| Popup Views (remaining) | ✅ GitBranch, GitFiles, ScratchPad, FileViewer, DetectedLinks, TaskPanel |
| All ViewModels (23 files) | Registered in DI, ready to use |

## Expected Outcome

After this stage:
- Tab strip shows with project tabs, Settings, Statistics buttons
- Opening a folder creates a terminal pair tab
- Tab content switches when selecting different tabs
- All keyboard shortcuts work (Ctrl+N, F1, Ctrl+Shift+P, etc.)
- All popups accessible (Command palette, Help, Git panels, etc.)
- Window state persists across sessions

---

## Implementation Phases

### Phase 1: MainViewModel Integration (Foundation) ✅ COMPLETED

**Complexity:** Low | **Dependencies:** None

**File:** `src/TerminalHost/TerminalHost/MainWindow.axaml.cs`

| Change | Description | Status |
|--------|-------------|--------|
| Add constructor parameter | `MainViewModel mainViewModel` from DI | ✅ Done |
| Set DataContext | `DataContext = _mainViewModel` after InitializeComponent | ✅ Done |
| Initialize lifecycle | Call `_mainViewModel.Initialize()` in `OnOpened` | ✅ Done |
| Shutdown lifecycle | Call `_mainViewModel.Shutdown()` in `OnClosing` | ✅ Done |
| Remove placeholders | Delete `_currentTerminal`, `_currentSession` fields | ✅ Done |
| Remove handlers | Delete `NewTerminalButton_Click`, `ClearButton_Click` | ✅ Done |
| Window state persistence | Save position/size in `OnClosing` | ✅ Done |
| Basic keyboard shortcuts | F1, Escape, Ctrl+N, Ctrl+W, Ctrl+,, Ctrl+Shift+P, etc. | ✅ Done |

**Additional fixes during Phase 1:**
- Added missing `IGitPrService` registration in `App.axaml.cs`
- Updated `MainWindow.axaml` buttons to use Command bindings instead of Click handlers
- Added `CloseAllPopups()` helper method

---

### Phase 2: MainWindow Layout Structure ✅ COMPLETED

**Complexity:** Medium | **Dependencies:** Phase 1

**File:** `src/TerminalHost/TerminalHost/MainWindow.axaml`

Replace placeholder content with proper structure:

```
┌─────────────────────────────────────────┐
│ Row 0: TabStrip                         │
├─────────────────────────────────────────┤
│ Row 1: ContentControl (SelectedTab)     │
│                                         │
│   [Tab content rendered via templates]  │
│                                         │
├─────────────────────────────────────────┤
│ Overlay: Popup Host Panel               │
│   - CommandPaletteView                  │
│   - HelpView                            │
│   - TabSwitcherView                     │
│   - TabDropdownView                     │
│   - GitBranchView                       │
│   - GitFilesView                        │
│   - ScratchPadView                      │
│   - FileViewerPopup                     │
└─────────────────────────────────────────┘
```

**Key AXAML elements:**
```xml
<!-- Tab Strip -->
<views:TabStrip Grid.Row="0" DataContext="{Binding}" />

<!-- Tab Content -->
<ContentControl Grid.Row="1" Content="{Binding SelectedTab}" />

<!-- Popup Overlay -->
<Panel x:Name="PopupHost">
    <popups:CommandPaletteView IsVisible="{Binding IsCommandPaletteOpen}" />
    <popups:HelpView IsVisible="{Binding IsHelpOpen}" />
    <!-- etc. -->
</Panel>
```

---

### Phase 3: Tab Content Templates ✅ COMPLETED

**Complexity:** Low | **Dependencies:** Phase 2

**File:** `src/TerminalHost/TerminalHost/App.axaml`

Add implicit DataTemplates to `Application.DataTemplates` (not ResourceDictionary - Avalonia requires DataTemplates in this collection for implicit matching):
```xml
<Application.DataTemplates>
    <DataTemplate DataType="{x:Type vm:TerminalPairTabViewModel}">
        <tabs:TerminalPairView />
    </DataTemplate>
    <DataTemplate DataType="{x:Type vm:SettingsTabViewModel}">
        <views:SettingsView/>
    </DataTemplate>
    <!-- etc. -->
</Application.DataTemplates>
```

This enables implicit DataTemplate selection:

| ViewModel Type | View |
|----------------|------|
| TerminalPairTabViewModel | TerminalPairView |
| SettingsTabViewModel | SettingsView |
| ProfilesTabViewModel | ProfilesView |
| StatisticsTabViewModel | StatisticsView |
| DashboardTabViewModel | DashboardView |
| ProfileTerminalTabViewModel | ProfileTerminalView |

---

### Phase 4: Popup Hosting ✅ COMPLETED

**Complexity:** Medium-High | **Dependencies:** Phase 2

**Implementation Notes:**
- Added popup views to MainWindow.axaml: GitBranchView, GitFilesView, ScratchPadView, FileViewerPopup, DetectedLinksView, TaskPanelView
- Injected popup ViewModels via constructor: GitBranchViewModel, GitFilesViewModel, ScratchPadViewModel, FileViewerViewModel, DetectedLinksViewModel, TaskPanelViewModel
- Wired MainViewModel events: GitChangesRequested, FilePreviewRequested, FilePopOutRequested, SetupRequested
- Note: ScratchPadViewModel and TaskPanelViewModel subscribe to their events internally
- Added keyboard shortcuts: Ctrl+B (Git Branch), Ctrl+G (Git Changes), Ctrl+Shift+N (Scratch Pad), Ctrl+T (Task Panel), Ctrl+O (File Preview), Ctrl+Shift+E (File Edit), Ctrl+Shift+F (File Explorer toggle)
- Updated CloseAllPopups to close all popup ViewModels on Escape

#### 4.1 MainWindow.axaml - Add popup views

| Popup | Visibility Binding | Position |
|-------|-------------------|----------|
| CommandPaletteView | IsCommandPaletteOpen | Top-center |
| HelpView | IsHelpOpen | Center |
| TabSwitcherView | IsTabSwitcherOpen | Center |
| TabDropdownView | IsTabDropdownOpen | Top-left |
| QuickTaskView | IsQuickTaskOpen | Center |
| QuickNoteView | IsQuickNoteOpen | Center |

#### 4.2 MainWindow.axaml.cs - Wire events

```csharp
// Subscribe to MainViewModel events
_mainViewModel.ScratchPadRequested += OnScratchPadRequested;
_mainViewModel.GitChangesRequested += OnGitChangesRequested;
_mainViewModel.FilePreviewRequested += OnFilePreviewRequested;
_mainViewModel.FilePopOutRequested += OnFilePopOutRequested;
_mainViewModel.SetupRequested += OnSetupRequested;
_mainViewModel.TaskPanelRequested += OnTaskPanelRequested;
_mainViewModel.PrReviewRequested += OnPrReviewRequested;
_mainViewModel.MarkdownPreviewRequested += OnMarkdownPreviewRequested;
```

Get popup ViewModels from DI:
- `ScratchPadViewModel`
- `GitFilesViewModel`
- `GitBranchViewModel`
- `FileViewerViewModel`
- `DetectedLinksViewModel`

---

### Phase 5: Keyboard Shortcuts ✅ COMPLETED

**Complexity:** Medium | **Dependencies:** Phase 1, Phase 4

**Status:** Implemented in Phase 1 and Phase 4

#### 5.1 MainWindow.axaml - KeyBindings

| Shortcut | Action | Command/Property |
|----------|--------|------------------|
| Ctrl+N | New project folder | OpenNewProjectCommand |
| Ctrl+W | Close current tab | CloseTabCommand |
| Ctrl+, | Open settings | OpenSettingsCommand |
| Ctrl+Shift+P | Command palette | Toggle IsCommandPaletteOpen |
| Ctrl+Shift+T | Tab switcher | Toggle IsTabSwitcherOpen |
| F1 | Show help | Toggle IsHelpOpen |
| Ctrl+B | Git branch switcher | Toggle GitBranchViewModel.IsOpen |
| Ctrl+G | Git changes panel | Toggle GitFilesViewModel.IsOpen |
| Ctrl+Shift+N | Scratch pad | Invoke ScratchPadRequested |
| Ctrl+Shift+F | File explorer toggle | ToggleExplorerCommand |
| Ctrl+O | File viewer (preview) | Invoke FilePreviewRequested |
| Ctrl+Shift+E | File viewer (edit) | Invoke FilePreviewRequested |
| Ctrl+PageDown | Next tab | CycleTabCommand(true) |
| Ctrl+PageUp | Previous tab | CycleTabCommand(false) |
| Ctrl+1-9 | Jump to tab N | Custom handler |
| Ctrl+` | Toggle terminal | ToggleTerminalCommand |
| F5 | Start/Stop run | RunCommand |
| Escape | Close active popup | Custom handler |

#### 5.2 MainWindow.axaml.cs - OnKeyDown handler

Handle platform-specific modifiers and complex shortcuts:
```csharp
protected override void OnKeyDown(KeyEventArgs e)
{
    var isCtrlOrCmd = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
        : e.KeyModifiers.HasFlag(KeyModifiers.Control);

    // Escape closes popups
    if (e.Key == Key.Escape)
    {
        CloseAllPopups();
        e.Handled = true;
        return;
    }

    // Ctrl+1-9 tab jumping
    if (isCtrlOrCmd && e.Key >= Key.D1 && e.Key <= Key.D9)
    {
        var index = e.Key - Key.D1;
        _mainViewModel.JumpToTab(index);
        e.Handled = true;
        return;
    }

    base.OnKeyDown(e);
}
```

---

### Phase 6: Event Handlers & Window State ✅ COMPLETED

**Complexity:** Low | **Dependencies:** Phase 1

**Status:** Window state persistence done in Phase 1, popup event handlers done in Phase 4

#### 6.1 Event handlers in MainWindow.axaml.cs

```csharp
private void OnFilePreviewRequested(object? sender, FilePreviewEventArgs e)
{
    // Show FileViewerPopup with file path
    _fileViewerViewModel.OpenFile(e.FilePath, e.EditMode);
    // Toggle popup visibility
}

private void OnFilePopOutRequested(object? sender, FilePreviewEventArgs e)
{
    // Create new FileViewerWindow
    var window = new FileViewerWindow();
    window.DataContext = new FileViewerViewModel(...);
    window.Show();
}

private void OnSetupRequested(object? sender, EventArgs e)
{
    var setupWindow = new SetupWindow();
    setupWindow.ShowDialog(this);
}

private void OnRunTerminalRequested(object? sender, RunTerminalEventArgs e)
{
    // Create run terminal via TerminalControlFactory
    var terminal = await _terminalControlFactory.CreateTerminalControlAsync(...);
    e.Tab.SetRunTerminal(terminal);
}
```

#### 6.2 Window state persistence

```csharp
private void OnClosing(object? sender, WindowClosingEventArgs e)
{
    // Save window position/size
    var config = _configService.GetConfiguration();
    config.WindowState = new WindowState
    {
        Left = Position.X,
        Top = Position.Y,
        Width = Width,
        Height = Height,
        IsMaximized = WindowState == WindowState.Maximized
    };
    _configService.SaveConfiguration(config);

    _mainViewModel.Shutdown();
}
```

---

### Phase 7: macOS Menu Updates

**Complexity:** Low | **Dependencies:** Phase 1, Phase 5

**File:** `src/TerminalHost/TerminalHost/MainWindow.axaml.cs`

Update `SetupMacOSMenu()` method:

| Menu | Item | Action |
|------|------|--------|
| File | New Project | `_mainViewModel.OpenNewProjectCommand.Execute(null)` |
| File | Close Tab | `_mainViewModel.CloseTabCommand.Execute(_mainViewModel.SelectedTab)` |
| View | Settings | `_mainViewModel.OpenSettingsCommand.Execute(null)` |
| View | Command Palette | `_mainViewModel.IsCommandPaletteOpen = true` |
| View | Statistics | `_mainViewModel.OpenStatisticsCommand.Execute(null)` |
| Help | Keyboard Shortcuts | `_mainViewModel.IsHelpOpen = true` |

---

## Files to Modify

| File | Scope of Changes |
|------|------------------|
| `MainWindow.axaml` | Complete rewrite (~150 lines) |
| `MainWindow.axaml.cs` | Major changes (~200 lines) |
| `App.axaml` | Add 1 ResourceInclude |

## Files to Reference (no changes needed)

| File | Reference Purpose |
|------|-------------------|
| `ViewModels/MainViewModel.cs` | Commands, properties, events API |
| `Views/TabStrip.axaml` | Binding patterns |
| `Resources/TabContentTemplates.axaml` | DataTemplate definitions |
| `Views/Popups/*.axaml` | Popup view structure |

---

## Implementation Order

```
Phase 1 (Foundation) ✅ DONE
    │
    ▼
Phase 2 (Layout) ✅ ───► Phase 3 (Templates) ✅
    │
    ▼
Phase 4 (Popups) ✅ DONE
    │
    ▼
Phase 5 (Shortcuts) ✅ - mostly done in Phase 1 & 4
    │
    ▼
Phase 6 (Events) ✅ - window state done in Phase 1, popup events in Phase 4
    │
    ▼
Phase 7 (macOS Menu) ← NEXT
```

---

## Risk Mitigations

| Risk | Mitigation |
|------|------------|
| Implicit DataTemplates not matching | Use explicit ContentTemplateSelector if needed |
| Popup focus issues | Call Focus() in AttachedToVisualTree handlers |
| Platform keyboard differences | Check RuntimeInformation.IsOSPlatform |
| DraggablePopup positioning | Control already handles centering/bounds |

---

## Verification Checklist

### Phase 1 (Foundation)
- [x] Application launches without errors
- [x] MainViewModel connected as DataContext
- [x] Window state persistence implemented (saves on close)
- [x] Basic keyboard shortcuts wired (F1, Escape, Ctrl+N, Ctrl+W, etc.)

### Phase 2 & 3 (Layout & Templates)
- [x] Tab strip visible with buttons (New Project, Settings, Statistics)
- [x] Tab content displays correctly for each tab type (via DataTemplates)
- [x] Popup overlay panel in place with visibility bindings

### Phase 4 (Popup Hosting)
- [x] Git branch switcher added (Ctrl+B)
- [x] Git changes panel added (Ctrl+G)
- [x] Scratch pad added (Ctrl+Shift+N)
- [x] File viewer popup added (Ctrl+O for preview, Ctrl+Shift+E for edit)
- [x] Detected links popup added
- [x] Task panel added (Ctrl+T)
- [x] File explorer toggle works (Ctrl+Shift+F)
- [x] Escape closes all popups

### Runtime Fixes (Phase 4)
- [x] Added `TaskPanelViewModel` to DI registration in App.axaml.cs
- [x] Fixed cursor type `SizeNorthwestSoutheast` → `BottomRightCorner` in DraggablePopup.axaml
- [x] Replaced missing `EditModeShortcutsConverter` with static text in FileViewerPopup.axaml
- [x] Replaced missing `AccentSecondaryBrush` with `AccentDarkBrush` in DetectedLinksView.axaml
- [x] Changed `FileViewerViewModel` from `AddTransient` to `AddSingleton` for MainWindow injection

### Runtime Fixes (Tab Display) ✅
- [x] Fixed TabStrip DataContext inheritance in MainWindow.axaml:
  - Removed explicit `DataContext="{Binding}"` from TabStrip - in Avalonia, child controls automatically inherit DataContext
  - The explicit `{Binding}` syntax was breaking the DataContext inheritance chain
- [x] Simplified ListBox in TabStrip.axaml:
  - Removed custom ListBox template that was causing items to not render
  - Removed custom ListBoxItem template - using default Avalonia template with minimal style overrides
  - Kept only ItemsPanel (horizontal StackPanel) and basic ListBoxItem styling (padding, margin, background)

### Runtime Fixes (Terminal Display) ✅
- [x] Fixed terminal content type: `ContentControl?` → `Control?` in ViewModels
  - `TerminalPairTabViewModel`: CustomTerminalContent, ShellTerminalContent, RunTerminalContent, CurrentTerminalContent
  - `ProfileTerminalTabViewModel`: TerminalContent
  - `TerminalTabViewModel`: TerminalContent
  - Cast changed from `as ContentControl` to `as Control` in SetTerminalControls methods
- [x] Fixed terminal rendering in `MacTerminalControl.cs`:
  - `OnAttachedToVisualTree`: Added deferred InvalidateVisual and Focus using DispatcherPriority.Loaded
  - `ArrangeOverride`: Now passes `finalSize` to UpdateTerminalSize (was using stale `Bounds`)
  - `UpdateTerminalSize`: Takes Size parameter, validates non-zero, calls InvalidateVisual after resize
  - `InitializeAsync`: Added deferred InvalidateVisual for controls not yet in visual tree
- [x] Fixed Claude CLI launch error (`unknown option '-i'`) in `MacPtyService.cs`:
  - Removed hardcoded `-i -l` shell flags from all commands
  - New `GetCommandAndArgs()` method properly parses command and arguments
  - Shell flags only added for default shell (when no command specified)
- [x] Fixed TabStrip buttons disabled issue:
  - Added missing `TerminalSwitchButton` and `TabCloseButton` styles to `Buttons.axaml`
  - Changed buttons to use Click handlers instead of Command bindings (workaround for compiled binding issue with source-generated commands)
  - Added `x:CompileBindings="False"` to TabStrip.axaml

### Lazy Terminal Initialization ✅ COMPLETED
- [x] Added `IsTerminalInitialized` and `InitializeTerminalsAsync()` to `ITabViewModel`
- [x] `TerminalPairTabViewModel` and `ProfileTerminalTabViewModel` now create terminals lazily
- [x] `OpenProjectTab` accepts `selectTab` parameter (default true)
- [x] `RestoreOpenFolders` passes `selectTab: false` - only last selected tab initializes on startup
- [x] Terminals are created when user first clicks on a tab
- [x] Resources saved: only 1 terminal pair starts on launch instead of all saved tabs

### Tab Activity Indicators ✅ COMPLETED
- [x] Added `ShowActivitySpinner` and `ShowCompletedIndicator` to `ITabViewModel`
- [x] Yellow pulsing dot (●): terminal is actively producing output
- [x] Green solid dot (●): activity finished but tab not yet viewed
- [x] Indicators hidden when tab is selected
- [x] Pulsing animation (opacity 1.0 → 0.3 → 1.0) instead of rotation

### UI Fixes ✅ COMPLETED
- [x] Fixed DraggablePopup for macOS - replaced Popup control with inline Border
- [x] Fixed SettingsView pseudoclass selectors (:checked, :pointerover)
- [x] Removed IsHitTestVisible="False" from PopupHost panel
- [x] TerminalPairView shows folder name instead of full path in toolbar

### Phase 7 (macOS Menu) - REMAINING
- [ ] Update SetupMacOSMenu() with proper menu actions
- [ ] Wire File menu items (New Project, Close Tab)
- [ ] Wire View menu items (Settings, Command Palette, Statistics)
- [ ] Wire Help menu items (Keyboard Shortcuts)

### Functional Testing - IN PROGRESS
- [x] Settings button works (opens Settings tab)
- [x] Statistics button works (opens Statistics tab)
- [x] New Project button works (opens folder picker)
- [x] Shell terminal displays and works
- [x] Claude pane displays (command configurable in Settings)
- [x] Tab strip displays project tabs correctly
- [x] Tab selection changes content view
- [x] Lazy initialization works (terminals only start when tab clicked)
- [x] Activity indicators work (yellow pulse for active, green for completed)
- [ ] Ctrl+N opens folder picker, creates terminal pair tab
- [ ] Command palette opens (Ctrl+Shift+P), filters commands
- [ ] Help popup shows (F1)
- [ ] Tab switching works (Ctrl+PageDown/Up, Ctrl+1-9)
- [ ] Window position/size persists across restarts
