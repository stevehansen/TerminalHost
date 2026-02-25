# PRD: Floating Status Overlay

## Overview

A small, always-on-top floating window that displays terminal activity status when the main TerminalHost window is not focused. Acts as an ambient indicator so users can see at a glance whether their AI assistant is working, waiting for input, or has completed — without switching windows.

## Problem Statement

When developers run an AI assistant (Claude Code, etc.) in TerminalHost and switch to another application (browser, editor, etc.), they lose visibility into terminal state. Currently, the taskbar progress indicator provides a subtle glow, but:

- **Taskbar glow is easy to miss** — especially with many taskbar items or on multi-monitor setups
- **No visibility across virtual desktops** — taskbar glow only visible on the desktop where the window lives
- **No detail** — taskbar glow shows state but not which tab or what's happening

A floating overlay solves these by staying visible on top of other windows, across desktops.

## Goals

1. **Always-visible status** — Show terminal activity state on top of all windows
2. **Non-intrusive** — Small, draggable, click-through-able, never steals focus
3. **Multi-desktop support** — Option to create multiple overlays pinned to different virtual desktops
4. **Configurable size** — Toggle between small (icon-only) and medium (icon + text) modes
5. **Easy access** — Toggle via command palette and keyboard shortcut
6. **Activity-aware** — Reflect the same states as the taskbar indicator (active/waiting/completed/idle)

## Non-Goals

- Not a full terminal emulator or output viewer
- Not a chat interface or input mechanism
- Not a replacement for the main window — clicking the overlay focuses the main window

## Features

### Phase 1: Core Overlay

#### Overlay Window

A borderless, transparent, always-on-top window that displays:

```
Small mode (icon-only, ~48x48):
┌──────┐
│  🔄  │   ← Animated spinner when active
└──────┘

Medium mode (icon + text, ~220x48):
┌────────────────────────┐
│  🔄  Claude is working │
└────────────────────────┘
```

#### Visual States

| State | Icon | Medium Text | Color Accent |
|-------|------|-------------|--------------|
| Active (output flowing) | Spinning indicator | "{AI} is working" | Blue/indeterminate |
| Waiting for input | Amber pulse | "Waiting for input" | Amber |
| Completed | Green check | "Task completed" | Green |
| Idle | Dim circle | "{Project} — idle" | Gray/muted |

Where `{AI}` is the configured custom command name (e.g., "Claude Code") and `{Project}` is the active tab's directory name.

#### Window Behavior

