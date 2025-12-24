# Keyboard Shortcuts Reference

This document tracks all keyboard shortcuts used in TerminalHost. Keep this file updated when adding or modifying shortcuts.

## Shortcut Registry

### Global Shortcuts (Always Active)

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Escape` | Close popups/dialogs | Global | Closes any open popup |
| `F1` | Toggle help popup | Global | |
| `Ctrl+,` | Open settings | Global | |
| `Ctrl+P` | Open profiles | Global | |
| `Ctrl+N` | New project (folder picker) | Global | |
| `Ctrl+W` | Close current tab | Global | |
| `Ctrl+PageDown` | Next tab | Global | |
| `Ctrl+PageUp` | Previous tab | Global | |
| `Ctrl+1-9` | Jump to tab 1-9 | Global | |
| `Ctrl+Shift+T` | Open tab switcher | Global | Search and switch tabs |
| `Ctrl+Shift+P` | Open command palette | Global | |
| `Ctrl+Shift+O` | Open repository switcher | Global | |

### Terminal Shortcuts (When Terminal Focused)

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+`` ` | Switch Custom/Shell terminal | Terminal tab | Oem3 key |
| `Ctrl+V` | Paste to terminal | Terminal focused | |
| `Ctrl+C` | Copy from terminal (if selection) | Terminal focused | Falls through if no selection |
| `Tab` | Send tab character | Terminal focused | |
| `Shift+Tab` | Send shift-tab escape sequence | Terminal focused | |

### Project Tab Shortcuts (Requires Project Tab Selected)

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+E` | Open in Explorer | Project tab | |
| `Ctrl+O` | Open file viewer (preview) | Project tab | |
| `Ctrl+Shift+E` | Open file viewer (edit mode) | Project tab | |
| `Ctrl+Shift+F` | Toggle file explorer panel | Project tab | Tree view with git status |
| `Ctrl+G` | Open git changes panel | Project tab | Staging + commit UI |
| `Ctrl+H` | Open commit history | Project tab | |
| `Ctrl+F3` | Search across files | Project tab | Full-text search with replace |
| `Ctrl+B` | Open git branch switcher | Project tab | |
| `Ctrl+Shift+S` | Open git stash manager | Project tab | |
| `Ctrl+T` | Open task panel (focus mode) | Project tab | |
| `Ctrl+M` | Open Markdown preview | Project tab | |
| `Ctrl+Shift+H` | Open GitHub Dashboard | Project tab | |
| `Ctrl+Shift+R` | Open PR Review Mode | Project tab | |

### Project Runner Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `F5` | Start/Stop project run | Project tab | Toggle |
| `Shift+F5` | Force stop project run | Project tab | |
| `F6` | Run tests | Project tab | |

### Notes & Tasks Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+Shift+N` | Open scratch pad | Global | Per-project notes |
| `Ctrl+Shift+Q` | Quick add task | Global | |
| `Ctrl+Shift+M` | Quick add note | Global | |

### Layout Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+L` | Toggle layout mode | Global | Switch between Tabs and Sidebar |
| `Ctrl+Shift+L` | Toggle sidebar | Sidebar mode | Collapse/expand sidebar |

### Configurable Quick Commands (Default)

These are user-configurable in settings. Default shortcuts:

| Shortcut | Action | Target | Notes |
|----------|--------|--------|-------|
| `Ctrl+Shift+C` | Commit | Custom terminal | Sends "commit" |
| `Ctrl+Shift+D` | Git Pull | Shell terminal | Sends "git pull" |
| `Ctrl+Shift+U` | Git Push | Shell terminal | Sends "git push" |

---

## Reserved/Unavailable Shortcuts

These shortcuts are reserved by the system or have special meaning:

| Shortcut | Reason |
|----------|--------|
| `Ctrl+Alt+Delete` | System reserved |
| `Alt+Tab` | System window switching |
| `Alt+F4` | Close window (system) |
| `Ctrl+Shift+Escape` | Task Manager (system) |

---

## Available Shortcut Slots

### Unused Ctrl+Key combinations:
- `Ctrl+A` - (Select all - may want to reserve for terminal)
- `Ctrl+D` - Available
- `Ctrl+F` - Available (could be used for in-terminal search)
- `Ctrl+I` - Available
- `Ctrl+J` - Available
- `Ctrl+K` - Available
- `Ctrl+L` - Available
- `Ctrl+Q` - Available
- `Ctrl+R` - Available
- `Ctrl+S` - Available (save - may want to reserve)
- `Ctrl+U` - Available
- `Ctrl+X` - (Cut - may want to reserve for terminal)
- `Ctrl+Y` - Available
- `Ctrl+Z` - (Undo - may want to reserve)

### Unused Ctrl+Shift+Key combinations:
- `Ctrl+Shift+A` - Available
- `Ctrl+Shift+B` - Available
- `Ctrl+Shift+G` - Available
- `Ctrl+Shift+I` - Available
- `Ctrl+Shift+J` - Available
- `Ctrl+Shift+K` - Available
- `Ctrl+Shift+L` - Available
- `Ctrl+Shift+V` - Available
- `Ctrl+Shift+W` - Available
- `Ctrl+Shift+X` - Available
- `Ctrl+Shift+Y` - Available
- `Ctrl+Shift+Z` - Available

### Unused Function Keys:
- `F2` - Available
- `F3` - Available (F3/Shift+F3 often used for find next/prev)
- `F4` - Available
- `F7` - Available
- `F8` - Available
- `F9` - Available
- `F10` - Available (may activate menu bar on some systems)
- `F11` - Available (often fullscreen)
- `F12` - Available (often dev tools)

### Unused Ctrl+Function Key combinations:
- `Ctrl+F1` - Available
- `Ctrl+F2` - Available
- `Ctrl+F4` - Available (often close tab - may conflict)
- `Ctrl+F5` - Available
- `Ctrl+F6` - Available
- `Ctrl+F7` through `Ctrl+F12` - Available

---

## Adding New Shortcuts

When adding a new shortcut:

1. Check this document for conflicts
2. Consider the scope (global vs project tab vs terminal)
3. Update this file with the new shortcut
4. Update `CLAUDE.md` keyboard shortcuts section
5. Update relevant PRD files if applicable
6. Implement in `MainWindow.xaml.cs` `OnPreviewKeyDown` method

## Shortcut Naming Conventions

- Use `Ctrl+` prefix (not `Control+`)
- Use `Shift+` for shift modifier
- Use `Alt+` for alt modifier
- Function keys: `F1`, `F2`, etc.
- Special keys: `Escape`, `Tab`, `PageUp`, `PageDown`
- For the backtick key, note it's `Oem3` in code

---

*Last updated: 2024-12-24*
