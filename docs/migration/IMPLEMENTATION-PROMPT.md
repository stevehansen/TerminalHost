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

## Feature 12: Timeline Mode ✅ IMPLEMENTED

**Master branch commits:** `304cd1f`, `93119da`, `b8f3ef6`, `eb0ef95`, `16eb644`
**macOS implementation:** Cmd+Shift+I

### What it does
Advanced mode providing a visual timeline view of AI-assisted development work. Organizes development into **intents** (goals/features), each backed by a git worktree, with Claude Code sessions displayed as blocks on a timeline.

### Core Concepts

**Intent = Swimlane = Worktree:**
- 1 swimlane = 1 worktree = 1 intent
- Each intent gets its own git worktree (e.g., `feature/auth`, `hotfix/payment`)
- Intents displayed as horizontal swimlanes in the timeline

**Claude Code Sessions:**
- Blocks on timeline representing Claude Code work
- Track: duration, files changed, commands run, agent notes
- Sessions can fork to try alternative approaches

### Technical Details (from master)

**Domain Models:**
```csharp
public record Intent
{
    public string Id { get; init; }
    public string Name { get; init; }           // "Implement user authentication"
    public string WorktreePath { get; init; }    // Full path to worktree directory
    public string BranchName { get; init; }      // "feature/auth"
    public IntentStatus Status { get; init; }    // Active, Completed, Paused
    public string? ContextFilePath { get; init; } // Path to intent-context.md
    public DateTime CreatedAt { get; init; }
    public List<string> SessionIds { get; init; }
}

public enum IntentStatus { Active, Completed, Paused }

public record ClaudeSession
{
    public string Id { get; init; }
    public string IntentId { get; init; }
    public string? ParentSessionId { get; init; } // null for first session, set for forks
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public ClaudeSessionStatus Status { get; init; }   // Running, Success, Failed, Abandoned
    public string? CommitHash { get; init; }
    public string? CommitMessage { get; init; }
    public List<FileChange> FilesChanged { get; init; }
    public List<string> CommandsExecuted { get; init; }
    public string? AgentNotes { get; init; }
}

public enum ClaudeSessionStatus { Running, Success, Failed, Abandoned }

public record FileChange(string Path, int Additions, int Deletions);

public record TimelineState
{
    public TimeSpan AccumulatedFocusTime { get; init; }
    public TimeScale CurrentScale { get; init; } // Minutes, Hours, Days
    public List<string> VisibleIntentIds { get; init; }
}

public enum TimeScale { Minutes, Hours, Days }
```

**Service Interface (ITimelineService):**
```csharp
public interface ITimelineService
{
    // Intent management
    Task<Intent> CreateIntentAsync(string name, string branchName, string? contextFile = null);
    Task<Intent?> GetIntentAsync(string id);
    Task<List<Intent>> GetAllIntentsAsync();
    Task UpdateIntentAsync(Intent intent);
    Task DeleteIntentAsync(string id);

    // Session management
    Task<ClaudeSession> StartSessionAsync(string intentId, string? parentSessionId = null);
    Task<ClaudeSession?> GetSessionAsync(string id);
    Task UpdateSessionAsync(ClaudeSession session);
    Task<List<ClaudeSession>> GetSessionsForIntentAsync(string intentId);

    // Cherry-pick between intents
    Task<(bool Success, string? Error)> CherryPickToIntentAsync(string sessionId, string targetIntentId);

    // Focus time tracking
    Task StartFocusTimerAsync();
    Task StopFocusTimerAsync();
    TimeSpan GetAccumulatedFocusTime();
}
```

**New Files:**
- `Domain/Intent.cs`
- `Domain/IntentStatus.cs`
- `Domain/ClaudeSession.cs`
- `Domain/ClaudeSessionStatus.cs`
- `Domain/FileChange.cs`
- `Domain/TimelineState.cs`
- `Domain/TimeScale.cs`
- `Domain/OrphanSession.cs`
- `Services/ITimelineService.cs`
- `Services/TimelineService.cs`
- `Services/TranscriptParserService.cs`
- `ViewModels/TimelineModeViewModel.cs`
- `ViewModels/IntentViewModel.cs`
- `ViewModels/SessionBlockViewModel.cs`
- `Views/TimelineModeView.axaml`