| Property | Value |
|----------|-------|
| Always on top | Yes (`Topmost = true`) |
| Show in taskbar | No |
| Steal focus | Never (WS_EX_NOACTIVATE) |
| Background | Semi-transparent dark (#CC1E1E2E) |
| Corner radius | 8px rounded |
| Resizable | No |
| Draggable | Yes (anywhere on surface) |
| Position persistence | Saved per overlay instance |

#### Interactions

- **Left-click**: Focus/restore the main TerminalHost window
- **Right-click**: Context menu with options:
  - Toggle Small / Medium size
  - Move to: (list of screens)
  - Close this overlay
  - Close all overlays
- **Drag**: Reposition the overlay (position saved on release)
- **Double-click**: Focus main window and switch to the active tab's terminal

### Phase 2: Multi-Instance & Settings

#### Multiple Overlays

Users can create multiple overlay instances, useful for multi-monitor or multi-desktop setups:

- Each overlay has an independent position (saved in config)
- All overlays reflect the same activity state (from the selected tab)
- Create via command palette: "New Status Overlay"
- Each overlay can be individually closed

#### Settings (Ctrl+, section)

```
Status Overlay
──────────────────────────────────
☑ Show overlay when window loses focus
  (Auto-show when TerminalHost is unfocused, auto-hide when focused)

Size:  ○ Small (icon only)  ● Medium (icon + text)

Opacity: [========--] 80%
```

#### Configuration Schema Addition

```json
{
  "settings": {
    "statusOverlay": {
      "enabled": false,
      "autoShowOnUnfocus": false,
      "size": "Medium",
      "opacity": 0.8,
      "instances": [
        {
          "id": "overlay-1",
          "left": 1850,
          "top": 50,
          "size": "Medium"
        }
      ]
    }
  }
}
```

### Phase 3: Enhanced Display

#### Tab Indicator (Medium Mode)

When multiple tabs are open, show which tab is active:

```
┌─────────────────────────────────┐
│  🔄  Claude is working          │
│  TerminalHost (2/5)             │  ← Project name and tab position
└─────────────────────────────────┘
```

#### Progress Hint

For long-running operations, optionally show elapsed time:

```
┌─────────────────────────────────┐
│  🔄  Claude is working (2m 15s) │
└─────────────────────────────────┘
```

## Technical Design

### Architecture

```
┌─────────────────────────────────────────────────────┐
│                   MainWindow                         │
│                                                      │
│  ┌──────────────────┐    ┌────────────────────────┐ │
│  │ Activity State    │───▶│ StatusOverlayService   │ │
│  │ (existing props)  │    │                        │ │
│  │ - IsAnyActive     │    │ - CreateOverlay()      │ │
│  │ - IsWaiting       │    │ - CloseOverlay(id)     │ │
│  │ - HasCompleted    │    │ - CloseAll()           │ │
│  └──────────────────┘    │ - UpdateState()        │ │
│                           │ - overlays[]           │ │
│                           └────────┬───────────────┘ │
│                                    │                  │
│                    ┌───────────────┼───────────────┐  │
│                    ▼               ▼               ▼  │
│            ┌──────────┐    ┌──────────┐    ┌────────┐│
│            │ Overlay1 │    │ Overlay2 │    │  ...   ││
│            │ (Window) │    │ (Window) │    │        ││
│            └──────────┘    └──────────┘    └────────┘│
└─────────────────────────────────────────────────────┘
```

### Reusable Infrastructure

| Need | Existing Pattern | Location |
|------|-----------------|----------|
| Transparent window | `ToastWindow.xaml` | Same XAML attributes |
| Non-activating window | `WS_EX_NOACTIVATE` P/Invoke | `ToastWindow.xaml.cs` |
| Activity state | Observable properties | `TerminalPairTabViewModel` |
| State priority logic | `UpdateTaskbarGlow()` | `MainWindow.xaml.cs` |
| Position persistence | Window state save/restore | `MainWindow.xaml.cs` |
| DPI-aware positioning | DPI scaling helpers | `ToastWindow.xaml.cs` |
| Command registration | `InitializeCommandPalette()` | `MainViewModel.cs` |

### New Components

| Component | Type | Purpose |
|-----------|------|---------|
| `StatusOverlayWindow.xaml` | Window | The floating overlay UI |
| `StatusOverlayWindow.xaml.cs` | Code-behind | Drag, click, positioning |
| `StatusOverlayService.cs` | Service | Manages overlay instances and state |
| `StatusOverlayState` | Enum (Core) | Active, Waiting, Completed, Idle |
| `StatusOverlaySettings` | Domain (Core) | Settings model |

### StatusOverlayService Interface

```csharp
public interface IStatusOverlayService
{
    /// <summary>Creates a new overlay window at default or saved position.</summary>
    void CreateOverlay();

    /// <summary>Closes a specific overlay by ID.</summary>
    void CloseOverlay(string id);

    /// <summary>Closes all overlay windows.</summary>
    void CloseAll();

    /// <summary>Toggles overlay size between Small and Medium.</summary>
    void ToggleSize(string id);

    /// <summary>Updates all overlays with current activity state.</summary>
    void UpdateState(StatusOverlayState state, string projectName, string aiName);

    /// <summary>Shows overlays (e.g., when main window loses focus).</summary>
    void Show();

    /// <summary>Hides overlays (e.g., when main window gains focus).</summary>
    void Hide();

    /// <summary>Number of active overlay instances.</summary>
    int OverlayCount { get; }
}
```

### Integration Points

1. **MainWindow.OnDeactivated** → If `autoShowOnUnfocus` enabled, call `Show()`
2. **MainWindow.OnActivated** → If `autoShowOnUnfocus` enabled, call `Hide()`
3. **Existing activity property changes** → Call `UpdateState()` with current values
4. **Overlay left-click** → Call `MainWindow.Activate()` / restore from minimized
5. **Command palette** → Register toggle/create/close commands

### Keyboard Shortcut

`Ctrl+Shift+Y` — Toggle status overlay (show/hide)

### Command Palette Commands

| Command | Description |
|---------|-------------|
| Toggle Status Overlay | Show or hide the floating status overlay |
| New Status Overlay | Create an additional overlay instance |
| Close All Status Overlays | Close all floating overlays |

## Implementation Plan

### Phase 1 (Core)
1. Add `StatusOverlaySettings` to `AppSettings` in Core
2. Create `StatusOverlayWindow.xaml` — transparent, always-on-top, small/medium modes
3. Create `StatusOverlayService` — manages instances, state updates
4. Wire into `MainWindow` — activity state forwarding, focus/unfocus hooks
5. Register command palette commands with `IntroducedOn` date
6. Add keyboard shortcut `Ctrl+Shift+Y`
7. Update SHORTCUTS.md and ShortcutConflictService

### Phase 2 (Multi-Instance & Settings)
1. Add multi-instance support to service
2. Add Settings UI section
3. Add position persistence per instance
4. Add right-click context menu (size toggle, screen selection, close)

### Phase 3 (Enhanced Display)
1. Add tab position indicator in medium mode
2. Add elapsed time display
3. Add opacity setting

## Success Criteria

1. User can toggle a floating overlay via `Ctrl+Shift+Y` or command palette
2. Overlay reflects terminal activity state (active/waiting/completed/idle) in real-time
3. Overlay stays on top of all windows without stealing focus
4. Overlay is draggable and remembers its position
5. Clicking the overlay focuses the main TerminalHost window
6. Overlay auto-shows/hides when main window loses/gains focus (optional setting)
7. Multiple overlays can coexist for multi-monitor setups
