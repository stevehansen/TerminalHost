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
| **Status** | New |
| **What exists** | Flat commit list in CommitHistoryViewModel (Ctrl+H). Shows hash, subject, author, date. Paginated (50 at a time). Filter by author/message. |
| **Gap** | No visual branch/merge graph lines. GitKraken shows colored lines connecting commits across branches, merge points, and branch divergence. This is the single most distinctive feature of Git GUI clients. |
| **RICE** | R:4 I:4 C:0.5 E:5 → **1.6** |
| **Notes** | Very high effort. Requires custom graph layout algorithm, canvas rendering for lines/nodes, and scroll virtualization. The `git log --graph` ASCII art is an alternative but less polished. Consider: is a visual graph essential when users already have the terminal for `git log --graph --oneline`? |

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
| **What exists** | Search by message text, filter by author. Paginated loading with "Load More". |
| **Gap** | No filter by file path, date range, or commit hash. GitKraken has Ctrl+Alt+F with advanced filtering. |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | File path filter exists in the service layer (not exposed in UI). Date range would be useful for large repos. |

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
| **Status** | New |
| **What exists** | Changed files shown as flat list (file path). |
| **Gap** | GitKraken offers Path (flat) vs Tree (folder hierarchy) toggle for viewing changed files. Tree view groups files by directory. |
| **RICE** | R:3 I:2 C:0.8 E:2 → **2.4** |
| **Notes** | Useful for PRs/commits touching many files across directories. Reuse FileSystemNode tree infrastructure from FileExplorerViewModel. |

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
| **Status** | New |
| **What exists** | Reflog viewer (Ctrl+Shift+G) provides access to history and recovery. Individual operations (cherry-pick, revert) can be aborted. |
| **Gap** | No one-click "undo last git operation" button. GitKraken tracks operation history and provides undo/redo. |
| **RICE** | R:4 I:3 C:0.5 E:4 → **1.5** |
| **Notes** | Complex to implement reliably — need to track operation types and their inverses (undo commit = reset HEAD~1, undo checkout = checkout previous branch, etc.). Reflog already provides the data; this would be a UX convenience layer. Risk of data loss if implemented incorrectly. |

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
| **Status** | New |
| **What exists** | Unified Git Panel (Alt+G) with tabs: Branches, Changes, History, Stash, Compare. This is a popup/panel, not a persistent sidebar. |
| **Gap** | GitKraken has an always-visible left sidebar showing branches, remotes, stashes, PRs, issues, tags with counts — a persistent navigation tree. TerminalHost's git features are accessed via shortcuts/popups. |
| **RICE** | R:4 I:3 C:0.6 E:4 → **1.8** |
| **Notes** | Major architectural change. TerminalHost already has a workspace sidebar and file explorer panel. Adding a persistent git sidebar would compete for screen space. The popup/shortcut approach is arguably more terminal-centric. Consider: a collapsible git section in the existing workspace sidebar instead of a full separate panel. |

### 5.2 Hunk-Level Staging (Partial Staging)

| Field | Value |
|-------|-------|
| **Status** | New |
| **What exists** | File-level staging only. Stage/unstage entire files. |
| **Gap** | GitKraken (and VS Code, Fork, etc.) support staging individual hunks or even lines within a file. This allows committing part of a file's changes. |
| **RICE** | R:3 I:4 C:0.6 E:4 → **1.8** |
| **Notes** | High-value for experienced git users who make atomic commits. Requires parsing diff hunks, rendering them interactively, and running `git add -p` equivalent programmatically. Could use `git add --patch` with stdin scripting or `git apply --cached` with individual hunks. |

### 5.3 Merge Conflict Resolution

| Field | Value |
|-------|-------|
| **Status** | Planned |
| **What exists** | Specified in GitAdvanced.md and RemainingFeatures.md. Not implemented. Cherry-pick/revert/rebase have continue/abort support. |
| **Gap** | No visual three-way merge editor. Conflicts must be resolved in terminal or external tool. |
| **RICE** | R:3 I:3 C:0.5 E:5 → **0.9** |
| **Notes** | Very high effort for a proper three-way merge editor. Most users have preferred merge tools (VS Code, Beyond Compare, etc.). Could offer "open in merge tool" integration instead of building a custom merge UI. |

