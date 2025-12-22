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

### Priority for Panel Support

| Content | Panel | Popup | Window | Priority |
|---------|-------|-------|--------|----------|
| File Viewer | ✓ (existing) | ✓ (existing) | ✓ (existing) | Done |
| Markdown Preview | New | New | ✓ (existing) | High |
| Git Changes | New | ✓ (existing) | New | Medium |
| Scratch Pad | New | ✓ (existing) | New | Medium |
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

### Phase 4: Unified Panel System
1. Design `IPanelableViewModel` interface
2. Add `PanelHost` to MainWindow
3. Migrate File Viewer to new system
4. Add Markdown Preview panel support
5. Add Git Changes panel support
6. Add Scratch Pad panel support

---

## References

- Existing file viewer pop-out: `FileViewerWindow.xaml`
- Existing popup system: `Views/Popups/`
- File explorer service: `FileExplorerService.cs`
- Single instance: `SingleInstanceService.cs`

---

*Document Version: 1.0*
*Created: 2025-12-22*
