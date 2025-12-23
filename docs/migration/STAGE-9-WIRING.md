# Stage 9: Wire Existing Avalonia Components

## Problem Statement

The Avalonia migration (Stages 1-8) has created all necessary Views, ViewModels, and services, but MainWindow only shows a placeholder UI with "New Terminal" and "Clear" buttons. The existing components need to be wired together to restore full functionality.

## Current State

| Component | Status |
|-----------|--------|
| MainWindow.axaml | Placeholder UI with command bindings |
| MainWindow.axaml.cs | ✅ Phase 1 complete - MainViewModel connected |
| MainViewModel | ✅ Connected via DI, DataContext set |
| TabStrip.axaml | Complete, expects MainViewModel as DataContext |
| TabContentTemplates.axaml | Has DataTemplates for all tab types |
| Popup Views (14 files) | All exist in Views/Popups/ - NOT hosted |
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

### Phase 2: MainWindow Layout Structure

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

### Phase 3: Tab Content Templates

**Complexity:** Low | **Dependencies:** Phase 2

**File:** `src/TerminalHost/TerminalHost/App.axaml`

Add TabContentTemplates as application resource:
```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <!-- existing resources -->
            <ResourceInclude Source="avares://host/Resources/TabContentTemplates.axaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
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

### Phase 4: Popup Hosting

**Complexity:** Medium-High | **Dependencies:** Phase 2

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

### Phase 5: Keyboard Shortcuts

**Complexity:** Medium | **Dependencies:** Phase 1, Phase 4

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

### Phase 6: Event Handlers & Window State

**Complexity:** Low | **Dependencies:** Phase 1

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
Phase 2 (Layout) ───► Phase 3 (Templates)  ← NEXT
    │
    ▼
Phase 4 (Popups)
    │
    ▼
Phase 5 (Shortcuts) - partially done in Phase 1
    │
    ▼
Phase 6 (Events) - window state done in Phase 1
    │
    ▼
Phase 7 (macOS Menu)
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

### Remaining Phases
- [ ] Tab strip visible with buttons (New Project, Settings, Statistics)
- [ ] Ctrl+N opens folder picker, creates terminal pair tab
- [ ] Tab content displays correctly for each tab type
- [ ] Command palette opens (Ctrl+Shift+P), filters commands
- [ ] Help popup shows (F1)
- [ ] Git branch switcher works (Ctrl+B)
- [ ] Git changes panel works (Ctrl+G)
- [ ] Scratch pad works (Ctrl+Shift+N)
- [ ] File explorer toggles (Ctrl+Shift+F)
- [ ] Tab switching works (Ctrl+PageDown/Up, Ctrl+1-9)
- [ ] Escape closes all popups
- [ ] macOS menu items functional
- [ ] Window position/size persists across restarts