**Claude Code Hooks Integration:**
- Uses Claude Code hooks to track session events
- Parses JSONL transcripts to extract commands and summaries
- Tracks file changes and commits automatically

---

## Feature 13: Toast Notification System

**Master branch commit:** `3b07d03`

### What it does
Non-intrusive toast notifications for user feedback instead of blocking dialogs.

### Technical Details (from master)

**Service Interface (IToastService):**
```csharp
public interface IToastService
{
    void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000);
    IProgressToast ShowProgress(string message);
    void Update(string toastId, string message, ToastType type);
    void Close(string toastId);
}

public interface IProgressToast : IDisposable
{
    void Complete(string message);
    void Fail(string message);
    void Update(string message);
}

public enum ToastType { Info, Success, Warning, Error }
```

**New Files:**
- `Services/IToastService.cs`
- `Services/ToastService.cs`
- `ViewModels/ToastViewModel.cs`
- `Views/ToastContainerView.axaml`
- `Views/ToastItemView.axaml`
- `Views/ToastWindow.axaml` (WPF airspace workaround)

**Features:**
- Max 5 visible toasts, others queued
- Auto-close with configurable duration
- Progress toasts for multi-step operations
- Integrated with: Settings save, Dashboard checkout, PR Review actions

---

## Feature 14: Create Worktree Dialog ✅ IMPLEMENTED

**Master branch commit:** `947c8a8`
**macOS implementation:** Workspace Sidebar → New Worktree button

### What it does
Replace simple input dialog with full-featured Create Worktree dialog.

### Technical Details (from master)

**Dialog Features:**
- Branch input text box for new branch names
- Branch selection list showing all local/remote branches
- Auto-selects "Use existing branch" when selecting from list
- Uses short branch name for remote branches
- Auto-generated worktree path with manual editing
- Browse button for custom location
- Validation for existing directories and branch names
- "Open in TerminalHost after creation" checkbox

**IDialogService Addition:**
```csharp
Task<CreateWorktreeDialogResult?> ShowCreateWorktreeDialog(
    string repositoryPath,
    List<GitBranch> branches);

public record CreateWorktreeDialogResult(
    string BranchName,
    string WorktreePath,
    bool CreateNewBranch,
    bool OpenAfterCreation);
```

**New Files:**
- `Views/Dialogs/CreateWorktreeDialog.axaml`

---

## Feature 15: Manage Worktrees Popup ✅ IMPLEMENTED

**Master branch commit:** `e562a48`
**macOS implementation:** Workspace Sidebar → Manage Worktrees context menu

### What it does
Popup for managing all worktrees in a repository.

### Technical Details (from master)

**Popup Features:**
- Shows all worktrees with path, branch, lock status
- Actions: Open, Remove, Lock/Unlock, Copy Path
- New Worktree button opens Create Worktree dialog
- Prune button removes stale worktree entries

**Enhanced Worktree Context Menu in Sidebar:**
- Open in Explorer
- Manage Worktrees...
- Git Fetch, Pull (Rebase), Push
- Remove Worktree...

**New Files:**
- `ViewModels/ManageWorktreesViewModel.cs`
- `Views/Popups/ManageWorktreesView.axaml`

---

## Feature 16: Recent Folders, Markdown Side-by-Side, Side-by-Side Diff, PR Comments ✅ IMPLEMENTED

**Master branch commit:** `fead767`
**macOS implementation:** All sub-features implemented (16a, 16b, 16c, 16d)

### What it does
Four related features bundled together.

### 16a: Recent Folders ✅ IMPLEMENTED
- Track last 20 opened project directories in config
- Display in Repository Switcher popup (Cmd+Shift+O)
- Auto-update when opening new projects
- Recent items shown with "r" indicator in orange

### 16b: Markdown Side-by-Side Editor ✅ IMPLEMENTED
- Tri-state mode for .md files: Preview, Edit, Side-by-Side
- Live preview updates with 300ms debounce
- Available in File Viewer, popup, and detached window

