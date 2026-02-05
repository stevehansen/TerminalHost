# Unified Panel System & UX Improvements

TerminalHost features a unified UI system where content can transition between docked, floating, and independent window states, along with several core UX enhancements.

## 1. Unified Panel System

Content can exist in one of three states:

| State | Description | Behavior |
|-------|-------------|----------|
| **Panel** | Integrated in main window | Docked left/right, persists across tab switches |
| **Popup** | Floating within app | Modal-ish, centered, single instance |
| **Window** | Detached window | Independent, can be moved to other monitors, multiple instances |

### State Transitions

- **Undock (⊞)**: Transition from Panel to Popup.
- **Pop-out (⧉)**: Transition from Panel or Popup to a detached Window.
- **Dock (◧)**: Transition from Popup or Window back to a docked Panel.

### Default States by Panel Type

| Panel | Default State | Rationale |
|-------|---------------|-----------|
| File Explorer | Panel | Continuous tree navigation |
| Markdown Preview | Panel | Persistent documentation viewing |
| Git Changes | Popup | Frequent quick diff reviews |
| Scratch Pad | Panel | Persistent, easily accessible notes |

## 2. File Explorer: .gitignore Support

The file explorer respects `.gitignore` rules to keep the tree uncluttered.
- **Filtering**: Files ignored by git are hidden by default.
- **Show Ignored**: A toolbar toggle (◌) reveals ignored files.
- **Visuals**: Ignored files are shown with 50% opacity and no git status badges.

## 3. Single Instance & UX Refinements

- **Single Instance UX**: Running `host` without arguments while already open shows a dialog to focus the existing instance or open a new one (using named pipe IPC).
- **Duplicate Tabs**: Support for opening the same folder in multiple tabs (e.g., `host -n .`). Tabs are indexed for clarity: `MyProject (2)`.
- **First-Run Setup**: Automatic detection of untouched configurations to guide new users through the dependency checker (Setup window) on first launch.

## 4. Keyboard Shortcut Behavior

| Shortcut | Behavior |
|----------|----------|
| **Ctrl+Shift+F** | Toggle File Explorer panel |
| **Ctrl+M** | Toggle Markdown Preview (opens README.md if not open) |
| **Alt+G** | Toggle Git Changes panel |
| **Ctrl+Shift+N** | Toggle Scratch Pad panel |

## 5. Popup & Window Sizing

Each panel uses a `SizePreset` (Compact, Medium, Large, Full) to ensure it appears at an appropriate scale for its content when undocked or detached.

- **Compact**: Narrow tree views (File Explorer).
- **Medium**: General content (Scratch Pad).
- **Large**: High-density content (Markdown, Git Changes).

---

*Document Version: 2.0*
*Last Updated: 2025-12-26*