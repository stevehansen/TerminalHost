# PRD: Unified Panel System and UX Improvements

This document covers the next phase of UI improvements including the unified panel/popup/window system, file explorer enhancements, and UX refinements.

## 1. Unified Panel System

### Current State

The application has several content types that can appear in different contexts:
- **File Viewer**: Popup (Ctrl+O), Pop-out Window (detach button), Panel (file explorer integration)
- **Git Changes**: Popup only (Ctrl+G)
- **Git Branch**: Popup only (Ctrl+B)
- **Scratch Pad**: Popup only (Ctrl+Shift+N)
- **Task Panel**: Popup only (Ctrl+T)
- **Markdown Preview**: Window only (Ctrl+M)

### Goal

Create a unified system where content can transition between three states:

| State | Description | Behavior |
|-------|-------------|----------|
| **Panel** | Integrated in main window | Docked left/right, persists across tab switches |
| **Popup** | Floating within app | Modal-ish, centered, single instance |
| **Window** | Detached window | Independent, can be moved to other monitors, multiple instances |

### Transitions

```
     ┌────────┐
     │ Panel  │◄──────────────────┐
     └───┬────┘                   │
         │ Undock                 │ Dock
         ▼                        │
     ┌────────┐                   │
     │ Popup  │───────────────────┤
     └───┬────┘                   │
         │ Pop-out                │
         ▼                        │
     ┌────────┐                   │
     │ Window │───────────────────┘
     └────────┘     Dock back
```

### UI Controls

Each panel-capable view should have consistent controls:

**In Panel mode:**
- `⊞` Undock to popup
- `⧉` Pop-out to window
- `×` Close panel

**In Popup mode:**
- `◧` Dock to panel (left/right choice)
- `⧉` Pop-out to window
- `×` Close popup

**In Window mode:**
- `◧` Dock back to panel
- `×` Close window

### Implementation Approach

1. **Base ViewModels**: Create `IPanelableViewModel` interface with state management
2. **Panel Host**: New `PanelHost` control in MainWindow for docked panels
3. **State Persistence**: Remember preferred state per content type in config
4. **Multiple Windows**: Allow multiple detached windows for same content type

### Configuration

```json
{
  "panelStates": {
    "fileViewer": { "preferredState": "Window", "panelSide": "Right" },
    "gitChanges": { "preferredState": "Popup", "panelSide": "Right" },
    "scratchPad": { "preferredState": "Panel", "panelSide": "Right" }
  }
}
```

### Default Display States

Each panel has a default display state that determines how it opens when not already visible:

| Panel | Default State | Rationale |
|-------|---------------|-----------|
| File Explorer | Panel | Tree navigation is best docked for continuous use |
| Markdown Preview | Panel | Reading docs benefits from persistent side-by-side view |
| Git Changes | Popup | Quick diff review, typically opened/closed frequently |
| Scratch Pad | Panel | Notes should persist and be easily accessible |

### Priority for Panel Support

| Content | Panel | Popup | Window | Priority |
|---------|-------|-------|--------|----------|
| File Viewer | ✓ (existing) | ✓ (existing) | ✓ (existing) | Done |
| Markdown Preview | ✓ | ✓ | ✓ | Done |
| Git Changes | ✓ | ✓ | ✓ | Done |
| Scratch Pad | ✓ | ✓ | ✓ | Done |
| Task Panel | New | ✓ (existing) | New | Low |
| Git Branch | - | ✓ (existing) | - | N/A (small) |

---

## 2. File Explorer: .gitignore Support

### Current Problem

The file explorer shows all files regardless of `.gitignore` rules. This causes:
- Cluttered tree with `node_modules`, `bin`, `obj`, etc.
- Git status badges on files that are actually ignored
- Confusion about which files are tracked

### Goal

Respect `.gitignore` patterns to hide ignored files by default, with toggle to show them.

### Implementation Options

