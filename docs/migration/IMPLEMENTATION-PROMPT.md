# Implementation Prompt for macOS Feature Porting

Use this document as context when implementing features from the Windows version.

## Current State

The macOS version uses:
- **Avalonia UI** instead of WPF
- **VtNetCore** for terminal emulation (not ConPTY)
- **.axaml** files for views (not .xaml)
- Platform-specific services in `Services/` directory

The Windows implementation is on the `master` branch. Reference it for implementation details.

---

## Feature 1: Interactive Staging & Commit UI ✅ IMPLEMENTED

**Master branch commit:** `ad768e7`
**macOS implementation:** Cmd+G (Git Changes panel)

### What it does
Enhances Git Changes panel (Ctrl+G) with staged/unstaged sections, individual file staging, and commit creation.

### Technical Details (from master)

**Domain Models:**
- `GitCommit.cs` - Commit metadata
- `GitCommitDetails.cs` - Full commit with files
- `GitCommitFile.cs` - File in a commit with stats

**Service Methods (IGitStatusService):**
```csharp
Task<bool> StageFileAsync(string workingDirectory, string filePath);
Task<bool> UnstageFileAsync(string workingDirectory, string filePath);
Task<bool> StageAllAsync(string workingDirectory);
Task<bool> UnstageAllAsync(string workingDirectory);
Task<bool> DiscardChangesAsync(string workingDirectory, string filePath);
Task<(bool Success, string? Error)> CommitAsync(string workingDirectory, string message, bool amend = false);
```

**ViewModel Changes (GitFilesViewModel):**
- Add `StagedFiles` and `UnstagedFiles` collections
- Add `CommitMessage`, `IsAmend` properties
- Add `StageFileCommand`, `UnstageFileCommand`, `StageAllCommand`, `UnstageAllCommand`
- Add `CommitCommand` with validation

**View Changes (GitFilesView.axaml):**
- Split file list into Staged/Unstaged sections
- Add commit message TextBox with character counter
- Add conventional commit prefix buttons (feat, fix, docs, refactor)

---

## Feature 2: Commit History Viewer ✅ IMPLEMENTED

**Master branch commit:** `ad768e7`
**macOS implementation:** Cmd+Shift+H

### What it does
New popup (Ctrl+H) to browse commit history with diff viewing.

### Technical Details (from master)

**Service Methods (IGitStatusService):**
```csharp
Task<List<GitCommit>> GetCommitHistoryAsync(string workingDirectory, int skip = 0, int take = 50, string? author = null);
Task<GitCommitDetails?> GetCommitDetailsAsync(string workingDirectory, string commitHash);
Task<string?> GetFileDiffInCommitAsync(string workingDirectory, string commitHash, string filePath);
```

**New Files:**
- `CommitHistoryViewModel.cs` - List commits, select, view details
- `CommitHistoryView.axaml` - Popup with commit list, details panel, diff viewer

**Key Properties:**
- `Commits` - ObservableCollection of commits
- `SelectedCommit` - Currently selected
- `CommitDetails` - Full details of selected
- `SelectedFile` - File to view diff
- `FileDiff` - Diff content

---

## Feature 3: Git Stash Operations ✅ IMPLEMENTED

**Master branch commit:** `a5bfb5b`
**macOS implementation:** Cmd+Shift+S

### What it does
New popup for stash management.

### Technical Details (from master)

**Domain Model:**
```csharp
public class GitStashEntry
{
    public int Index { get; set; }
    public string Reference { get; set; } // stash@{0}
    public string Message { get; set; }
    public string BranchName { get; set; }
    public DateTime Date { get; set; }
    public string RelativeDate { get; set; }
}
```

**Service Methods (IGitStatusService):**
```csharp
Task<List<GitStashEntry>> GetStashListAsync(string workingDirectory);
Task<(bool Success, string? Error)> StashAsync(string workingDirectory, string? message = null, bool includeUntracked = false);
Task<(bool Success, string? Error)> StashApplyAsync(string workingDirectory, int index);
Task<(bool Success, string? Error)> StashPopAsync(string workingDirectory, int index);
Task<(bool Success, string? Error)> StashDropAsync(string workingDirectory, int index);
Task<(bool Success, string? Error)> StashBranchAsync(string workingDirectory, int index, string branchName);
```

