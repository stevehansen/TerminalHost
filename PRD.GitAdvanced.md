# PRD: Advanced Git Features

This document outlines planned advanced git features for TerminalHost, building on the existing git integration (branch management, status display, changes panel).

## Current State

TerminalHost already includes:
- Git status in tab titles (branch, dirty indicator, ahead/behind)
- Git Changes panel (Ctrl+G) with file list and diff viewer
- Git Branch popup (Ctrl+B) with checkout, create, delete, fetch, pull
- GitHub Dashboard (Ctrl+Shift+H) for PRs and issues
- PR Review Mode (Ctrl+Shift+R) with diff viewer and review actions

## Goals

1. **Complete git workflow** - Handle all common git operations without terminal
2. **Visual commit history** - Browse and inspect past commits
3. **Flexible staging** - Stage/unstage at file and hunk level
4. **Commit creation** - Create commits with proper messages from UI
5. **Recovery tools** - Stash, reflog, revert for recovering from mistakes

---

## Commit History Viewer (High Priority)

**Shortcut**: `Ctrl+H`

A comprehensive commit history viewer to browse and inspect past commits without leaving the application.

### Features

- **Commit list**: Display last N commits (configurable, default 50) with:
  - Abbreviated commit hash (7 chars, clickable to copy full hash)
  - Author name and avatar (if available)
  - Relative timestamp (e.g., "2 hours ago", "3 days ago")
  - Commit message summary (first line)
  - Branch/tag decorations
- **Commit details panel**: When selecting a commit:
  - Full commit hash (copyable)
  - Author name and email
  - Committer name and email (if different)
  - Full commit message
  - Parent commit(s)
  - Changed files list with stats (+N/-M lines)
- **Diff viewer**: View changes for any file in the selected commit
- **Pagination**: "Load more" button or infinite scroll for older commits
- **Search/filter**:
  - Filter by author
  - Filter by date range
  - Search commit messages
  - Filter by file path (commits that touched specific file)

### Implementation Notes

- Use `git log --format=...` for commit list
- Use `git show <hash>` for commit details
- Use `git diff <hash>^ <hash>` for commit diffs
- Consider graph visualization for merge commits

### UI Layout

```
+-------------------------------------------------------------+
| Commit History                              [Search] [Filter]|
+-------------------------------------------------------------+
| +-------------------------+ +-----------------------------+ |
| | abc1234  Add feature X  | | Commit: abc1234567890...    | |
| | John Doe - 2 hours ago  | | Author: John Doe            | |
| +-------------------------+ | Date: 2025-12-24 10:30      | |
| | def5678  Fix bug in Y   | |                             | |
| | Jane Doe - 5 hours ago  | | Add feature X for better UX | |
| +-------------------------+ |                             | |
| | ...                     | | Files changed (3):          | |
| |                         | |  M src/App.cs      +15 -3   | |
| |                         | |  A src/Feature.cs  +42 -0   | |
| |                         | |  M tests/Test.cs   +8 -2    | |
| +-------------------------+ +-----------------------------+ |
| +-----------------------------------------------------------+ |
| | [Diff viewer for selected file]                           | |
| +-----------------------------------------------------------+ |
+-------------------------------------------------------------+
```

### Configuration

```json
{
  "settings": {
    "commitHistoryDefaultCount": 50,
    "commitHistoryShowGraph": true
  }
}
```

### Git Commands

| Operation | Command |
|-----------|---------|
| List commits | `git log --format="%H|%h|%an|%ae|%ar|%s|%d" -n 50` |
| Commit details | `git show --stat <hash>` |
| Commit diff | `git diff <hash>^ <hash>` |
| File diff | `git diff <hash>^ <hash> -- <file>` |
| Filter by author | `git log --author="name"` |
| Filter by file | `git log --follow -- <file>` |

---

## Interactive Staging (High Priority)

Enhance the Git Changes panel (Ctrl+G) with staging capabilities.

### Features