#### Option A: Git-based (Recommended)

Use `git check-ignore` to determine if files are ignored:

```csharp
// Check single file
git check-ignore -q "path/to/file"  // Exit 0 = ignored, 1 = not ignored

// Batch check (more efficient)
git check-ignore --stdin < files.txt

// List all ignored files in directory
git status --ignored --porcelain
```

**Pros:**
- 100% accurate (uses git's own logic)
- Handles nested `.gitignore`, global gitignore, `.git/info/exclude`
- No pattern parsing needed

**Cons:**
- Requires git process calls
- Slower for large directories

#### Option B: Parse .gitignore directly

Use a library like `Ignore` (npm) ported to C#, or implement pattern matching.

**Pros:**
- No external process
- Works in non-git directories with `.gitignore`

**Cons:**
- Complex pattern matching (negations, `**`, etc.)
- May not match git behavior exactly

### Recommended Approach

1. **Cache ignored paths**: On directory load, run `git status --ignored --porcelain` once
2. **Filter in GetChildrenAsync**: Check against cached ignored paths
3. **Toggle button**: "Show Ignored" toggle in file explorer toolbar
4. **Visual indicator**: Dim ignored files when shown (similar to VS Code)

### UI Changes

**File Explorer Toolbar:**
```
[🔄 Refresh] [👁 Show Hidden] [◌ Show Ignored] [⚙]
```

**Visual Treatment:**
- Ignored files: 50% opacity, no git status badge
- Option to completely hide (default) or show dimmed

### Configuration

```json
{
  "fileExplorer": {
    "showHiddenFiles": false,
    "showIgnoredFiles": false,
    "respectGitignore": true
  }
}
```

---

## 3. Single Instance UX Improvements

### Current Problem

When running `host` without arguments while the app is already running:
- The new instance silently exits
- User gets no feedback
- May think the app didn't start

### Goal

Provide clear feedback when the app is already running.

### Implementation

**Scenario 1: `host` (no arguments)**
- Detect existing instance via mutex
- Show themed dialog:
  ```
  TerminalHost is already running.

  Use 'host <path>' to open a project, or use -multi flag
  to allow multiple instances.

  [Focus Existing] [Open New Instance] [Cancel]
  ```
- "Focus Existing" sends message to bring existing window to front

**Scenario 2: `host <path>` (with path)**
- Current behavior: sends path to existing instance (correct)

### Configuration

```json
{
  "settings": {
    "singleInstanceBehavior": "ShowDialog"  // "ShowDialog" | "SilentFocus" | "AllowMultiple"
  }
}
```

---

## 4. Multiple Tabs for Same Folder

### Current Problem

Opening the same folder twice focuses the existing tab instead of creating a new one.
Sometimes users want multiple tabs for the same project (e.g., different branches, different terminals).

### Goal

Allow opt-in creation of multiple tabs for the same directory.

### Implementation

**Methods to create duplicate tab:**
1. **Modifier key**: `Ctrl+Shift+N` then select same folder = forces new tab
2. **Command palette**: "New Project Tab (Force New)" command
3. **CLI flag**: `host --new <path>` or `host -n <path>`
4. **Right-click tab**: "Duplicate Tab" context menu option

**Visual distinction:**
- Tabs for same directory show index: `MyProject`, `MyProject (2)`, `MyProject (3)`
- Tooltip shows full path + tab index

### Configuration

```json
{
  "settings": {
    "allowDuplicateTabs": true  // Enable duplicate tab features
  }
}
```

---

## 5. First-Run Setup Experience

### Current Problem

New users may not have dependencies installed and won't know about the setup window.

### Goal

Show setup window on first run, but avoid false positives.

### Detection Criteria

Show first-run setup ONLY when ALL of these are true:
1. Config file does not exist OR is empty/default
2. No `--no-setup` flag passed
3. No `--disable-single-instance` flag (likely testing)
4. App was not launched via `host <path>` (user knows what they're doing)

### Implementation

```csharp
// In App.xaml.cs, before showing MainWindow
bool IsFirstRun()
{
    // Skip if testing flags
    if (startupArgs.DisableSingleInstance || startupArgs.SkipSetup)
        return false;

    // Skip if opened with a path (user knows the app)
    if (startupArgs.HasValidRequest())
        return false;

    // Check if config exists and has been modified
    var configPath = ConfigurationService.GetConfigPath();
    if (!File.Exists(configPath))
        return true;

    var config = ConfigurationService.Load();
    return config.IsDefault();  // New method to check if config is untouched
}
```

**Config.IsDefault() checks:**
- No custom quick commands (beyond defaults)
- No profiles added
- No open folders history
- Settings are all default values

### First-Run Flow

1. App starts → detect first run
2. Show Setup window (blocking, before MainWindow)
3. User clicks "Continue" → setup window closes, MainWindow shows
4. Set `firstRunCompleted: true` in config to prevent re-showing

### Configuration

```json
{
  "settings": {
    "firstRunCompleted": true,
    "firstRunDate": "2025-12-22T00:00:00Z"
  }
}
```

### CLI Flags

| Flag | Description |
|------|-------------|
| `--no-setup` | Skip first-run setup check |
| `/setup` | Force show setup (existing) |

---

## Implementation Phases

### Phase 1: File Explorer .gitignore Support [COMPLETED]
1. ~~Add `GitIgnoreService` using `git check-ignore`~~ - Added `IGitIgnoreService` in Core library
2. ~~Integrate with `FileExplorerService.GetChildrenAsync`~~ - Integrated in `FileExplorerViewModel`
3. ~~Add "Show Ignored" toggle to toolbar~~ - Added toggle button with `◌` icon
4. ~~Add dimmed visual style for ignored files~~ - 50% opacity + "(ignored)" label

### Phase 2: Single Instance & Duplicate Tabs [COMPLETED]
1. ~~Add dialog when running without arguments~~ - Shows custom button dialog with Focus Existing/Open New Instance/Cancel
2. ~~Implement "Duplicate Tab" feature~~ - Right-click context menu + command palette command
3. ~~Add CLI flag `--new`~~ - `host --new <path>` or `host -n <path>` forces new tab
4. ~~Add tab indexing for duplicates~~ - Displays as "MyProject (2)", "MyProject (3)", etc.
5. ~~Add `singleInstanceBehavior` setting~~ - ShowDialog (default), SilentFocus, AllowMultiple
6. ~~Add `allowDuplicateTabs` setting~~ - Enables duplicate tab features (default: true)

### Phase 3: First-Run Setup [COMPLETED]
1. ~~Add `IsFirstRun()` detection logic~~ - Detects untouched config (no open folders, scratch pads, tasks, etc.)
2. ~~Add `--no-setup` CLI flag~~ - Skips first-run setup check
3. ~~Show setup window before MainWindow on first run~~ - Uses existing SetupWindow with `isStartupMode: true`
4. ~~Add `firstRunCompleted` flag to config~~ - Added `FirstRunCompleted` and `FirstRunDate` to AppSettings

### Phase 4: Unified Panel System [COMPLETED]
1. ~~Design `IPanelableViewModel` interface~~ - Created `IPanelableViewModel` with `PanelDisplayState`, `PanelSide` enums and state transition commands
2. ~~Add `PanelHost` control~~ - Created `PanelHost` tabbed container control with panel tabs and content area
3. ~~Add panel state to configuration~~ - Added `PanelStateConfig` class and panel-related properties to `DirectorySettings`
4. ~~Create FileExplorerPanelViewModel wrapper~~ - Wraps existing `FileExplorerViewModel` for panel system
5. ~~Add Markdown Preview panel support~~ - `MarkdownPreviewViewModel` implements `IPanelableViewModel`, created `MarkdownPreviewView` UserControl, full panel/popup/window state transitions
6. ~~Add Git Changes panel support~~ - `GitFilesViewModel` implements `IPanelableViewModel`
7. ~~Add Scratch Pad panel support~~ - `ScratchPadViewModel` implements `IPanelableViewModel`
8. ~~Integrate PanelHost into TerminalPairView~~ - File Explorer now uses PanelHost for docking
9. ~~Create generic PanelPopup control~~ - Uses DraggablePopup for floating panels
10. ~~Create generic PanelWindow~~ - Independent window for detached panels
11. ~~Implement state transitions~~ - Panel↔Popup↔Window with proper cleanup

**Current Architecture:**

```
┌─────────────────────────────────────────────────────────────────┐
│ TerminalPairView                                                │
│ ┌─────────────────────────────────┬───────────────────────────┐ │
│ │ Terminals (Custom/Shell/Run)    │ PanelHost (Right side)    │ │
│ │                                 │ ┌───────────────────────┐ │ │
│ │                                 │ │ [📁 Explorer] [tabs]  │ │ │
│ │                                 │ │ [⊞] [⧉] [×]          │ │ │
│ │                                 │ ├───────────────────────┤ │ │
│ │                                 │ │                       │ │ │
│ │                                 │ │   Active Panel View   │ │ │
│ │                                 │ │   (FileExplorerView)  │ │ │
│ │                                 │ │                       │ │ │
│ │                                 │ └───────────────────────┘ │ │
│ └─────────────────────────────────┴───────────────────────────┘ │
│                                                                 │
│ PanelPopup (floating, hidden until undocked)                    │
└─────────────────────────────────────────────────────────────────┘
```

**State Transitions:**

| From | To | Trigger | Behavior |
|------|-----|---------|----------|
| Panel | Popup | Undock (⊞) | Hide docked panel, show floating popup |
| Panel | Window | Pop-out (⧉) | Hide docked panel, show independent window |
| Popup | Panel | Dock (◧) | Close popup, show docked panel |
| Popup | Window | Pop-out (⧉) | Close popup, show independent window |
| Window | Panel | Dock (◧) | Close window, show docked panel |

**Key Files:**
- `Controls/PanelHost.xaml` - Tabbed container for docked panels
- `Controls/PanelPopup.xaml` - DraggablePopup wrapper for floating panels
- `Views/PanelWindow.xaml` - Generic window for detached panels
- `Resources/PanelContentTemplates.xaml` - DataTemplates mapping ViewModels to Views

### Phase 4.1: Panel System Refinements [COMPLETED]

**Design Decisions:**

1. **Single Popup, Multiple Windows**
   - Only one panel can be in popup state at a time (singleton)
   - Multiple windows allowed for different panels
   - If new popup requested when one exists: dock existing popup back, show new popup

2. **Panel-Specific Header Buttons**
   - Each panel type can define custom toolbar buttons (e.g., Refresh for FileExplorer)
   - `HeaderCommands` collection added to `IPanelableViewModel`
   - Standard buttons (Undock/PopOut/Close) always present; custom buttons prepended with separator

3. **Footer/Status Bar by Mode**
   - **Panel (docked)**: No footer - resizing via GridSplitter
   - **Popup (floating)**: Resize grip (bottom-right) + optional status text bar
   - **Window (detached)**: Native window chrome handles resizing; optional status bar

**Implemented:**

| Item | Status | Description |
|------|--------|-------------|
| Custom header buttons | ✅ Done | `HeaderCommands` property for panel-specific actions |
| Status text support | ✅ Done | `StatusText` property in popup/window footers |
| Edge case handling | ✅ Done | Proper cleanup when transitioning with existing popup |
| Keyboard shortcut handling | ✅ Done | Ctrl+Shift+F focuses popup/window, or docks back to panel |
| Window close state sync | ✅ Done | Closing window via X resets state for next toggle |
| Redundant UI cleanup | ✅ Done | Removed duplicate title from FileExplorerView header |

**API:**

```csharp
// IPanelableViewModel additions
IEnumerable<PanelHeaderCommand>? HeaderCommands { get; }
string? StatusText { get; }

public class PanelHeaderCommand
{
    public required string Icon { get; init; }
    public required string Tooltip { get; init; }
    public required ICommand Command { get; init; }
}
```

**Example - FileExplorerPanelViewModel:**
```csharp
public IEnumerable<PanelHeaderCommand>? HeaderCommands => new[]
{
    new PanelHeaderCommand
    {
        Icon = "🔄",
        Tooltip = "Refresh",
        Command = _explorerViewModel.RefreshCommand
    }
};

public string? StatusText => _explorerViewModel.LastChangedFile != null
    ? $"Changed: {Path.GetFileName(_explorerViewModel.LastChangedFile)}"
    : null;
```

---

## 8. Adding a New Panel (Implementation Guide)

When adding a new panel to the system, follow these steps:

### Step 1: Create the Content View

Create a content-only UserControl (no popup/window chrome):

```
Views/MyPanelContentView.xaml      - Content only, suitable for Panel/Popup/Window modes
Views/MyPanelContentView.xaml.cs   - Code-behind (usually empty)
```

If the panel needs popup functionality, keep the existing popup view:

```
Views/Popups/MyPanelView.xaml      - Wraps DraggablePopup, binds IsOpen to ViewModel
```

### Step 2: Implement IPanelableViewModel

```csharp
public partial class MyPanelViewModel : ObservableObject, IPanelableViewModel
{
    // Required properties
    public string PanelId => "myPanel";
    public string PanelTitle => "My Panel";
    public string PanelIcon => "📄";

    [ObservableProperty]
    private PanelDisplayState _displayState = PanelDisplayState.Panel; // Default state
    // Use PanelDisplayState.Popup for panels that are opened/closed frequently (e.g., Git Changes)
    // Use PanelDisplayState.Panel for panels that should persist (e.g., File Explorer, Scratch Pad)

    [ObservableProperty]
    private PanelSide _preferredSide = PanelSide.Right;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private double _width = 600;

    [ObservableProperty]
    private double _height = 500;

    public PanelSizePreset SizePreset => PanelSizePreset.Medium;

    // Panel commands
    public ICommand DockCommand { get; private set; }
    public ICommand UndockCommand { get; private set; }
    public ICommand DetachCommand { get; private set; }

    // Event for state changes
    public event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;
    public event EventHandler? ShowRequested;  // Notify MainWindow to show panel

    // In Open() method, invoke ShowRequested but DON'T set IsOpen:
    public void Open()
    {
        // ... setup ...
        // NOTE: Do NOT set IsOpen = true here!
        // Let the ShowRequested handler control IsOpen based on DisplayState
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }
}
```

### Step 3: Register DataTemplates

In `Resources/PanelContentTemplates.xaml` and `Controls/PanelHost.xaml`:

```xml
<DataTemplate DataType="{x:Type vm:MyPanelViewModel}">
    <views:MyPanelContentView/>
</DataTemplate>
```

### Step 4: Add to TerminalPairTabViewModel

```csharp
// Property
public MyPanelViewModel? MyPanel { get; private set; }

// Event
public event EventHandler<PanelToggleEventArgs>? MyPanelToggleRequested;

// Methods
public void SetMyPanel(MyPanelViewModel panel) => MyPanel = panel;
public void ToggleMyPanel() { /* Similar to ToggleGitFilesPanel */ }
public void ShowMyPanel() { /* Similar to ShowGitFilesPanel */ }
public void HideMyPanel() { /* Similar to HideGitFilesPanel */ }
```

### Step 5: Wire up in MainWindow

**IMPORTANT: Do NOT add popup views to MainWindow.xaml.** The panel system handles all display modes through:
- **Panel mode**: Content shown via PanelHost using DataTemplates
- **Popup mode**: Currently falls back to Panel mode (popup support can be added later)
- **Window mode**: PanelWindow created dynamically

**In MainWindow.xaml.cs:**
```csharp
// Field for window mode
private Views.PanelWindow? _myPanelWindow;

// In constructor:
_myPanelViewModel.ShowRequested += OnMyPanelShowRequested;
_viewModel.MyPanelRequested += OnMyPanelRequested;

// Request handler (called from keyboard shortcut):
private void OnMyPanelRequested(object? sender, EventArgs e)
{
    var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
    if (currentTab == null) return;

    if (currentTab.MyPanel == null)
        currentTab.SetMyPanel(_myPanelViewModel);

    // If already open, toggle
    if (_myPanelViewModel.IsOpen)
    {
        if (_myPanelViewModel.DisplayState == PanelDisplayState.Window)
        {
            _myPanelWindow?.Close();
            return;
        }
        currentTab.ToggleMyPanel();
        return;
    }

    // Set display state and open
    _myPanelViewModel.DisplayState = PanelDisplayState.Panel;
    _myPanelViewModel.Open();
}

// Show handler (called from ViewModel.ShowRequested):
private void OnMyPanelShowRequested(object? sender, EventArgs e)
{
    switch (_myPanelViewModel.DisplayState)
    {
        case PanelDisplayState.Panel:
            // Show in docked panel
            if (_viewModel.SelectedTab is TerminalPairTabViewModel currentTab)
            {
                if (currentTab.MyPanel == null)
                    currentTab.SetMyPanel(_myPanelViewModel);
                currentTab.ShowMyPanel();
            }
            break;

        case PanelDisplayState.Popup:
            // Show as floating popup
            if (_viewModel.SelectedTab is TerminalPairTabViewModel popupTab)
            {
                if (popupTab.MyPanel == null)
                    popupTab.SetMyPanel(_myPanelViewModel);
                popupTab.ShowPanelAsPopup(_myPanelViewModel);
            }
            break;

        case PanelDisplayState.Window:
            // Create PanelWindow dynamically
            if (_myPanelWindow == null || !_myPanelWindow.IsLoaded)
            {
                _myPanelWindow = new Views.PanelWindow
                {
                    DataContext = _myPanelViewModel,
                    Owner = this,
                    Width = _myPanelViewModel.Width,
                    Height = _myPanelViewModel.Height
                };
                _myPanelWindow.DockRequested += OnMyPanelWindowDockRequested;
                _myPanelWindow.Closed += (s, args) =>
                {
                    _myPanelWindow = null;
                    _myPanelViewModel.DisplayState = PanelDisplayState.Panel;
                };
                _myPanelWindow.Show();
            }
            else
            {
                _myPanelWindow.Activate();
            }
            break;
    }
}
```

### Key Points

1. **No popup views in MainWindow.xaml**: Unlike the old approach, panels don't have popup views in XAML. This prevents `IsOpen` binding conflicts.

2. **Content views are mode-agnostic**: The content view (e.g., `MyPanelContentView.xaml`) is used by:
   - PanelHost (docked mode) via DataTemplates
   - PanelPopup (popup mode) via DataTemplates
   - PanelWindow (window mode) via DataTemplates

3. **DisplayState controls which mode**: The ViewModel's default `DisplayState` determines the initial mode. Set `DisplayState` before calling the open method.

4. **ShowRequested event**: The ViewModel fires this after setup. MainWindow handles it to show in the correct mode.

5. **ShowPanelAsPopup method**: For popup mode, call `currentTab.ShowPanelAsPopup(viewModel)` which triggers `ShowPanelPopup` in the view via the `PopupShowRequested` event.

6. **IsOpen is optional**: Since there's no popup view binding to `IsOpen`, you can use it for internal state tracking or omit it entirely.

7. **Default display states vary by panel type**: Use `PanelDisplayState.Popup` for transient panels (Git Changes) and `PanelDisplayState.Panel` for persistent panels (File Explorer, Scratch Pad).

---

## References

- Panel interface: `src/TerminalHost.Core/Interfaces/IPanelableViewModel.cs`
- Panel host control: `src/TerminalHost/TerminalHost/Controls/PanelHost.xaml`
- Panel templates: `src/TerminalHost/TerminalHost/Resources/PanelContentTemplates.xaml`
- Panel configuration: `src/TerminalHost.Core/Domain/AppConfiguration.cs` (`DirectorySettings`, `PanelStateConfig`)
- Existing file viewer pop-out: `FileViewerWindow.xaml`
- Existing popup system: `Views/Popups/`
- File explorer service: `FileExplorerService.cs`
- Single instance: `SingleInstanceService.cs`
- Git Changes content view: `src/TerminalHost/TerminalHost/Views/GitFilesContentView.xaml`
- Scratch Pad content view: `src/TerminalHost/TerminalHost/Views/ScratchPadContentView.xaml`

---

## 6. Popup/Window Sizing

### PanelSizePreset Enum

Each panel specifies a `SizePreset` that determines how it's sized when shown as popup or window:

```csharp
public enum PanelSizePreset
{
    Compact,  // 350x500, max 400 width - for narrow panels (file explorer)
    Medium,   // 600x500 - general content (scratch pad)
    Large,    // 60%x70% of window - content viewers (markdown, git changes)
    Full,     // 80%x80% of window - immersive content
    Custom    // Use Width/Height properties directly
}
```

### Panel Default Sizes

| Panel | SizePreset | Notes |
|-------|------------|-------|
| File Explorer | Compact | Fixed narrow width for tree views |
| Markdown Preview | Large | Scales with window for readability |
| Git Changes | Large | Needs room for diff view |
| Scratch Pad | Medium | Moderate size for notes |

### Responsive Sizing Constraints

| Preset | Width | Height | Min W | Max W | Min H | Max H |
|--------|-------|--------|-------|-------|-------|-------|
| Compact | fixed | fixed | 300 | 400 | 400 | 800 |
| Medium | fixed | fixed | 500 | 800 | 400 | 700 |
| Large | 60% window | 70% window | 600 | 1200 | 500 | 900 |
| Full | 80% window | 80% window | 800 | 1600 | 600 | 1000 |

---

## 7. Keyboard Shortcut Behavior

See `docs/PANEL_BEHAVIOR.md` for detailed keyboard shortcut specifications.

| Shortcut | Behavior |
|----------|----------|
| Ctrl+Shift+F | Toggle file explorer: if active→hide, if docked but not active→focus, if popup/window→focus |
| Ctrl+M | Toggle markdown: if active→remove, if docked but not active→focus, if not open→open README.md |
| Ctrl+G | Toggle git changes: if active→remove, if docked but not active→focus, if popup/window→focus |
| Ctrl+Shift+N | Toggle scratch pad: if active→remove, if docked but not active→focus, if popup/window→focus |

---

*Document Version: 1.8*
*Created: 2025-12-22*
*Updated: 2025-12-23 - Phase 4.1 Panel System Refinements completed*
*Updated: 2025-12-23 - Markdown Preview fully integrated into panel system*
*Updated: 2025-12-23 - Added PanelSizePreset, fixed undock behavior, keyboard shortcuts*
*Updated: 2025-12-23 - Git Changes and Scratch Pad fully integrated into panel system (Panel/Window states, Popup falls back to Panel)*
*Updated: 2025-12-23 - Rewrote implementation guide: no popup views in XAML, matching Markdown Preview pattern*
*Updated: 2025-12-23 - Git Changes defaults to Popup, Scratch Pad defaults to Panel; added ShowPanelAsPopup support*