**New Files:**
- `GitStashViewModel.cs`
- `GitStashView.axaml`

---

## Feature 4: File History & Blame ✅ IMPLEMENTED

**Master branch commit:** `25b0996`
**macOS implementation:** File Explorer context menu (View History, View Blame)

### What it does
Two related features for investigating file changes:

1. **File History**: Shows all commits that modified a specific file with diff viewing
2. **File Blame**: Line-by-line annotations showing who changed each line and when

### Screens & Popups

| Component | Type | Size | Description |
|-----------|------|------|-------------|
| FileHistoryView | Popup | 1000x700 | Commit list for file + diff panel |
| FileBlameView | Popup | 1100x750 | Line-by-line blame annotations |

### Keyboard Shortcuts (macOS)

| Shortcut | Action |
|----------|--------|
| Cmd+Shift+B | Open File Blame (when file selected) |

### Access Methods

**File Explorer Context Menu:**
- Right-click file → "View History" (📜 icon)
- Right-click file → "View Blame" (👤 icon) - shows Cmd+Shift+B

**File Viewer Toolbar:**
- 📜 button → View History (visible when file is in git repo)
- 👤 button → View Blame (visible when file is in git repo)

### Exact Functionality

**File History Popup:**
- Left panel: List of commits that touched this file
- Right panel: Diff showing changes in selected commit
- "View at Commit" button to see file content at that point
- Pagination: Load more commits (25 at a time, starts with 50)
- Copy commit hash to clipboard

**File Blame Popup:**
- Left panel: Blame annotations (hash, author, date, line number)
- Right panel: Commit details when line selected
- Color by author toggle (visual differentiation)
- Click any line to see full commit details
- Groups consecutive lines from same commit visually

### Technical Details (from master)

**Domain Models:**
```csharp
public class GitBlameLine
{
    public string CommitHash { get; set; }
    public string Author { get; set; }
    public DateTime Date { get; set; }
    public int LineNumber { get; set; }
    public string Content { get; set; }
    public string AuthorColor { get; set; } // Generated for UI
}

public class GitBlameResult
{
    public string FilePath { get; set; }
    public List<GitBlameLine> Lines { get; set; }
}
```

**Service Methods (IGitStatusService):**
```csharp
Task<GitBlameResult?> GetFileBlameAsync(string workingDirectory, string filePath);
Task<List<GitCommit>> GetFileHistoryAsync(string workingDirectory, string filePath, int skip = 0, int take = 50);
Task<string?> GetFileContentAtCommitAsync(string workingDirectory, string commitHash, string filePath);
Task<string?> GetFileDiffBetweenCommitsAsync(string workingDirectory, string fromHash, string toHash, string filePath);
```

**New Files:**
- `Domain/GitBlame.cs` (GitBlameLine, GitBlameResult)
- `ViewModels/FileBlameViewModel.cs`
- `ViewModels/FileHistoryViewModel.cs`
- `Views/Popups/FileBlameView.axaml`
- `Views/Popups/FileHistoryView.axaml`

**Modified Files:**
- `ViewModels/FileExplorerViewModel.cs` - Add ViewHistoryCommand, ViewBlameCommand
- `ViewModels/FileViewerViewModel.cs` - Add ViewHistoryCommand, ViewBlameCommand, CanViewHistory, CanViewBlame
- `ViewModels/MainViewModel.cs` - Add FileHistoryViewModel, FileBlameViewModel properties
- `Views/FileExplorerView.axaml` - Add context menu items
- `Views/FileViewerView.axaml` - Add toolbar buttons
- `MainWindow.axaml.cs` - Add Cmd+Shift+B keybinding

**Git Commands:**
```bash
# Blame with line-porcelain format
git blame --line-porcelain <file>

# File history (follows renames)
git log --follow --format="%H|%an|%at|%s" -- <file>

# File content at specific commit
git show <hash>:<file>

# Diff between commits for specific file
git diff <from_hash>..<to_hash> -- <file>
```

---

## Feature 5: Git Reflog, Cherry-pick & Revert ✅ IMPLEMENTED

**Master branch commit:** `253d91b`
**macOS implementation:** Cmd+Shift+G (Reflog), Commit History panel (Cherry-pick/Revert)

### What it does
Two related features:

1. **Reflog Viewer**: Browse git reflog to recover lost commits and view HEAD history
2. **Cherry-pick & Revert**: Apply or undo specific commits from Commit History panel