- **File-level staging**:
  - Checkbox next to each file to stage/unstage
  - "Stage All" / "Unstage All" buttons
  - Visual distinction between staged and unstaged files
  - Separate sections: "Staged Changes" and "Unstaged Changes"
- **Hunk-level staging**:
  - In diff viewer, show "Stage Hunk" / "Unstage Hunk" buttons
  - Visual indication of which hunks are staged
  - Support for partial file staging
- **Discard changes**:
  - "Discard" button per file (with confirmation)
  - "Discard Hunk" for individual hunks
  - "Discard All" with strong confirmation

### UI Changes to Git Changes Panel

```
+-------------------------------------------------------------+
| Git Changes                    [Stage All] [Unstage All]    |
+-------------------------------------------------------------+
| Staged Changes (2 files)                                    |
| +-- [x] M src/App.cs                    [Unstage] [Discard] |
| +-- [x] A src/New.cs                    [Unstage] [Discard] |
|                                                             |
| Unstaged Changes (3 files)                                  |
| +-- [ ] M src/Other.cs                  [Stage] [Discard]   |
| +-- [ ] ? untracked.txt                 [Stage] [Delete]    |
| +-- [ ] D deleted.cs                    [Stage] [Restore]   |
+-------------------------------------------------------------+
| Diff: src/Other.cs                                          |
| +-----------------------------------------------------------+
| | @@ -10,6 +10,8 @@              [Stage Hunk] [Discard]     |
| |   existing line                                           |
| | + new line 1                                              |
| | + new line 2                                              |
| +-----------------------------------------------------------+
+-------------------------------------------------------------+
```

### Git Commands

| Operation | Command |
|-----------|---------|
| Stage file | `git add <file>` |
| Unstage file | `git reset HEAD <file>` |
| Discard changes | `git checkout -- <file>` |
| Show staged | `git diff --cached` |
| Show unstaged | `git diff` |
| Stage hunk | Parse `git add -p` output or use `git apply --cached` |

---

## Commit Creation UI (High Priority)

Add ability to create commits directly from the Git Changes panel.

### Features

- **Commit message input**:
  - Multi-line text box (subject + body)
  - Character count for subject line (warn if > 72)
  - Conventional commit helpers (dropdown: feat, fix, docs, style, refactor, test, chore)
- **Commit button**: Only enabled when staged changes exist
- **Amend option**: Checkbox to amend last commit (with warning)
- **Sign-off option**: Add Signed-off-by line
- **Commit templates**: Load from `.gitmessage` if configured

### UI Addition to Git Changes Panel

```
+-------------------------------------------------------------+
| Commit Message                              [feat v] [amend]|
| +-----------------------------------------------------------+
| | feat: Add new feature X                          52/72    |
| |                                                           |
| | Longer description of the changes...                      |
| |                                                           |
| +-----------------------------------------------------------+
|                                              [Commit (2)]   |
+-------------------------------------------------------------+
```

### Git Commands

| Operation | Command |
|-----------|---------|
| Create commit | `git commit -m "message"` |
| Amend commit | `git commit --amend -m "message"` |
| Sign-off | `git commit -s -m "message"` |
| Get template | `git config commit.template` |

---

## Stash Operations (Medium Priority)

**Shortcut**: `Ctrl+Shift+S` (Stash popup)

Manage git stash entries through a dedicated UI.

### Features

- **Stash current changes**:
  - Quick stash button in Git Changes panel
  - Optional message input
  - Options: include untracked, keep index
- **Stash list popup**:
  - List all stash entries with message and timestamp
  - Preview stash contents (files changed)
  - Apply, Pop, or Drop actions per entry
  - Create branch from stash
- **Stash diff viewer**: View changes in a stash entry

### UI Layout

