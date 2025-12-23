# Panel System Behavior Specification

## Overview

The panel system allows content views to exist in three states:
- **Panel (Docked)**: Integrated as a tab in the right panel area
- **Popup (Floating)**: Floating within the app, draggable/resizable
- **Window (Detached)**: Independent window, can move to other monitors

## Key Principles

### 1. Multiple Docked Panels
- Multiple panels CAN be docked simultaneously as tabs
- They share the same panel area (right side by default)
- Only ONE panel is active/visible at a time (tab-based)
- Closing/undocking a panel should NOT affect other docked panels

### 2. Single Popup Rule
- Only ONE popup can exist at a time (floating is premium real estate)
- If a new popup is requested while one exists:
  - Dock the existing popup back to panel
  - Show the new popup

### 3. Multiple Windows Allowed
- Multiple windows CAN exist simultaneously
- Each panel can have its own window

## Keyboard Shortcut Behavior

| Shortcut | Panel State | Expected Behavior |
|----------|-------------|-------------------|
| Ctrl+Shift+F | Not open | Open file explorer, add to panels, make active |
| Ctrl+Shift+F | Docked (active) | Hide right panel area |
| Ctrl+Shift+F | Docked (not active) | Make file explorer the active tab |
| Ctrl+Shift+F | Popup | Focus popup, bring to front |
| Ctrl+Shift+F | Window | Focus window, bring to front |
| Ctrl+M | Not open | Open markdown preview, add to panels, make active |
| Ctrl+M | Docked (active) | Remove from panels (toggle off) |
| Ctrl+M | Docked (not active) | Make markdown the active tab |
| Ctrl+M | Popup | Focus popup |
| Ctrl+M | Window | Focus window |

## Panel Operations

### Open Panel (Keyboard Shortcut)
1. If panel is in Window state → focus window
2. If panel is in Popup state → focus popup
3. If panel is docked and active → toggle visibility OR remove from panels
4. If panel is docked but not active → make it the active tab
5. If panel is not in collection → add to panels, make active

### Undock (⊞ button)
1. Remove panel from RightPanels collection
2. Set next panel as active (if any remain)
3. If no panels remain, hide panel area
4. Set panel DisplayState to Popup
5. Show panel as popup (centered)

### Pop-out (⧉ button)
1. Remove panel from RightPanels collection (if docked)
2. Close popup (if in popup mode)
3. Set next panel as active (if any remain in dock)
4. Set panel DisplayState to Window
5. Show panel as window

### Dock (◧ button from popup/window)
1. Close popup or window
2. Add panel back to RightPanels collection
3. Make it the active panel
4. Set DisplayState to Panel
5. Show right panel area

### Close (× button)
1. Remove from collection (if docked)
2. Close popup/window
3. Set IsOpen = false
4. Set next panel as active (if any)

## Popup/Window Sizing

### Size Presets (New)

```csharp
public enum PanelSizePreset
{
    /// <summary>
    /// Compact size for narrow panels (file explorer, tree views)
    /// Default: 350x500, constrained width
    /// </summary>
    Compact,

    /// <summary>
    /// Medium size for general content
    /// Default: 600x500
    /// </summary>
    Medium,

    /// <summary>
    /// Large size for content viewers (markdown, code preview)
    /// Scales with window: 60% width, 70% height (with min/max constraints)
    /// </summary>
    Large,

    /// <summary>
    /// Full size for immersive content
    /// Scales with window: 80% width, 80% height
    /// </summary>
    Full,

    /// <summary>
    /// Custom fixed dimensions specified by Width/Height properties
    /// </summary>
    Custom
}
```

### Size Calculation (DPI-aware)

For responsive sizes (Large, Full):
```
PopupWidth = MainWindow.Width * SizeRatio
PopupHeight = MainWindow.Height * SizeRatio

// Apply constraints
PopupWidth = Clamp(PopupWidth, MinWidth, MaxWidth)
PopupHeight = Clamp(PopupHeight, MinHeight, MaxHeight)
```

| Preset | Width Ratio | Height Ratio | Min W | Max W | Min H | Max H |
|--------|------------|--------------|-------|-------|-------|-------|
| Compact | - | - | 300 | 400 | 400 | 800 |
| Medium | - | - | 500 | 800 | 400 | 700 |
| Large | 60% | 70% | 600 | 1200 | 500 | 900 |
| Full | 80% | 80% | 800 | 1600 | 600 | 1000 |

### Panel Default Sizes

| Panel | Size Preset | Notes |
|-------|-------------|-------|
| File Explorer | Compact | Fixed narrow width for tree |
| Markdown Preview | Large | Scales with window |
| Git Changes | Large | Needs room for diff view |
| Scratch Pad | Medium | Moderate size for notes |
| Task Panel | Medium | List-based content |

## Positioning

### Popup Centering
- Popups should ALWAYS center on the main window when first opened
- Position is remembered if user drags the popup
- Position resets when a different panel becomes a popup

### Window Positioning
- Windows should center on screen initially
- User can move to any position/monitor
- Position can be persisted per-panel in config

## State Persistence

Per-directory settings should include:
```json
{
  "panelStates": {
    "fileExplorer": {
      "displayState": "Panel",
      "isOpen": true
    },
    "markdownPreview": {
      "displayState": "Panel",
      "isOpen": false,
      "lastFilePath": "README.md"
    }
  },
  "rightPanelVisible": true,
  "rightPanelSplitRatio": 0.25,
  "activePanelId": "fileExplorer"
}
```