### Screens & Popups

| Component | Type | Size | Description |
|-----------|------|------|-------------|
| ReflogView | Popup | 700x500 | Reflog entries with checkout/branch actions |

### Keyboard Shortcuts (macOS)

| Shortcut | Action |
|----------|--------|
| Cmd+Shift+G | Open Git Reflog popup |

### Access Methods

**Reflog:**
- Keyboard: Cmd+Shift+G
- Command Palette: "Git Reflog"

**Cherry-pick & Revert:**
- Open Commit History (Cmd+Shift+H)
- Select a commit
- In the commit details panel, use "Cherry-pick" or "Revert" buttons

### Exact Functionality

**Reflog Popup:**
- List of reflog entries showing: selector (HEAD@{0}), action (commit, checkout, rebase, etc.), message, relative date
- Select entry to enable actions
- **Checkout**: Reset HEAD to selected reflog entry
- **Create Branch**: Create new branch from selected reflog entry (prompts for branch name)
- **Copy Hash**: Copy full commit hash to clipboard
- Shows 50 entries by default

**Cherry-pick (in Commit History):**
- Confirmation dialog before operation
- Applies the selected commit to current branch
- Shows toast on success/failure
- Continue/Abort commands available if conflicts occur

**Revert (in Commit History):**
- Confirmation dialog explaining it creates a new commit
- Creates a new commit that undoes the changes
- Refreshes commit list after successful revert
- Continue/Abort commands available if conflicts occur

### Technical Details (from master)

**Domain Model:**
```csharp
public class GitReflogEntry
{
    public string Hash { get; set; }
    public string ShortHash { get; set; }
    public string Selector { get; set; } // HEAD@{0}
    public string Action { get; set; } // commit, checkout, rebase, etc.
    public string Message { get; set; }
    public DateTime Date { get; set; }
    public string RelativeDate { get; set; }
}
```

**Service Methods (IGitStatusService):**
```csharp
// Reflog
Task<List<GitReflogEntry>> GetReflogAsync(string workingDirectory, int take = 100);
Task<(bool Success, string? Error)> CreateBranchFromRefAsync(string workingDirectory, string refSpec, string branchName);

// Cherry-pick
Task<(bool Success, string? Error)> CherryPickAsync(string workingDirectory, string commitHash);
Task<(bool Success, string? Error)> CherryPickContinueAsync(string workingDirectory);
Task<(bool Success, string? Error)> CherryPickAbortAsync(string workingDirectory);

// Revert
Task<(bool Success, string? Error)> RevertAsync(string workingDirectory, string commitHash);
Task<(bool Success, string? Error)> RevertContinueAsync(string workingDirectory);
Task<(bool Success, string? Error)> RevertAbortAsync(string workingDirectory);
```

**New Files:**
- `Domain/GitReflogEntry.cs`
- `ViewModels/ReflogViewModel.cs`
- `Views/Popups/ReflogView.axaml`

**Modified Files:**
- `ViewModels/CommitHistoryViewModel.cs` - Add CherryPickCommand, RevertCommitCommand
- `ViewModels/MainViewModel.cs` - Add ReflogViewModel property
- `Views/CommitHistoryContentView.axaml` - Add Cherry-pick/Revert buttons in details panel
- `MainWindow.axaml.cs` - Add Cmd+Shift+G keybinding
- `ViewModels/HelpViewModel.cs` - Add Cmd+Shift+G to shortcuts list

**Git Commands:**
```bash
# Get reflog entries
git reflog --format="%H|%gd|%gs|%ci" -n 100

# Cherry-pick
git cherry-pick <hash>
git cherry-pick --continue
git cherry-pick --abort

# Revert
git revert <hash>
git revert --continue
git revert --abort

# Create branch from ref
git checkout -b <branch_name> <ref_spec>
```

---

## Feature 6: Search Across Files ✅ IMPLEMENTED

**Master branch commit:** `a99f869`
**macOS implementation:** Cmd+F

### What it does
Full-text search across project files (Ctrl+F3).

### Technical Details (from master)