### 5.4 Fetch All Remotes

| Field | Value |
|-------|-------|
| **Status** | Done |
| **What exists** | `FetchAllAsync` in IGitStatusService. "Fetch" button in branch popup. Auto-fetch on branch popup open. |

### 5.5 Diff Viewer Enhancements

| Field | Value |
|-------|-------|
| **Status** | Partial |
| **What exists** | Syntax-highlighted diff in Git Changes, Commit History, Branch Comparison, PR Review. Side-by-side diff in PR Review. |
| **Gap** | No inline diff annotations (e.g., word-level diff highlighting). No side-by-side diff in the Changes panel (only in PR Review). No diff navigation (next/previous change). |
| **RICE** | R:4 I:3 C:0.7 E:3 → **2.8** |
| **Notes** | Side-by-side diff in Changes panel would be a good enhancement. Word-level highlighting is visually helpful but adds complexity. |

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
| 5.5 | Diff viewer enhancements (side-by-side, navigation) | 2.8 | 3 wk | Enhance |
| 1.3 | Stash count in sidebar | 2.7 | <1 wk | **Done** |
| 4.4 | Quick stash/pop toolbar buttons | 2.7 | <1 wk | Enhance |

### Tier 3: Nice to Have (RICE 1.5–2.4)

| # | Feature | RICE | Effort | Status |
|---|---------|------|--------|--------|
| 1.6 | Tags management | 2.4 | 2 wk | **Done** |
| 1.2 | Remote branch tree view | 2.4 | 2 wk | **Done** |
| 2.4 | Commit table columns layout | 2.4 | 2 wk | **Done** |
| 2.5 | Advanced commit search (file, date) | 2.4 | 2 wk | Enhance |
| 3.4 | Path/Tree view toggle for changed files | 2.4 | 2 wk | New |
| 1.4 | PR sidebar section | 2.1 | 3 wk | Enhance |
| 2.6 | WIP entry in commit history | 2.1 | 2 wk | **Done** |
| 5.1 | Unified git sidebar panel | 1.8 | 4 wk | New |
| 5.2 | Hunk-level staging | 1.8 | 4 wk | New |
| 2.1 | Visual commit graph | 1.6 | 5+ wk | New |
| 4.1 | Undo/redo git operations | 1.5 | 4 wk | New |

### Tier 4: Low Priority (RICE < 1.5)

| # | Feature | RICE | Effort | Status |
|---|---------|------|--------|--------|
| 2.3 | PR indicators on commits | 1.2 | 3 wk | New |
| 1.5 | Issues sidebar section | 0.9 | 3 wk | Enhance |
| 5.3 | Merge conflict resolution | 0.9 | 5+ wk | Planned |

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

### Phase 3: Advanced Staging & Diff (~5 weeks)

Power user features for precise git workflows:

1. **Side-by-side diff** in Git Changes panel (5.5)
2. **Path/Tree toggle** for changed files (3.4)
3. **Advanced commit filters** — file path, date range (2.5)
4. **Hunk-level staging** (5.2) — if demand warrants

### Phase 4: Aspirational (~8+ weeks)

High-effort features to evaluate based on user demand:

1. **Visual commit graph** (2.1) — signature feature of Git GUIs but massive effort
2. **Undo/redo git operations** (4.1) — nice UX layer over reflog
3. **Git sidebar panel** (5.1) — persistent navigation, requires layout redesign
4. **Merge conflict resolution** (5.3) — consider "open in external merge tool" as alternative

---

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-02-05 | Created PRD | Baseline comparison against GitKraken feature set |
| 2026-02-05 | Skip AI commits, cloud patches, teams | Not aligned with TerminalHost's terminal-centric approach |
| 2026-02-05 | Prioritize quick wins over graph visualization | Graph is high effort with uncertain ROI given terminal access to `git log --graph` |
| 2026-02-05 | Phase 1 complete | All 4 quick wins implemented. Pull uses stash/rebase/pop flow. Old shell quick commands replaced with built-in shortcuts. |
| 2026-02-05 | Phase 2 complete | Branch tree view, tags management, WIP entry, compact commit view all implemented. |

---

*Document Version: 1.2*
*Created: 2026-02-05*