```
+-------------------------------------------------------------+
| Git Stash                                         [+ Stash] |
+-------------------------------------------------------------+
| stash@{0}: WIP on main: abc1234 Last commit message         |
|            2 hours ago - 3 files                            |
|            [Apply] [Pop] [Drop] [Branch]                    |
+-------------------------------------------------------------+
| stash@{1}: On feature: Save before switching                |
|            1 day ago - 5 files                              |
|            [Apply] [Pop] [Drop] [Branch]                    |
+-------------------------------------------------------------+
```

### Git Commands

| Operation | Command |
|-----------|---------|
| Create stash | `git stash push -m "message"` |
| Stash with untracked | `git stash push -u -m "message"` |
| List stashes | `git stash list` |
| Show stash contents | `git stash show stash@{N}` |
| Show stash diff | `git stash show -p stash@{N}` |
| Apply stash | `git stash apply stash@{N}` |
| Pop stash | `git stash pop stash@{N}` |
| Drop stash | `git stash drop stash@{N}` |
| Create branch | `git stash branch <name> stash@{N}` |

---

## File History & Blame (Medium Priority)

View the complete history of a specific file and who changed each line.

### Access Methods

- Right-click file in File Explorer -> "View History"
- Right-click file in Git Changes -> "View History"
- Command palette: "Git: File History"
- In file viewer: "History" button

### File History Features

- List all commits that modified the file
- Commit details (hash, author, date, message)
- View file content at any commit
- Diff between any two versions
- "View at this commit" to see full file

### Blame View Features

- Line-by-line annotation showing:
  - Commit hash (abbreviated)
  - Author
  - Date
  - Line content
- Click annotation to see full commit
- Color-code by author or recency
- Toggle between blame and normal view

### UI Layout (Blame View)

```
+-------------------------------------------------------------+
| src/App.cs - Blame                    [History] [Normal]    |
+-------------------------------------------------------------+
| abc1234 John  2d  |  1 | using System;                      |
| abc1234 John  2d  |  2 | using System.IO;                   |
| def5678 Jane  5d  |  3 |                                    |
| def5678 Jane  5d  |  4 | namespace App                      |
| def5678 Jane  5d  |  5 | {                                  |
| ghi9012 John  1w  |  6 |     public class Program           |
| ...                                                         |
+-------------------------------------------------------------+
```

### Git Commands

| Operation | Command |
|-----------|---------|
| File history | `git log --follow -- <file>` |
| Blame | `git blame <file>` |
| File at commit | `git show <hash>:<file>` |
| Compare versions | `git diff <hash1> <hash2> -- <file>` |

---

## Commit/Branch Comparison (Medium Priority)

Compare changes between any two commits, branches, or tags.

### Access Methods

- Command palette: "Git: Compare..."
- Toolbar button in Commit History
- Right-click branch in Branch popup -> "Compare with..."

### Features

- **Selection UI**:
  - Two dropdowns/inputs: "Base" and "Compare"
  - Support commits, branches, tags
  - Autocomplete with recent items
  - Common shortcuts: "Compare with main", "Compare with HEAD~1"
- **Comparison view**:
  - List of changed files
  - Stats per file (+N/-M)
  - Diff viewer for selected file
  - Summary: N files changed, X insertions, Y deletions
- **Special comparisons**:
  - HEAD vs staged
  - HEAD vs working tree
  - Arbitrary range (commit1..commit2)

### UI Layout

```
+-------------------------------------------------------------+
| Compare                                                     |
| Base: [main          v]  Compare: [feature/xyz    v]        |
|                                                  [Compare]  |
+-------------------------------------------------------------+
| 5 files changed, 142 insertions(+), 38 deletions(-)         |
+-------------------------------------------------------------+
| M src/App.cs                                    +45 -12     |
| A src/NewFeature.cs                             +87 -0      |
| M src/Config.cs                                 +10 -26     |
| ...                                                         |
+-------------------------------------------------------------+
| [Diff viewer]                                               |
+-------------------------------------------------------------+
```

### Git Commands