**Domain Models:**
```csharp
public class SearchResult
{
    public string FilePath { get; set; }
    public string RelativePath { get; set; }
    public List<SearchMatch> Matches { get; set; }
    public bool IsExpanded { get; set; }
}

public class SearchMatch
{
    public int LineNumber { get; set; }
    public string LineContent { get; set; }
    public int MatchStart { get; set; }
    public int MatchLength { get; set; }
    public string ContextBefore { get; set; }
    public string ContextAfter { get; set; }
}
```

**New Service (ISearchService):**
```csharp
public interface ISearchService
{
    Task<List<SearchResult>> SearchAsync(
        string directory,
        string searchPattern,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false,
        string? includePattern = null,
        string? excludePattern = null,
        bool respectGitignore = true,
        CancellationToken cancellationToken = default);

    Task<int> ReplaceInFileAsync(string filePath, string search, string replace, bool caseSensitive, bool wholeWord, bool useRegex);
    Task<int> ReplaceAllAsync(string directory, string search, string replace, /* options */);
}
```

**New Files:**
- `SearchService.cs` (uses `git grep` or manual search with gitignore support)
- `SearchAcrossFilesViewModel.cs`
- `SearchAcrossFilesView.axaml`

---

## Feature 7-8: Workspace Sidebar with Worktrees ✅ IMPLEMENTED

**Master branch commit:** `65f7c74`
**macOS implementation:** Cmd+Shift+L (toggle), sidebar header button

### What it does
Alternative layout with sidebar showing workspaces and git worktrees.

### Technical Details (from master)

**Domain Models:**
```csharp
public enum AppLayoutMode { Tabs, Sidebar }

public class Workspace
{
    public string Path { get; set; }
    public string Name { get; set; }
    public bool IsPlayground { get; set; }
    public DateTime LastOpened { get; set; }
}

public class WorktreeInfo
{
    public string Path { get; set; }
    public string Branch { get; set; }
    public string CommitHash { get; set; }
    public bool IsMain { get; set; }
    public bool IsBare { get; set; }
}
```

**New Service (IGitWorktreeService):**
```csharp
public interface IGitWorktreeService
{
    Task<List<WorktreeInfo>> GetWorktreesAsync(string repositoryPath);
    Task<(bool Success, string? Error)> CreateWorktreeAsync(string repositoryPath, string path, string branch, bool createBranch = false);
    Task<(bool Success, string? Error)> RemoveWorktreeAsync(string repositoryPath, string path, bool force = false);
}
```

**New Files:**
- `GitWorktreeService.cs`
- `WorkspaceSidebarViewModel.cs`
- `WorkspaceSidebarView.axaml`

**MainViewModel Changes:**
- Add `LayoutMode` property
- Add `Workspaces` collection
- Add `ToggleLayoutCommand`

---

## Feature 9: .gitignore Support

**Master branch commit:** `eaee789`

### What it does
Hide git-ignored files in file explorer.

### Technical Details (from master)

**New Service (IGitIgnoreService):**
```csharp
public interface IGitIgnoreService
{
    Task<HashSet<string>> GetIgnoredFilesAsync(string workingDirectory);
    bool IsIgnored(string filePath, HashSet<string> ignoredFiles);
}
```

**Implementation:** Uses `git status --ignored --porcelain` to get list.

**FileExplorerViewModel Changes:**
- Add `ShowIgnoredFiles` property
- Filter `Children` based on ignored status
- Add toggle button in view

---

## Feature 10: First-Run Setup

**Master branch commit:** `93273f3`

### What it does
Show setup window on first launch.

### Technical Details (from master)

**AppConfiguration Changes:**
```csharp
public bool FirstRunCompleted { get; set; }
public DateTime? FirstRunDate { get; set; }

public bool IsDefault() =>
    OpenFolders.Count == 0 &&
    ScratchPads.Count == 0 &&
    Tasks.Count == 0 &&
    Profiles.Count <= 1; // Only default profile
```

**App.xaml.cs Changes:**
- Check `IsDefault()` on startup
- Show SetupWindow before MainWindow if first run
- Set `FirstRunCompleted = true` after setup

---

## Feature 11: Shortcut Conflict Warnings ✅ IMPLEMENTED

**Master branch commit:** `d82ef59`
**macOS implementation:** Real-time warnings in Settings (Quick Commands and Profiles sections)

### What it does
Warn when configuring conflicting shortcuts in Settings.

### Screens & Popups

| Component | Type | Description |
|-----------|------|-------------|
| SettingsView | Tab | Shows orange warning text when shortcut conflicts with built-in or other shortcuts |