### 16c: Side-by-Side Diff Viewer ✅ IMPLEMENTED
- Toggle between Unified and Side-by-Side views
- Synchronized scrolling columns for old/new versions
- Color-coded additions (green) and deletions (red)
- Available in Git Changes panel and PR Review Mode

### 16d: PR Comments (View Only) ✅ IMPLEMENTED
- Display existing review comments in PR Review popup
- Comments panel with All/Current File filter
- Expandable threads with resolved/outdated status
- Fetch via GitHub GraphQL API

### Technical Details (from master)

**Domain Models:**
```csharp
public enum DiffViewMode { Unified, SideBySide }

public class ParsedDiff
{
    public string FilePath { get; set; }
    public List<DiffHunk> Hunks { get; set; }
}

public class DiffHunk
{
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    public List<DiffLine> Lines { get; set; }
}

public class PrReviewComment
{
    public string Id { get; set; }
    public string Author { get; set; }
    public string Body { get; set; }
    public string FilePath { get; set; }
    public int? Line { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsResolved { get; set; }
    public bool IsOutdated { get; set; }
    public List<PrReviewComment> Replies { get; set; }
}
```

**New Files:**
- `Domain/DiffViewMode.cs`
- `Domain/ParsedDiff.cs`
- `Domain/PrComments.cs`
- `Services/DiffParserService.cs`
- `Controls/SideBySideDiffViewer.axaml`
- `Controls/PrCommentThread.axaml`

**New Service Methods (IGitHubService):**
```csharp
Task<PrComments?> GetPrCommentsAsync(string owner, string repo, int prNumber);
```

---

## Feature 17: Multi-folder Opening & Auto-sort by Usage

**Master branch commit:** `665ab1c`

### What it does
Open multiple folders at once and auto-sort workspaces by usage.

### Technical Details (from master)

**Multi-folder Opening:**
- Add `PickFolders()` method to `IFolderPickerService` for multi-select
- Add `AddWorkspacesAsync()` batch method to WorkspaceSidebarViewModel
- Ctrl+click on Add Workspace button for multi-select

**Auto-sort by Usage:**
- Track focus time per directory (seconds tab was active)
- Store `FocusTimeSecondsByDay` in stats.json
- Calculate usage score: 60% focus time + 40% char count (last 7 days)
- Sort toggle button in sidebar header
- `WorkspaceAutoSort` setting in General Settings

**IFolderPickerService Addition:**
```csharp
Task<List<string>?> PickFolders();
```

**IStatisticsService Additions:**
```csharp
Task TrackFocusTimeAsync(string directory, int seconds);
Task<Dictionary<string, double>> GetUsageScoresAsync(IEnumerable<string> directories);
```

---

## Feature 18: Workspace Git Actions & Auto-fetch ✅ IMPLEMENTED

**Master branch commit:** `aad0b4c`
**macOS implementation:** Workspace sidebar context menu with Git Fetch/Pull/Push, auto-fetch timer (Cmd+Shift+L)

### What it does
Git operations in workspace sidebar with automatic background fetch.

### Technical Details (from master)

**Context Menu Improvements:**
- Simplified Close (combines close tab + remove from sidebar)
- Git Fetch, Pull (Rebase), Push actions
- Git Push disabled when no commits to push (AheadCount == 0)
- Open in Explorer