| Operation | Command |
|-----------|---------|
| Compare refs | `git diff <base>..<compare>` |
| Three-dot diff | `git diff <base>...<compare>` |
| Commits in range | `git log <base>..<compare>` |
| Diff stats | `git diff --stat <base>..<compare>` |

---

## Tags Management (Low Priority)

View, create, and manage git tags.

### Access Methods

- Command palette: "Git: Tags"
- Section in Branch popup (collapsible)

### Features

- **Tag list**:
  - All tags sorted by date or name
  - Show tag type (lightweight vs annotated)
  - Show associated commit
  - Show tag message (for annotated)
- **Create tag**:
  - Name input with validation
  - Annotated vs lightweight toggle
  - Message input (for annotated)
  - Target commit (default: HEAD)
- **Tag actions**:
  - Delete local tag
  - Push tag to remote
  - Push all tags
  - Delete remote tag

### Git Commands

| Operation | Command |
|-----------|---------|
| List tags | `git tag -l` |
| Show tag message | `git tag -n <tag>` |
| Create lightweight | `git tag <name>` |
| Create annotated | `git tag -a <name> -m "message"` |
| Push tag | `git push origin <tag>` |
| Push all tags | `git push origin --tags` |
| Delete local | `git tag -d <name>` |
| Delete remote | `git push origin --delete <tag>` |

---

## Cherry-pick UI (Low Priority)

Cherry-pick commits from one branch to another through a visual interface.

### Access Methods

- Right-click commit in History -> "Cherry-pick"
- Command palette: "Git: Cherry-pick..."

### Features

- Select one or multiple commits
- Preview changes before applying
- Handle conflicts with merge UI
- Options: no-commit, edit message

### Git Commands

| Operation | Command |
|-----------|---------|
| Cherry-pick | `git cherry-pick <hash>` |
| No commit | `git cherry-pick --no-commit <hash>` |
| Continue | `git cherry-pick --continue` |
| Abort | `git cherry-pick --abort` |

---

## Revert Commit UI (Low Priority)

Revert specific commits through a visual interface.

### Access Methods

- Right-click commit in History -> "Revert"
- Command palette: "Git: Revert Commit..."

### Features

- Select commit to revert
- Preview revert changes
- Handle conflicts
- Auto-generate revert commit message

### Git Commands

| Operation | Command |
|-----------|---------|
| Revert | `git revert <hash>` |
| No commit | `git revert --no-commit <hash>` |
| Continue | `git revert --continue` |
| Abort | `git revert --abort` |

---

## Merge Conflict Resolution (Low Priority)

Visual merge conflict resolution when conflicts occur.

### Features

- **Conflict detection**: Detect and list conflicted files after merge/rebase/cherry-pick
- **Three-way merge view**:
  - Left: Ours (current branch)
  - Center: Result (editable)
  - Right: Theirs (incoming changes)
- **Quick actions per conflict**:
  - Accept ours
  - Accept theirs
  - Accept both
  - Manual edit
- **Mark resolved**: Button to mark file as resolved
- **Continue/abort**: Continue merge or abort operation

### UI Layout

```
+-------------------------------------------------------------+
| Resolve Conflicts: src/App.cs                               |
+-------------------+-----------------+-----------------------+
| Ours (HEAD)       | Result          | Theirs (feature)      |
+-------------------+-----------------+-----------------------+
| public void Foo() | public void Foo | public void Foo()     |
| {                 | {               | {                     |
|   return 1;       |   return ???;   |   return 2;           |
| }                 | }               | }                     |
+-------------------+-----------------+-----------------------+
| [Accept Ours] [Accept Theirs] [Accept Both] [Mark Resolved] |
+-------------------------------------------------------------+
```

### Git Commands

| Operation | Command |
|-----------|---------|
| List conflicts | `git diff --name-only --diff-filter=U` |
| Mark resolved | `git add <file>` |
| Continue merge | `git merge --continue` |
| Abort merge | `git merge --abort` |
| Continue rebase | `git rebase --continue` |
| Abort rebase | `git rebase --abort` |