### Exact Functionality

**Real-time Conflict Detection:**
- When editing a Quick Command shortcut, shows warning if it conflicts with:
  - Built-in shortcuts (Cmd+G, Cmd+B, etc.)
  - Other Quick Command shortcuts
  - Profile shortcuts
- When editing a Profile shortcut, shows warning if it conflicts with:
  - Built-in shortcuts
  - Quick Command shortcuts
  - Other Profile shortcuts

**Save-time Validation:**
- When saving settings (Rich or Raw mode), warns about any shortcut conflicts
- Allows save but notifies user with warning message

### Technical Details (from master)

**New Service (ShortcutConflictService):**
```csharp
public static class ShortcutConflictService
{
    public static readonly Dictionary<string, string> BuiltInShortcuts = new()
    {
        { "Cmd+G", "Git Changes" },
        { "Cmd+B", "Git Branches" },
        // ... all built-in shortcuts (macOS uses Cmd instead of Ctrl)
    };

    public static string? GetConflict(string shortcut, IEnumerable<QuickCommand> quickCommands, IEnumerable<Profile> profiles, string? excludeQuickCommandId, string? excludeProfileName);
    public static List<string> GetAllConflicts(IEnumerable<QuickCommand> quickCommands, IEnumerable<Profile> profiles);
}
```

**New Files:**
- `Services/ShortcutConflictService.cs` - Static service with built-in shortcuts dictionary and conflict detection

**Modified Files:**
- `ViewModels/SettingsTabViewModel.cs` - Add QcShortcutWarning, ProfileShortcutWarning properties and change handlers
- `Views/SettingsView.axaml` - Add warning TextBlocks in Quick Commands and Profiles sections
- `Services/ConfigurationService.cs` - Add conflict validation in ValidateConfiguration method
- `TerminalHost.csproj` - Include ShortcutConflictService.cs

**SettingsTabViewModel Changes:**
- Add `QcShortcutWarning` property for Quick Commands
- Add `ProfileShortcutWarning` property for Profiles
- Add `OnEditQcShortcutChanged` partial method for real-time validation
- Add `OnEditProfileShortcutChanged` partial method for real-time validation

---

## Git Command Reference

Common git commands used in implementations:

```bash
# Staging
git add <file>
git reset HEAD <file>
git checkout -- <file>  # discard changes

# Commits
git commit -m "message"
git commit --amend -m "message"
git log --format="%H|%an|%ae|%at|%s" -n 50
git show <hash> --stat --format="%H|%an|%ae|%at|%B"

# Stash
git stash list --format="%gd|%gs|%ci"
git stash push -m "message" [-u]
git stash apply stash@{0}
git stash pop stash@{0}
git stash drop stash@{0}
git stash branch <branch> stash@{0}

# Blame & History
git blame --line-porcelain <file>
git log --follow --format="%H|%an|%at|%s" -- <file>
git show <hash>:<file>

# Reflog
git reflog --format="%H|%gd|%gs|%ci" -n 100

# Cherry-pick & Revert
git cherry-pick <hash>
git cherry-pick --continue
git cherry-pick --abort
git revert <hash>

# Search
git grep -n -I "pattern" -- "*.cs"

# Worktrees
git worktree list --porcelain
git worktree add <path> <branch>
git worktree remove <path>

# Ignored files
git status --ignored --porcelain
```

---

## Implementation Order Recommendation

1. ~~**.gitignore Support**~~ (Low complexity, improves existing feature) - **NOT YET IMPLEMENTED**
2. ~~**Git Stash Operations**~~ ✅ DONE
3. ~~**Commit History Viewer**~~ ✅ DONE
4. ~~**Interactive Staging & Commit UI**~~ ✅ DONE
5. ~~**File History & Blame**~~ ✅ DONE
6. ~~**Reflog, Cherry-pick & Revert**~~ ✅ DONE
7. ~~**Search Across Files**~~ ✅ DONE
8. ~~**First-Run Setup**~~ (Low, simple UX improvement) - **NOT YET IMPLEMENTED**
9. ~~**Shortcut Conflict Warnings**~~ ✅ DONE
10. ~~**Workspace Sidebar**~~ ✅ DONE

**Remaining Features:**
- Feature 9: .gitignore Support
- Feature 10: First-Run Setup
