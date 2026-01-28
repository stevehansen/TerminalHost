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
| `Ctrl+Shift+Down` | Next tab | Global | Windows only |
| `Ctrl+Shift+Up` | Previous tab | Global | Windows only |
| `Ctrl+1-9` | Jump to tab 1-9 | Global | |
| `Ctrl+Shift+T` | Open tab switcher | Global | Search and switch tabs |
| `Ctrl+Shift+P` | Open command palette | Global | |
| `Ctrl+Shift+O` | Open repository switcher | Global | |

### Terminal Shortcuts (When Terminal Focused)

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+`` ` | Switch Custom/Shell terminal | Terminal tab | Oem3/OemTilde key |
| `Ctrl+Shift+Left` | Switch terminal | Terminal tab | Windows only |
| `Ctrl+Shift+Right` | Switch terminal | Terminal tab | Windows only |
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
| `Ctrl+F3` | Search across files | Project tab | Full-text search with replace |
| `Ctrl+M` | Open Markdown preview | Project tab | |
| `Ctrl+Shift+I` | Open Timeline Mode | Global | Visual timeline of AI development sessions |
| `Ctrl+Shift+K` | Claude Tasks Panel | Global | View Claude Code task activity |

### Git & GitHub Shortcuts (Requires Project Tab Selected)

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Alt+G` | Unified Git panel | Project tab | Tabbed panel with all Git features (Changes, Branches, History, Stash, Comparison) |
| `Ctrl+G` | Git changes panel | Project tab | Staging + commit UI |
| `Ctrl+H` | Commit history | Project tab | |
| `Ctrl+B` | Branch switcher | Project tab | |
| `Ctrl+Shift+S` | Stash manager | Project tab | |
| `Ctrl+Shift+G` | Reflog | Project tab | Recovery tool for lost commits |
| `Ctrl+Alt+B` | Branch comparison | Project tab | Compare two branches side-by-side |
| `Ctrl+Shift+H` | GitHub Dashboard | Project tab | |
| `Ctrl+Shift+R` | PR Review Mode | Project tab | |

### Build & Test Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+Shift+B` | Build | Project tab | Executes "dev-build" quick command |
| `F6` | Run tests | Project tab | |

### Timeline Mode Shortcuts (When Timeline Tab Focused)

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `↑` / `↓` | Navigate between intents | Timeline | Cycle through intent list |
| `←` / `→` | Navigate between sessions | Timeline | Cycle through sessions in selected intent |
| `Enter` | Open session detail | Timeline | Shows commit, files changed, actions |
| `Escape` | Close session detail | Timeline | |
| `Ctrl+Alt+N` | New Intent | Timeline | Create new intent with worktree |
| `Ctrl+Alt+S` | Start session | Timeline | Start Claude Code in selected intent |
| `Ctrl+Alt+F` | Fork from session | Timeline | Fork from selected session |

### Project Runner Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `F5` | Start/Stop project run | Project tab | Toggle |
| `Shift+F5` | Force stop project run | Project tab | |

### Notes Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+Shift+N` | Open scratch pad | Global | Per-project notes |

### Layout Shortcuts

| Shortcut | Action | Scope | Notes |
|----------|--------|-------|-------|
| `Ctrl+L` | Toggle layout mode | Global | Switch between Tabs and Sidebar |

### Configurable Quick Commands (Default)

These are user-configurable in settings. Default shortcuts:

| Shortcut | Action | Target | Notes |
|----------|--------|--------|-------|
| `Ctrl+Shift+C` | Commit | Custom terminal | Sends "commit" |
| `Ctrl+Shift+D` | Git Pull | Shell terminal | Sends "git pull --rebase" |
| `Ctrl+Shift+U` | Git Push | Shell terminal | Sends "git push" |
| `Ctrl+Shift+L` | Launch IDE | Shell terminal | Sends "dev" |
| `Ctrl+Shift+V` | Review PR | Custom terminal | Claude Code prompt |

**Note:** Additional quick commands (Rate Code, Build, etc.) are available via Command Palette. Users can assign custom shortcuts in Settings.

---

## macOS Platform Notes

On macOS, all `Ctrl+` shortcuts in this document are automatically converted to `Cmd+` (⌘) equivalents. The application handles this conversion internally.

### macOS-Specific Behavior

| Windows Shortcut | macOS Equivalent | Notes |
|------------------|------------------|-------|
| `Ctrl+1-9` | `Cmd+1-9` | Tab jumping |
| `Ctrl+`` ` | `Cmd+`` ` | Terminal switching (may not work on all keyboards) |
| All other `Ctrl+` | `Cmd+` | Standard conversion |
| N/A | `Cmd+Alt+Left` | Previous tab (macOS only) |
| N/A | `Cmd+Alt+Right` | Next tab (macOS only) |