---

## Reflog Access (Low Priority)

Access git reflog to recover "lost" commits.

### Access Methods

- Command palette: "Git: Reflog"

### Features

- List reflog entries with:
  - HEAD position (HEAD@{N})
  - Action (commit, checkout, merge, etc.)
  - Commit hash
  - Message/description
  - Timestamp
- Checkout any reflog entry
- Create branch from reflog entry
- View commit details

### Git Commands

| Operation | Command |
|-----------|---------|
| List reflog | `git reflog` |
| Checkout entry | `git checkout HEAD@{N}` |
| Create branch | `git branch <name> HEAD@{N}` |

---

## Submodule Support (Low Priority)

Manage git submodules within projects.

### Features

- **Submodule indicator**: Show in file explorer
- **Submodule status**: Show current commit, dirty status
- **Update submodules**: Update to latest or specific commit
- **Initialize submodules**: Run `git submodule init`
- **Sync submodules**: Sync URLs

### Git Commands

| Operation | Command |
|-----------|---------|
| Status | `git submodule status` |
| Initialize | `git submodule init` |
| Update | `git submodule update` |
| Update to latest | `git submodule update --remote` |
| Sync URLs | `git submodule sync` |

---

## Implementation Priority

| Priority | Feature | Effort | Dependencies | Status |
|----------|---------|--------|--------------|--------|
| **High** | Commit History Viewer | Medium | GitStatusService | **DONE** (Ctrl+H) |
| **High** | Interactive Staging | Low | Git Changes panel | **DONE** |
| **High** | Commit Creation UI | Low | Interactive Staging | **DONE** |
| **Medium** | Stash Operations | Low | GitStatusService | **DONE** (Ctrl+Shift+S) |
| **Medium** | File History & Blame | Medium | Commit History | |
| **Medium** | Commit/Branch Comparison | Medium | Commit History | |
| **Low** | Tags Management | Low | GitStatusService | |
| **Low** | Cherry-pick UI | Low | Commit History | |
| **Low** | Revert Commit UI | Low | Commit History | |
| **Low** | Merge Conflict Resolution | High | Git operations | |
| **Low** | Reflog Access | Low | GitStatusService | |
| **Low** | Submodule Support | Medium | GitStatusService | |

## Service Extensions Required

### IGitStatusService Additions

```csharp
// Commit history
Task<List<GitCommit>> GetCommitHistoryAsync(int count = 50, string? author = null, string? path = null);
Task<GitCommitDetails> GetCommitDetailsAsync(string hash);
Task<string> GetCommitDiffAsync(string hash, string? filePath = null);

// Staging
Task<List<GitFileStatus>> GetStagedFilesAsync();
Task<List<GitFileStatus>> GetUnstagedFilesAsync();
Task<bool> StageFileAsync(string filePath);
Task<bool> UnstageFileAsync(string filePath);
Task<bool> DiscardFileChangesAsync(string filePath);

// Commits
Task<bool> CreateCommitAsync(string message, bool amend = false, bool signOff = false);

// Stash
Task<List<GitStashEntry>> GetStashListAsync();
Task<bool> CreateStashAsync(string? message = null, bool includeUntracked = false);
Task<bool> ApplyStashAsync(int index);
Task<bool> PopStashAsync(int index);
Task<bool> DropStashAsync(int index);

// Blame
Task<List<GitBlameLine>> GetFileBlameAsync(string filePath);

// Tags
Task<List<GitTag>> GetTagsAsync();
Task<bool> CreateTagAsync(string name, string? message = null, string? target = null);
Task<bool> DeleteTagAsync(string name, bool remote = false);
Task<bool> PushTagAsync(string name);

// Comparison
Task<GitComparisonResult> CompareRefsAsync(string baseRef, string compareRef);

// Reflog
Task<List<GitReflogEntry>> GetReflogAsync(int count = 50);
```

---

*Document Version: 1.0*
*Created: 2025-12-24*
