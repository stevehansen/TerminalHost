# PRD: Git GUI Feature Parity

Tracks feature parity between TerminalHost's git integration and dedicated Git GUI clients (e.g., GitKraken, Fork, SourceTree). Each feature is individually assessed with RICE scoring to guide prioritization.

**Reference**: Screenshot of GitKraken showing left sidebar (branches, remotes, stashes, PRs, issues, tags), center commit graph with branch visualization, and right staging panel.

---

## Scoring Methodology

**RICE Score** = (Reach × Impact × Confidence) / Effort

| Factor | Scale | Description |
|--------|-------|-------------|
| **Reach** | 1-5 | How many users benefit (1=niche, 5=everyone) |
| **Impact** | 1-5 | How much it improves the workflow (1=minimal, 5=transformative) |
| **Confidence** | 0.5-1.0 | How sure we are about the estimates |
| **Effort** | 1-5 | Implementation cost in developer-weeks (1=days, 5=months) |

---

## Feature Inventory

### Legend

| Status | Meaning |
|--------|---------|
| **Done** | Fully implemented |
| **Partial** | Core functionality exists, gaps identified |
| **Planned** | Specified but not started |
| **New** | Not previously planned, identified from GUI comparison |
| **Skip** | Explicitly excluded (not needed for TerminalHost's use case) |

---

## 1. Left Sidebar — Repository Navigation

### 1.1 Local Branches (Tree View)

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Flat searchable branch list in GitBranchViewModel (Ctrl+B). Local/remote branches listed together with filters. Full checkout, create, delete, rename support. Tree/list toggle button groups branches by folder prefix (e.g., `feature/`, `bugfix/`) with collapsible hierarchy and branch counts. |
| **Gap** | None. |
| **RICE** | R:4 I:2 C:0.8 E:2 → **3.2** |
| **Notes** | Tree view toggle added in Phase 2. Groups by TypeGroup (Current/Local/Remote), then splits on `/` for folder hierarchy. |

### 1.2 Remote Branches (Tree View)

| Field | Value |
|-------|-------|
| **Status** | Partial |
| **What exists** | Remote branches shown in branch list with `IsRemote` flag. Fetch, delete remote branch operations. |
| **Gap** | No dedicated "Remotes" section with collapsible remote-grouped tree (e.g., `origin/`, `upstream/`). No remote count badge. |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | Could be implemented as a grouping mode toggle in the existing branch popup. Most users only have 1-2 remotes. |

### 1.3 Stashes (Sidebar Section)

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Full stash management via GitStashViewModel (Ctrl+Shift+S). List, create, apply, pop, drop, create branch from stash. Quick stash from Git Changes panel. Also in Unified Git Panel tab. Stash count (📦N) shown in workspace sidebar, configurable via `showStashCount` setting. |
| **Gap** | None. |
| **RICE** | R:3 I:1 C:0.9 E:1 → **2.7** |
| **Notes** | Stash count in sidebar completed in Phase 1. |

### 1.4 Pull Requests (Sidebar Section)

| Field | Value |
|-------|-------|
| **Status** | Partial |
| **What exists** | GitHub Dashboard (Ctrl+Shift+H) with sections: Review Requests, My PRs, Issues, Failed CI. PR Review Mode (Ctrl+Shift+R) for full PR review workflow. |
| **Gap** | PRs are in a dedicated tab, not an always-visible sidebar section. No inline PR status indicators on branches. No "Assigned To Me" vs "Awaiting My Review" split (currently uses GitHub's "review-requested" filter). |
| **RICE** | R:3 I:3 C:0.7 E:3 → **2.1** |
| **Notes** | Dashboard tab works well for the PR workflow. A sidebar section would require significant redesign. PR indicators on branches would be high-value but complex (requires GitHub API correlation). |

### 1.5 Issues (Sidebar Section)

| Field | Value |
|-------|-------|
| **Status** | Partial |
| **What exists** | Issues section in GitHub Dashboard. Issue number parsing from branch names (e.g., `feature/123-foo` → links to issue #123). |
| **Gap** | No dedicated sidebar section for issues. No issue status indicators. No create-branch-from-issue flow. |
| **RICE** | R:2 I:2 C:0.7 E:3 → **0.9** |
| **Notes** | Low priority. TerminalHost users typically manage issues in browser/GitHub. Branch-from-issue could be useful but niche. |

### 1.6 Tags (Sidebar Section + Management)

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Tags tab in Unified Git Panel (Alt+G → Tags). Full tag management: list, search, create (lightweight or annotated), delete local, push, push all, delete remote. Context menu on tags. |
| **Gap** | No "create tag from commit" in history view (would require right-click context menu on commit items). |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | Implemented in Phase 2. GitTagsViewModel + GitTagsContentView with full CRUD operations via IGitStatusService. |

### 1.7 Teams

| Field | Value |
|-------|-------|
| **Status** | Skip |
| **Notes** | GitKraken-specific feature tied to their collaboration platform. Not relevant for TerminalHost. |

### 1.8 Cloud Patches

| Field | Value |
|-------|-------|
| **Status** | Skip |
| **Notes** | GitKraken-specific cloud feature. Not relevant for TerminalHost. |

---

## 2. Center Panel — Commit Graph & History

### 2.1 Visual Commit Graph (Branch Lines)

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | CommitGraphControl renders colored branch/merge lines alongside the commit list. CommitGraphService computes lane assignments using topological ordering. 10-color palette for lanes. Bezier curves for cross-lane merge edges, straight lines for same-lane connections. |
| **Gap** | None. |
| **RICE** | R:4 I:4 C:0.5 E:5 → **1.6** |
| **Notes** | Implemented in Phase 4. Graph column appears to the left of the commit list in both content and popup views. |

### 2.2 Inline Branch/Tag Labels on Commits

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | `ParsedDecorations` property on GitCommit returns typed `GitDecoration` records with color-coded badges: HEAD (cyan #4EC9B0), LocalBranch (blue #569CD6), RemoteBranch (purple #C586C0), Tag (yellow #DCDCAA). Rendered as colored pill badges in both CommitHistoryView and CommitHistoryContentView. |
| **Gap** | None. |
| **RICE** | R:4 I:2 C:0.9 E:1 → **7.2** |
| **Notes** | Completed in Phase 1. |

### 2.3 PR Indicators on Commits

| Field | Value |
|-------|-------|
| **Status** | New |
| **What exists** | Nothing — commits and PRs are separate views. |
| **Gap** | GitKraken shows PR numbers (#227, #148) inline on the commit graph with status icons (open, merged, draft). |
| **RICE** | R:3 I:2 C:0.6 E:3 → **1.2** |
| **Notes** | Requires correlating commits to PRs via GitHub API (head commit matching). Useful but complex. Could be implemented as a decoration enrichment step. |

### 2.4 Commit Table Columns (Author, Date, SHA)

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Commit items show hash, subject, author, relative date. Toggle between card-style and compact single-row table view via "≡" button. Compact view shows Hash(60px) | Subject+Decorations(*) | Author(100px) | Date(80px) in a dense row layout. |
| **Gap** | No sortable/resizable columns. |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | Compact view toggle implemented in Phase 2. |

### 2.5 Commit Search/Filter

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Search by message text, filter by author, file path, and date range (after/before). Expandable filter panel with Apply/Clear buttons. Paginated loading with "Load More". |
| **Gap** | None. |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | Advanced filters implemented in Phase 3. |

### 2.6 WIP / Working Directory Entry

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Synthetic "✎ Working Changes" entry at top of commit history when repo is dirty. Shows modified file count. Styled with yellow subject and orange hash. Selecting it clears the details pane (no commit details to show). |
| **Gap** | None. |
| **RICE** | R:3 I:2 C:0.7 E:2 → **2.1** |
| **Notes** | Implemented in Phase 2. Uses `IsWipEntry` flag on GitCommit to identify the synthetic entry. |

---

## 3. Right Panel — Staging & Commit

### 3.1 Unstaged Files List

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | GitFilesViewModel shows unstaged files with status icons and colors. Stage individual files or stage all. Drag-and-drop between staged/unstaged. |
| **Gap** | None significant. |

### 3.2 Staged Files List

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Staged files section with unstage individual or unstage all. |
| **Gap** | None significant. |

### 3.3 File Change Count Badge

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | `FileChangeSummary` property shows "N file changes on branch" in both GitFilesView and GitFilesContentView. |
| **Gap** | None. |
| **RICE** | R:4 I:1 C:0.9 E:1 → **3.6** |
| **Notes** | Completed in Phase 1. |

### 3.4 Path/Tree View Toggle for Changed Files

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Toggle button switches between flat list and directory-grouped tree view (HierarchicalDataTemplate with FileTreeNode). Tree view available in both content view and popup view. Separate trees for staged and unstaged files. |
| **Gap** | None. |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | Implemented in Phase 3. Uses recursive path splitting to build tree hierarchy. |

### 3.5 Stage All / Unstage All Buttons

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | "Stage All" and "Unstage All" commands with header buttons. |

### 3.6 Commit Message with Character Counter

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Commit message field with `SubjectLength` counter and `IsSubjectTooLong` warning at 72 chars. Multi-line support. |

### 3.7 Amend Previous Commit

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | `AmendCommit` checkbox in GitFilesViewModel. |

### 3.8 Conventional Commit Prefixes

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Quick prefix buttons (feat:, fix:, docs:, etc.) in commit UI. |
| **Gap** | None — TerminalHost actually has more here than GitKraken. |

### 3.9 Commit Description (Body) Field

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Multi-line commit message supports subject + body (separated by blank line). |

### 3.10 AI Commit Message Composition

| Field | Value |
|-------|-------|
| **Status** | Skip |
| **Notes** | GitKraken's "Compose commits with AI" feature. TerminalHost already has Claude Code available in the adjacent terminal — users can ask the AI directly. |

---

## 4. Toolbar — Quick Actions

### 4.1 Undo/Redo Git Operations

| Field | Value |
|-------|-------|
| **Status** | Partial |
| **What exists** | Reflog viewer (Ctrl+Shift+G) provides access to history and recovery. Individual operations (cherry-pick, revert) can be aborted. "Undo Commit" button in Git Changes panel performs `git reset --soft HEAD~1` (keeps changes staged). |
| **Gap** | No general-purpose undo/redo for all git operations (only undo last commit). |
| **RICE** | R:4 I:3 C:0.5 E:4 → **1.5** |
| **Notes** | Simple undo last commit implemented in Phase 4. Full undo/redo tracking would require significant infrastructure. |

### 4.2 Quick Pull Button

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | ↓ toolbar button and Ctrl+Shift+D shortcut. Uses stash/pull-rebase/pop flow to handle dirty working trees. Toast notifications for progress and result. |
| **Gap** | None. |
| **RICE** | R:4 I:2 C:0.9 E:1 → **7.2** |
| **Notes** | Completed in Phase 1. Replaced old shell-based quick command. |

### 4.3 Quick Push Button

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | ↑ toolbar button and Ctrl+Shift+U shortcut. Toast notifications for progress and result. |
| **Gap** | None. |
| **RICE** | R:4 I:2 C:0.9 E:1 → **7.2** |
| **Notes** | Completed in Phase 1. Replaced old shell-based quick command. |

### 4.4 Quick Stash/Pop Buttons

| Field | Value |
|-------|-------|
| **Status** | Partial |
| **What exists** | Quick stash button in Git Changes panel header. Full stash manager via Ctrl+Shift+S. |
| **Gap** | No top-level toolbar stash/pop buttons. Must open a panel first. |
| **RICE** | R:3 I:1 C:0.9 E:1 → **2.7** |
| **Notes** | Low priority. Current access via Ctrl+Shift+S and quick stash in Changes panel is adequate. |

### 4.5 Quick Branch Create Button

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Create branch available in GitBranchViewModel popup. Also accessible via command palette. |
| **Gap** | None significant — exists in popup, which is reasonable. |

---

## 5. Integrated Features (Cross-Cutting)

### 5.1 Unified Git Sidebar Panel

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Unified Git Panel (Alt+G) with tabs: Branches, Changes, History, Stash, Tags, Compare. Collapsible Git section in workspace sidebar showing current branch, top 10 branches (click to checkout), top 10 tags, and stash count. "Open Git Panel" button in sidebar. |
| **Gap** | None. |
| **RICE** | R:4 I:3 C:0.6 E:4 → **1.8** |
| **Notes** | Implemented as collapsible section in existing workspace sidebar (Phase 4), avoiding layout redesign. Refreshes when active tab changes. |

### 5.2 Hunk-Level Staging (Partial Staging)

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | HunkStagingDiffViewer control with per-hunk Stage/Unstage buttons. DiffParserService.ExtractHunkPatch reconstructs valid single-hunk patches. Uses `git apply --cached` with stdin for staging and `git apply --cached -R` for unstaging. |
| **Gap** | No line-level staging (only hunk-level). |
| **RICE** | R:3 I:4 C:0.6 E:4 → **1.8** |
| **Notes** | Implemented in Phase 3. Replaces DiffViewer with HunkStagingDiffViewer in Git Changes panel. |

### 5.3 Merge Conflict Resolution

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | MergeConflictViewer with three-panel layout: Ours (read-only) | Theirs (read-only) | Result (editable). Per-hunk Accept Ours/Theirs/Both buttons with Prev/Next navigation. MergeConflictViewModel detects merge-in-progress state, parses conflict markers, and supports Save & Mark Resolved, Abort Merge, Continue Merge. Merge conflict banner in Git Changes panel. |
| **Gap** | No diff3-style base content display. No syntax highlighting in merge panels. |
| **RICE** | R:3 I:3 C:0.5 E:5 → **0.9** |
| **Notes** | Implemented in Phase 4. Detects merge state via `.git/MERGE_HEAD` file existence. |

### 5.4 Fetch All Remotes

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | `FetchAllAsync` in IGitStatusService. "Fetch" button in branch popup. Auto-fetch on branch popup open. |

### 5.5 Diff Viewer Enhancements

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | Syntax-highlighted diff in Git Changes, Commit History, Branch Comparison, PR Review. Side-by-side and unified toggle in DiffViewer and SideBySideDiffViewer. HunkStagingDiffViewer in Git Changes panel with per-hunk stage/unstage buttons. |
| **Gap** | No word-level diff highlighting. |
| **RICE** | R:4 I:3 C:0.7 E:3 → **2.8** |
| **Notes** | Side-by-side diff and hunk-level staging implemented in Phase 3. |

---

## Priority Summary

### Tier 1: Quick Wins (RICE ≥ 5.0) — ✅ ALL DONE

| # | Feature | RICE | Effort | Status |
|---|---------|------|--------|--------|
| 2.2 | Inline branch/tag labels as colored badges | 7.2 | 1 wk | **Done** |
| 4.2 | Quick Pull toolbar button | 7.2 | 1 wk | **Done** |
| 4.3 | Quick Push toolbar button | 7.2 | 1 wk | **Done** |

### Tier 2: Moderate Value (RICE 2.5–4.9)

| # | Feature | RICE | Effort | Status |
|---|---------|------|--------|--------|
| 3.3 | File change count summary header | 3.6 | <1 wk | **Done** |
| 1.1 | Branch tree view (folder grouping) | 3.2 | 2 wk | **Done** |
| 5.5 | Diff viewer enhancements (side-by-side, hunk staging) | 2.8 | 3 wk | **Done** |
| 1.3 | Stash count in sidebar | 2.7 | <1 wk | **Done** |
| 4.4 | Quick stash/pop toolbar buttons | 2.7 | <1 wk | Enhance |

### Tier 3: Nice to Have (RICE 1.5–2.4)

| # | Feature | RICE | Effort | Status |
|---|---------|------|--------|--------|
| 1.6 | Tags management | 2.4 | 2 wk | **Done** |
| 1.2 | Remote branch tree view | 2.4 | 2 wk | **Done** |
| 2.4 | Commit table columns layout | 2.4 | 2 wk | **Done** |
| 2.5 | Advanced commit search (file, date) | 2.4 | 2 wk | **Done** |
| 3.4 | Path/Tree view toggle for changed files | 2.4 | 2 wk | **Done** |
| 1.4 | PR sidebar section | 2.1 | 3 wk | Enhance |
| 2.6 | WIP entry in commit history | 2.1 | 2 wk | **Done** |
| 5.1 | Git sidebar section | 1.8 | 4 wk | **Done** |
| 5.2 | Hunk-level staging | 1.8 | 4 wk | **Done** |
| 2.1 | Visual commit graph | 1.6 | 5+ wk | **Done** |
| 4.1 | Undo last commit | 1.5 | 4 wk | **Partial** |

### Tier 4: Low Priority (RICE < 1.5)

| # | Feature | RICE | Effort | Status |
|---|---------|------|--------|--------|
| 2.3 | PR indicators on commits | 1.2 | 3 wk | New |
| 1.5 | Issues sidebar section | 0.9 | 3 wk | Enhance |
| 5.3 | Merge conflict resolution | 0.9 | 5+ wk | **Done** |

### Explicitly Skipped

| # | Feature | Reason |
|---|---------|--------|
| 1.7 | Teams | GitKraken-specific, not applicable |
| 1.8 | Cloud Patches | GitKraken-specific, not applicable |
| 3.10 | AI Commit Messages | Claude Code is already in the adjacent terminal |

---

## Suggested Implementation Phases

### Phase 1: Polish Existing Features — ✅ COMPLETE

All quick wins implemented:

1. ~~**Colored branch/tag badges** in commit history (2.2)~~ ✅
2. ~~**Quick Pull/Push buttons** in toolbar with Ctrl+Shift+D/U shortcuts (4.2, 4.3)~~ ✅
3. ~~**File change count header** in Git Changes panel (3.3)~~ ✅
4. ~~**Stash count** in workspace sidebar with configurable setting (1.3)~~ ✅

### Phase 2: Enhanced Navigation — ✅ COMPLETE

All navigation enhancements implemented:

1. ~~**Branch tree view** with folder grouping and counts (1.1, 1.2)~~ ✅
2. ~~**Tags management** — list, create, delete, push in Unified Git Panel tab (1.6)~~ ✅
3. ~~**WIP entry** at top of commit history with file count (2.6)~~ ✅
4. ~~**Compact table view** option for commit history with toggle button (2.4)~~ ✅

### Phase 3: Advanced Staging & Diff — ✅ COMPLETE

All advanced staging and diff features implemented:

1. ~~**Side-by-side diff** verification in Git Changes panel (5.5)~~ ✅
2. ~~**Path/Tree toggle** for changed files with HierarchicalDataTemplate (3.4)~~ ✅
3. ~~**Advanced commit filters** — message, author, file path, date range (2.5)~~ ✅
4. ~~**Hunk-level staging** with per-hunk stage/unstage buttons (5.2)~~ ✅

### Phase 4: Aspirational — ✅ COMPLETE

All aspirational features implemented:

1. ~~**Visual commit graph** with colored branch lines and lane assignment (2.1)~~ ✅
2. ~~**Undo last commit** button in Git Changes panel (4.1)~~ ✅ (simple undo; full undo/redo deferred)
3. ~~**Git sidebar section** — collapsible branches, tags, stashes in workspace sidebar (5.1)~~ ✅
4. ~~**Merge conflict resolution** with three-panel editor (5.3)~~ ✅

---

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-02-05 | Created PRD | Baseline comparison against GitKraken feature set |
| 2026-02-05 | Skip AI commits, cloud patches, teams | Not aligned with TerminalHost's terminal-centric approach |
| 2026-02-05 | Prioritize quick wins over graph visualization | Graph is high effort with uncertain ROI given terminal access to `git log --graph` |
| 2026-02-05 | Phase 1 complete | All 4 quick wins implemented. Pull uses stash/rebase/pop flow. Old shell quick commands replaced with built-in shortcuts. |
| 2026-02-05 | Phase 2 complete | Branch tree view, tags management, WIP entry, compact commit view all implemented. |
| 2026-02-06 | Phase 3 complete | Side-by-side diff verified, path/tree toggle, advanced commit filters, hunk-level staging all implemented. |
| 2026-02-06 | Phase 4 complete | Visual commit graph, undo last commit, git sidebar section, merge conflict resolution all implemented. |

---

*Document Version: 1.3*
*Created: 2026-02-05*