### macOS System Conflicts (Unavoidable)

| Shortcut | macOS System Function | Workaround |
|----------|----------------------|------------|
| `Cmd+M` | Minimize window | Use Command Palette instead |
| `Cmd+H` | Hide application | Use `Cmd+Shift+H` for Commit History |
| `Cmd+Tab` | App switching | Use `Cmd+Alt+Left/Right` for tab cycling |
| `Cmd+Shift+Q` | Log out | Avoid using this combination |
| `Ctrl+Tab` | Does not work when terminal focused | Use `Cmd+Alt+Left/Right` instead |
| `Cmd+Shift+Arrow` | Conflicts with terminal | Use `Cmd+Alt+Left/Right` for tabs |

### Middle-Click Alternative

macOS trackpads don't have a middle-click button. To close tabs:
- Use `Cmd+W` to close the current tab
- Right-click on a tab and select "Close"

---

## Reserved/Unavailable Shortcuts

These shortcuts are reserved by the system or have special meaning:

### Windows

| Shortcut | Reason |
|----------|--------|
| `Ctrl+Alt+Delete` | System reserved |
| `Alt+Tab` | System window switching |
| `Alt+F4` | Close window (system) |
| `Ctrl+Shift+Escape` | Task Manager (system) |

### macOS

| Shortcut | Reason |
|----------|--------|
| `Cmd+Tab` | Application switching |
| `Cmd+H` | Hide application |
| `Cmd+M` | Minimize window |
| `Cmd+Q` | Quit application |
| `Cmd+Shift+Q` | Log out |
| `Cmd+Space` | Spotlight search |

---

## Available Shortcut Slots

### Unused Ctrl+Key combinations:
- `Ctrl+A` - (Select all - may want to reserve for terminal)
- `Ctrl+D` - Available
- `Ctrl+F` - Available (could be used for in-terminal search)
- `Ctrl+I` - Available
- `Ctrl+J` - Available
- `Ctrl+K` - Available
- `Ctrl+Q` - Available
- `Ctrl+R` - Available
- `Ctrl+S` - Available (save - may want to reserve)
- `Ctrl+T` - Available (freed from Task Panel removal)
- `Ctrl+U` - Available
- `Ctrl+X` - (Cut - may want to reserve for terminal)
- `Ctrl+Y` - Available
- `Ctrl+Z` - (Undo - may want to reserve)

### Unused Ctrl+Shift+Key combinations:
- `Ctrl+Shift+A` - Available
- `Ctrl+Shift+J` - Available
- `Ctrl+Shift+M` - Available (freed from Quick Note removal)
- `Ctrl+Shift+Q` - Available (freed from Quick Task removal)
- `Ctrl+Shift+W` - Available
- `Ctrl+Shift+X` - Available
- `Ctrl+Shift+Y` - Available
- `Ctrl+Shift+Z` - Available

**Note:** `Ctrl+Shift+C`, `Ctrl+Shift+D`, `Ctrl+Shift+U`, `Ctrl+Shift+L`, `Ctrl+Shift+V` are used by default Quick Commands but are user-configurable.

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
4. **Update `ShortcutConflictService.cs`** - This is the **single source of truth** for built-in shortcuts in code
5. Implement in `MainWindow.xaml.cs` `OnPreviewKeyDown` method
6. Update `CLAUDE.md` keyboard shortcuts section if it's a major feature

### Code Architecture (Single Source of Truth)

```
src/TerminalHost.Core/Services/ShortcutConflictService.cs
├── BuiltInShortcutSections  ← Add new shortcuts here (authoritative list)
└── BuiltInShortcuts         ← Auto-generated flat dictionary for conflict detection

src/TerminalHost/TerminalHost/ViewModels/HelpViewModel.cs
└── ShortcutSections         ← References ShortcutConflictService (no duplication)
```

**Important:** Only update `ShortcutConflictService.BuiltInShortcutSections`. The Help view (F1) and Settings conflict warnings both derive their data from this single source.

## Shortcut Naming Conventions

- Use `Ctrl+` prefix (not `Control+`)
- Use `Shift+` for shift modifier
- Use `Alt+` for alt modifier
- Function keys: `F1`, `F2`, etc.
- Special keys: `Escape`, `Tab`, `PageUp`, `PageDown`
- For the backtick key, note it's `Oem3` in code

---

*Last updated: 2026-01-28*