**Display Improvements:**
- Short branch names (e.g., #123 for issues/123) with full name tooltip
- Activity spinner (yellow) when terminal is active
- Completed indicator (green dot) for unread activity

**Git Auto-fetch:**
- Automatically fetches from remotes every 60 seconds (configurable)
- Keeps BehindCount up to date for all open projects
- New settings: `gitAutoFetch` (default: true), `gitAutoFetchIntervalSeconds` (default: 60)

**AppConfiguration Additions:**
```csharp
public bool GitAutoFetch { get; set; } = true;
public int GitAutoFetchIntervalSeconds { get; set; } = 60;
```

---

## Feature 19: Dashboard Improvements ✅ IMPLEMENTED

**Master branch commit:** `3cb75f3`
**macOS implementation:** Dashboard tab with persistence, size labels, and improved UX

### What it does
Enhanced GitHub Dashboard with persistence and better UX.

### Technical Details (from master)

**Improvements:**
- Dashboard persistence (restores on app restart if was open)
- Size labels (XS/S/M/L/XL/XXL) with colors from GitHub
- Time since update display with tooltip
- Selected section indicator (highlighted background + bold)
- Sort all lists by most recent update first
- Improved Checkout UX: prompt to browse or clone when repo not found
- Wire up PR Review mode from Dashboard items

---

## Feature 20: Terminal Window Title Display

**Master branch commit:** `a1a2a65`

### What it does
Display terminal window titles in terminal headers.

### Technical Details (from master)

**Implementation:**
- Parse OSC escape sequences from terminal output (OSC 0 and OSC 2)
- Add `TerminalTitle` property and `TitleChanged` event to TerminalSession
- Display title with " - " prefix when present in all terminal headers

**TerminalSession Additions:**
```csharp
public string? TerminalTitle { get; private set; }
public event EventHandler<string>? TitleChanged;
```

---

## Feature 21: Syntax Highlighting in Markdown Preview

**Master branch commit:** `87d52ca`

### What it does
Code syntax highlighting in markdown preview with VS Code Dark+ theme.

### Technical Details (from master)

**Implementation:**
- Uses Markdig.SyntaxHighlighting package
- Custom VS Code Dark+ color scheme for code blocks
- Increased default preview window size

---

## Feature 22: PR Description & Squash Merge Preview ✅ IMPLEMENTED

**Master branch commit:** `afe92b2`
**macOS implementation:** PR Review popup (Cmd+Shift+R) with collapsible description and Squash & Merge

### What it does
Show PR description and expected squash commit message.

### Technical Details (from master)

**Improvements:**
- Collapsible PR body/description panel in PR Review view
- Render PR body as markdown using existing MarkdownViewer
- Show expected squash commit message in merge confirmation dialog
- Rename "Merge" button to "Squash & Merge" for clarity
- Fix UTF-8 encoding for gh CLI output (emojis now render correctly)

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

### Core Git Features (Phase 1) ✅ COMPLETE
1. ~~**Git Stash Operations**~~ ✅ DONE
2. ~~**Commit History Viewer**~~ ✅ DONE
3. ~~**Interactive Staging & Commit UI**~~ ✅ DONE
4. ~~**File History & Blame**~~ ✅ DONE
5. ~~**Reflog, Cherry-pick & Revert**~~ ✅ DONE
6. ~~**Search Across Files**~~ ✅ DONE
7. ~~**Shortcut Conflict Warnings**~~ ✅ DONE
8. ~~**Workspace Sidebar**~~ ✅ DONE

### UX Improvements (Phase 2)
9. **.gitignore Support** (Low complexity) - **NOT YET IMPLEMENTED**
10. **First-Run Setup** (Low complexity) - **NOT YET IMPLEMENTED**
11. **Toast Notification System** (Medium) - **NOT YET IMPLEMENTED**
12. **Terminal Window Title Display** (Low) - **NOT YET IMPLEMENTED**
13. **Syntax Highlighting in Markdown** (Low) - **NOT YET IMPLEMENTED**

### Enhanced Features (Phase 3)
14. ~~**Create Worktree Dialog**~~ ✅ DONE
15. ~~**Manage Worktrees Popup**~~ ✅ DONE
16. ~~**Recent Folders**~~ ✅ DONE
17. ~~**Markdown Side-by-Side Editor**~~ ✅ DONE
18. ~~**Side-by-Side Diff Viewer**~~ ✅ DONE
19. ~~**PR Comments (View Only)**~~ ✅ DONE
20. **Multi-folder Opening & Auto-sort** (Medium) - **NOT YET IMPLEMENTED**
21. ~~**Workspace Git Actions & Auto-fetch**~~ ✅ DONE
22. ~~**Dashboard Improvements**~~ ✅ DONE
23. ~~**PR Description & Squash Merge Preview**~~ ✅ DONE

### Advanced Features (Phase 4) ✅ COMPLETE
24. ~~**Timeline Mode**~~ ✅ DONE

**Remaining Features Summary:**
- Phase 2: Features 9-10, 11-13 (5 features remaining)
- Phase 3: Feature 20 (1 feature remaining)
