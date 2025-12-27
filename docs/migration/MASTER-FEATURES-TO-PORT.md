# Features from Master Branch to Port to macOS

Features added to the Windows version since the macOS branch was created.

---

## 1. Interactive Staging & Commit UI
**Shortcut:** Part of Git Changes (Ctrl+G)

Enhances the Git Changes panel with:
- **Staged/Unstaged sections** - Separate views for staged and unstaged files
- **Stage/Unstage individual files** - Buttons in diff header to stage or unstage specific files
- **Stage All / Unstage All** - Bulk operations in section headers
- **Discard changes** - Discard unstaged changes with confirmation dialog
- **Commit message input** - Multi-line message with character counter
- **Conventional commit helpers** - Quick buttons for feat, fix, docs, refactor prefixes
- **72-char warning** - Visual warning when subject line exceeds recommended length
- **Amend checkbox** - Option to amend the last commit

---

## 2. Commit History Viewer
**Shortcut:** Ctrl+H

Browse and inspect commit history:
- **Commit list** - Paginated list with hash, author, date, message
- **Commit details** - View full details of selected commit
- **Files changed** - List of files with insertion/deletion stats
- **Diff viewer** - View diff for any file in a commit
- **Author filter** - Filter commits by author
- **Copy hash** - Copy commit hash to clipboard

---

## 3. Git Stash Operations
**Shortcut:** Ctrl+Shift+S

Full stash management popup:
- **Create stash** - Save current changes with optional message
- **Include untracked** - Option to include untracked files in stash
- **Stash list** - View all stashes with relative dates
- **Apply / Pop / Drop** - Actions per stash entry
- **Create branch from stash** - Create new branch from stash contents
- **Quick stash button** - One-click stash from Git Changes panel

---

## 4. File History & Blame
**Shortcut:** Ctrl+Shift+B (blame)

View file change history and line-by-line blame:
- **File history** - All commits that modified a specific file
- **Diff per commit** - View what changed in each commit
- **View at commit** - See file content at any historical commit
- **Line blame** - Line-by-line annotations showing commit, author, date
- **Author colors** - Visual differentiation by author
- **Click to details** - Click any line to see full commit details
- **Access via** - File Explorer context menu, File Viewer toolbar, or shortcut

---

## 5. Git Reflog, Cherry-pick & Revert
**Shortcut:** Ctrl+Shift+G (reflog)

Advanced git operations:
- **Reflog viewer** - Browse all ref updates (commits, checkouts, rebases, etc.)
- **Checkout from reflog** - Jump to any point in reflog
- **Create branch from reflog** - Create branch from any reflog entry
- **Cherry-pick** - Apply specific commits to current branch (from Commit History)
- **Revert** - Create revert commits (from Commit History)
- **Continue/Abort** - Handle conflicts during cherry-pick/revert

---

## 6. Search Across Files
**Shortcut:** Ctrl+F3

Full-text search across all project files:
- **Search input** - Search as you type with debouncing
- **Search options** - Case sensitive, whole word, regex toggles
- **File filters** - Include/exclude patterns (e.g., `*.cs`, `!**/bin/**`)
- **Respect .gitignore** - Toggle to honor gitignore rules
- **Results by file** - Grouped results with expand/collapse
- **Match highlighting** - Highlighted matches with context lines
- **Click to open** - Open file at specific line
- **Replace** - Replace in file or replace all functionality

---

## 7. Workspace Sidebar with Worktrees
**Shortcut:** Ctrl+L (toggle layout), Ctrl+Shift+L (toggle sidebar)

Alternative layout with left sidebar:
- **Workspace list** - All open projects with git status
- **Git worktrees** - List and manage worktrees per repository
- **Create worktree** - Dialog with branch selection, auto-generated path
- **Playground section** - Area for experimental/temporary projects
- **Open Tabs section** - Shows non-project tabs (Settings, Statistics)
- **Single-click open** - Click worktree to open in new tab
- **Persisted workspaces** - Remember workspaces across sessions

---

## 8. Workspace Sidebar Enhancements
**Part of Workspace Sidebar**

Additional sidebar features:
- **Context menu** - Right-click actions on workspaces
- **Git actions** - Pull, fetch, push from sidebar
- **Auto-fetch** - Automatically fetch remotes periodically
- **Multi-folder opening** - Ctrl+click to select multiple folders
- **Auto-sort by usage** - Sort workspaces by recent usage (focus time + activity)
- **Usage tracking** - Track time spent in each workspace

---

## 9. .gitignore Support in File Explorer

Hide ignored files in the file explorer:
- **Hide by default** - Git-ignored files hidden automatically
- **Show Ignored toggle** - Button to reveal ignored files
- **Visual indicator** - Ignored files shown at 50% opacity with "(ignored)" label
- **File watcher integration** - Ignore changes to ignored files

---

## 10. First-Run Setup Experience

Onboarding for new users:
- **Auto-detection** - Show setup on first launch (empty config)
- **Dependency check** - Verify Claude CLI, git, shell are available
- **Installation help** - Guide users to install missing dependencies
- **Skip option** - `--no-setup` CLI flag to skip

---

## 11. Keyboard Shortcut Conflict Warnings

Prevent shortcut conflicts in Settings:
- **Conflict detection** - Warns when shortcut conflicts with built-in shortcuts
- **Check against** - Application shortcuts, Quick Commands, Profile shortcuts
- **Visual warning** - Banner below shortcut input when conflict detected

---

## Summary

| # | Feature | Shortcut | Complexity |
|---|---------|----------|------------|
| 1 | Interactive Staging & Commit UI | Ctrl+G | High |
| 2 | Commit History Viewer | Ctrl+H | Medium |
| 3 | Git Stash Operations | Ctrl+Shift+S | Medium |
| 4 | File History & Blame | Ctrl+Shift+B | Medium |
| 5 | Reflog, Cherry-pick & Revert | Ctrl+Shift+G | Medium |
| 6 | Search Across Files | Ctrl+F3 | High |
| 7 | Workspace Sidebar with Worktrees | Ctrl+L | High |
| 8 | Workspace Sidebar Enhancements | - | Medium |
| 9 | .gitignore Support | - | Low |
| 10 | First-Run Setup | - | Low |
| 11 | Shortcut Conflict Warnings | - | Low |
