# PRD: GitHub Workflows for Multi-Repo Management

This document outlines workflows optimized for developers managing many repositories who work primarily through terminals, Git/GitHub, and markdown documentation.

## Implementation Status

| Feature | Shortcut | Status |
|---------|----------|--------|
| GitHub Dashboard (Home Tab) | Ctrl+Shift+H | **Implemented** |
| PR Review Mode | Ctrl+Shift+R | **Implemented** |
| Quick Test Runner | F6 | **Implemented** |
| Repository Quick Access | Ctrl+Shift+O | **Implemented** |
| Markdown Preview Panel | Ctrl+M | **Implemented** |

### Implemented Infrastructure
- `IGitHubService` / `GitHubService` - GitHub CLI wrapper for all gh operations
- `ITestRunnerService` / `TestRunnerService` - Test execution with TRX/console parsing
- `IMarkdownService` / `MarkdownService` - Markdown to HTML conversion using Markdig
- Domain models: `GitHubPullRequest`, `GitHubIssue`, `GitHubWorkflowRun`, `GitHubPrFile`, `GitHubRepository`, `RepositoryItem`, `TestResult`, `TestRunSummary`, `PrReviewComment`
- Configuration: `DashboardSettings`, `RepositorySettings`, `TestingSettings`, `MarkdownSettings` in `AppConfiguration.cs`
- `ProjectType` extended with `TestCommand`, `TestFilePattern`, `TestSingleFileCommand`

## Problem Statement

Developers managing 50+ repositories face daily friction:

1. **Context switching to GitHub web** for PR reviews, CI status, and issue tracking
2. **No unified inbox** - PRs awaiting review are scattered across repos
3. **Lost context** - "What was I working on in this repo?" requires mental reconstruction
4. **Manual test execution** - Tests run ad-hoc rather than integrated into workflow
5. **PR review friction** - Reviewing means: find PR → clone/fetch → checkout → read → test → go back to web

## Goals

1. **GitHub Dashboard** - Unified view of PRs, issues, and CI across all repos
2. **PR Review Mode** - End-to-end PR review without leaving TerminalHost
3. **Test Runner Integration** - One-key test execution with results panel
4. **Repository Quick Access** - Fast switching between frequently-used repos
5. **Markdown Panel** - Live preview for PRD-driven development

---

## Feature 1: GitHub Dashboard (Home Tab)

### Concept

A "Home" tab that shows your GitHub activity across all repositories, similar to the GitHub notifications page but terminal-native and actionable.

### Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│ 🏠 Dashboard                                               🔄 Refresh    │
├───────────────┬──────────────────────────────────────────────────────────┤
│ 📥 Review (5) │  ┌─────────────────────────────────────────────────────┐ │
│ 📤 My PRs (3) │  │ PR #423 - Add retry logic to API client            │ │
│ 🔴 CI Failed  │  │ repo: acme/backend  •  by: @alice  •  2h ago       │ │
│ 📌 Issues (8) │  │ +142 -23  •  CI: ✓ passing  •  Reviews: 1/2        │ │
│               │  │ [Open] [Checkout] [Review]                          │ │
│ ───────────── │  ├─────────────────────────────────────────────────────┤ │
│ Recent Repos  │  │ PR #89 - Fix authentication timeout                │ │
│ • ConHoster   │  │ repo: acme/auth-service  •  by: @bob  •  5h ago    │ │
│ • backend     │  │ +45 -12  •  CI: ✗ failing  •  Reviews: 0/1         │ │
│ • frontend    │  │ [Open] [Checkout] [Review]                          │ │
│ • shared-lib  │  └─────────────────────────────────────────────────────┘ │
└───────────────┴──────────────────────────────────────────────────────────┘
```

### Sections

| Section | Description | GitHub CLI Command |
|---------|-------------|-------------------|
| Review Requests | PRs where you're requested reviewer | `gh pr list --search "review-requested:@me"` |
| My PRs | PRs you authored, grouped by status | `gh pr list --author @me --state all` |
| CI Failed | Your PRs or watched repos with failing CI | `gh run list --status failure` |
| Issues | Issues assigned to you across repos | `gh issue list --assignee @me` |
| Recent Repos | Quick access to recently opened projects | Local tracking |

### Actions

| Action | Behavior |
|--------|----------|
| Open | Open PR/issue in browser |
| Checkout | Clone repo (if needed) + checkout PR branch + open as tab |
| Review | Enter PR Review Mode (see Feature 2) |
| Start | Create task from issue, checkout branch |

### Data Fetching

- Uses `gh` CLI (already a dependency for Task/Focus Mode)
- Background refresh every 5 minutes (configurable)
- Manual refresh button
- Caches results to avoid rate limiting
- Shows "last updated: X minutes ago" timestamp

### Configuration

```json
{
  "settings": {
    "dashboard": {
      "enabled": true,
      "refreshIntervalMinutes": 5,
      "watchedOrgs": ["mycompany", "myteam"],
      "excludedRepos": ["mycompany/archived-repo"],
      "showCIStatus": true
    }
  }
}
```

---

## Feature 2: PR Review Mode

### Concept

A dedicated mode for reviewing pull requests with all information inline, without switching to browser.

### Activation

- From Dashboard: Click "Review" on any PR
- From Command Palette: "PR: Review #123" or "PR: Review Current"
- Keyboard: `Ctrl+Shift+R` when in a repo with a PR checked out

### Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│ 🔍 Review: PR #423 - Add retry logic to API client         [✓] [✗] [💬] │
├──────────────────────────────────────────────────────────────────────────┤
│ Tab: ConHoster [main] │ Tab: backend [pr/423-retry-logic] │ ...         │
├───────────────────────┴──────────────────────────────────────────────────┤
│                                                                          │
│  ┌─ Changed Files ──────────────────────────────────────────────────┐   │
│  │ M src/api/client.ts                                    +89  -12  │   │
│  │ M src/api/retry.ts                                     +142 -0   │   │
│  │ A src/api/__tests__/retry.test.ts                      +67  -0   │   │
│  │ M package.json                                         +2   -1   │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│  ┌─ Diff: src/api/client.ts ────────────────────────────────────────┐   │
│  │  45 │   async function fetchWithRetry(url: string) {             │   │
│  │  46 │+    const maxRetries = 3;                                  │   │
│  │  47 │+    for (let attempt = 0; attempt < maxRetries; attempt++) │   │
│  │  ...                                                              │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│  ┌─ Terminal ───────────────────────────────────────────────────────┐   │
│  │ $ npm test                                                        │   │
│  │ PASS src/api/__tests__/retry.test.ts                             │   │
│  │ ✓ should retry on 5xx errors (45ms)                              │   │
│  │ ✓ should not retry on 4xx errors (12ms)                          │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│  [Run Tests: F5]  [Comment: C]  [Approve: A]  [Request Changes: X]      │
└──────────────────────────────────────────────────────────────────────────┘
```

### Review Actions

| Key | Action | GitHub CLI |
|-----|--------|------------|
| `A` | Approve PR | `gh pr review --approve` |
| `X` | Request Changes (opens comment dialog) | `gh pr review --request-changes -b "..."` |
| `C` | Add Comment (general or on line) | `gh pr review --comment -b "..."` |
| `M` | Merge PR | `gh pr merge --squash` (or configured method) |
| `F5` | Run tests | Configurable per project type |
| `Esc` | Exit review mode | Returns to normal terminal view |

### Workflow

1. User clicks "Review" on PR from Dashboard
2. TerminalHost:
   - Opens/focuses the repo tab (clones if needed)
   - Fetches and checks out PR branch: `gh pr checkout 423`
   - Enters Review Mode UI
   - Shows changed files list
3. User navigates files, reads diffs, runs tests
4. User approves/requests changes
5. Exit review mode → stays on branch or switches back

### Line Comments

When viewing a diff, user can:
- Press `C` on a specific line to add inline comment
- Comments are batched into a review
- Submit all comments with approval/request-changes

---

## Feature 3: Quick Test Runner

### Concept

One-key test execution with results panel, optimized for TDD and pre-commit verification.

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+T` | Run all tests (currently: open task panel - needs remap) |
| `Ctrl+Shift+T` | Run tests for current file |
| `Ctrl+R, Ctrl+R` | Re-run last test |
| `Ctrl+R, Ctrl+F` | Run failed tests only |

**Note:** `Ctrl+T` conflict - could use `F6` for tests instead, or move Task Panel to `Ctrl+Shift+K`.

### Test Results Panel

Appears as a toggleable bottom panel (like VS Code's test explorer):

```
┌─ Test Results ─────────────────────────────────────────────────────────┐
│ ✓ 142 passed  ✗ 2 failed  ○ 5 skipped                    [Re-run] [↻] │
├────────────────────────────────────────────────────────────────────────┤
│ ✗ src/api/__tests__/client.test.ts                                     │
│   ✗ should handle timeout errors                                       │
│     Expected: "timeout"                                                │
│     Received: "connection_error"                                       │
│     at client.test.ts:45                                              │
│   ✓ should retry on 5xx                                               │
│ ✓ src/api/__tests__/retry.test.ts (3 tests)                           │
└────────────────────────────────────────────────────────────────────────┘
```

### Test Detection

Extends existing `ProjectType` detection:

```json
{
  "projectTypes": [
    {
      "id": "dotnet",
      "name": ".NET",
      "detectFiles": ["*.csproj"],
      "testCommand": "dotnet test",
      "testFilePattern": "**/*Tests.cs",
      "testSingleFileCommand": "dotnet test --filter FullyQualifiedName~{testClass}"
    },
    {
      "id": "node",
      "name": "Node.js",
      "detectFiles": ["package.json"],
      "testCommand": "npm test",
      "testFilePattern": "**/*.test.{js,ts,jsx,tsx}",
      "testSingleFileCommand": "npm test -- {file}"
    }
  ]
}
```

### Integration with PR Review

- "Run Tests" button in review mode uses this system
- Test failures block approval (optional setting)
- Test output captured and shown in panel

---

## Feature 4: Repository Quick Access

### Concept

Fast switching between your frequently-used repositories, smarter than just "recent folders".

### Activation

- `Ctrl+Shift+O` - Open Repository Quick Switcher
- Command Palette: "Repos: Open..."

### Layout

```
┌─ Open Repository ─────────────────────────────────────────┐
│ 🔍 backend                                                │
├───────────────────────────────────────────────────────────┤
│ ★ acme/backend             P:\repos\backend      [Enter] │
│   acme/backend-v2          P:\repos\backend-v2           │
│   myteam/backend-shared    ~/repos/backend-shared        │
├───────────────────────────────────────────────────────────┤
│ Recent:                                                   │
│   acme/frontend            P:\repos\frontend     2h ago  │
│   acme/shared-lib          P:\repos\shared-lib   1d ago  │
├───────────────────────────────────────────────────────────┤
│ [Clone New...]                                            │
└───────────────────────────────────────────────────────────┘
```

### Features

| Feature | Description |
|---------|-------------|
| Fuzzy Search | Search by repo name, org, or path |
| Favorites | Star frequently-used repos (★) |
| Recent | Sorted by last access time |
| Clone | Clone a new repo via URL or `owner/repo` |
| Workspace | Optional: Group repos into workspaces |

### Data Sources

1. **Open folders** - Currently open tabs
2. **Recent folders** - From config
3. **Git repos on disk** - Scan configured directories for `.git` folders
4. **GitHub repos** - Fetch from `gh repo list` (cached)

### Configuration

```json
{
  "settings": {
    "repositories": {
      "scanPaths": ["P:\\repos", "~/projects"],
      "favorites": ["acme/backend", "acme/frontend"],
      "cloneDirectory": "P:\\repos"
    }
  }
}
```

---

## Feature 5: Markdown Preview Panel

### Concept

Live-updating markdown preview panel for PRD-driven development. Shows a markdown file that auto-reloads on change.

### Use Cases

1. **PRD tracking** - Keep PRD.md visible while implementing
2. **README editing** - Live preview while editing
3. **Documentation** - Reference docs while coding

### Activation

- Right-click .md file in File Explorer → "Pin as Preview"
- Command Palette: "Markdown: Pin Preview"
- `Ctrl+Shift+M` - Toggle markdown panel

### Layout Options

```
Option A: Right Panel (replaces shell temporarily)
┌─────────────────────────────────┬─────────────────────────────────┐
│         Claude Code             │       PRD.md (live preview)    │
│         Terminal                │                                 │
│                                 │   # Feature: Dashboard          │
│                                 │                                 │
│                                 │   ## Status: In Progress        │
│                                 │   - [x] Data fetching           │
│                                 │   - [ ] UI layout               │
│                                 │   - [ ] Actions                 │
└─────────────────────────────────┴─────────────────────────────────┘

Option B: Bottom Panel
┌─────────────────────────────────┬─────────────────────────────────┐
│         Claude Code             │           Shell                  │
├─────────────────────────────────┴─────────────────────────────────┤
│  PRD.md (preview)                                      [Unpin] [↗] │
│  # Feature: Dashboard                                              │
│  ...                                                               │
└───────────────────────────────────────────────────────────────────┘
```

### Features

| Feature | Description |
|---------|-------------|
| Auto-reload | File watcher triggers re-render on save |
| Scroll sync | Optional sync with editor |
| Checkboxes | Click to toggle `- [ ]` / `- [x]` |
| Links | Clickable links open in browser or file preview |
| Code blocks | Syntax highlighted |
| Pop-out | Detach to separate window |

### Rendering

Use existing infrastructure:
- File watcher from `FileExplorerService`
- Markdown parsing from file preview (extend for checkboxes)
- WPF `FlowDocument` or simple HTML in `WebView2`

---

## Implementation Priority

### Phase 1: Quick Wins (1-2 features)

1. **Test Runner Integration**
   - Low complexity, high daily value
   - Extends existing Project Runner infrastructure
   - Single keystroke to run tests

2. **Repository Quick Access**
   - Moderate complexity
   - Immediate productivity boost for multi-repo work
   - Foundation for Dashboard

### Phase 2: Core Value (Dashboard)

3. **GitHub Dashboard**
   - Higher complexity, transformative value
   - Requires `gh` CLI integration (already used)
   - Eliminates browser context switches

### Phase 3: Advanced (Review Mode)

4. **PR Review Mode**
   - Complex but completes the workflow
   - Builds on Dashboard + Git panels
   - Full review without leaving terminal

### Phase 4: Polish

5. **Markdown Preview Panel**
   - Nice-to-have for PRD workflows
   - Lower priority than core features

---

## Technical Considerations

### GitHub CLI Integration

All GitHub features depend on `gh` CLI:

```csharp
public class GitHubService
{
    // PRs awaiting your review
    public async Task<List<PullRequest>> GetReviewRequestsAsync()
    {
        var result = await RunGhAsync("pr list --search 'review-requested:@me' --json number,title,repository,author,createdAt");
        return JsonSerializer.Deserialize<List<PullRequest>>(result);
    }

    // Your authored PRs
    public async Task<List<PullRequest>> GetMyPullRequestsAsync()
    {
        var result = await RunGhAsync("pr list --author @me --state all --json number,title,state,repository");
        return JsonSerializer.Deserialize<List<PullRequest>>(result);
    }

    // Checkout a PR
    public async Task CheckoutPrAsync(string repo, int prNumber)
    {
        await RunGhAsync($"pr checkout {prNumber}", workingDirectory: GetRepoPath(repo));
    }

    // Review actions
    public async Task ApprovePrAsync(int prNumber, string? comment = null)
    {
        var body = comment != null ? $"-b \"{comment}\"" : "";
        await RunGhAsync($"pr review {prNumber} --approve {body}");
    }
}
```

### Caching Strategy

- Cache GitHub API results for 5 minutes (configurable)
- Show stale data immediately, refresh in background
- "Last updated" timestamp visible
- Manual refresh button

### Rate Limiting

- `gh` CLI handles authentication and rate limiting
- Batch requests where possible
- Don't poll more frequently than configured interval

---

## Configuration Summary

```json
{
  "settings": {
    "dashboard": {
      "enabled": true,
      "refreshIntervalMinutes": 5,
      "watchedOrgs": [],
      "showOnStartup": true
    },
    "repositories": {
      "scanPaths": ["P:\\repos"],
      "favorites": [],
      "cloneDirectory": "P:\\repos"
    },
    "testing": {
      "runOnSave": false,
      "showResultsPanel": true,
      "autoFocusOnFailure": true
    },
    "markdown": {
      "autoReload": true,
      "defaultPanelPosition": "right"
    }
  }
}
```

---

## Keyboard Shortcuts Summary

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+H` | Open Dashboard (Home) |
| `Ctrl+Shift+O` | Open Repository Switcher |
| `F6` | Run Tests |
| `Shift+F6` | Run Tests (current file) |
| `Ctrl+Shift+R` | Enter PR Review Mode |
| `Ctrl+Shift+M` | Toggle Markdown Panel |

---

## Success Metrics

1. **Reduced context switches** - Fewer browser tabs for GitHub
2. **Faster PR reviews** - Checkout → Review → Approve in one flow
3. **Pre-commit confidence** - Tests run consistently before commits
4. **Quick repo access** - < 3 seconds to switch between any repo

---

*Document Version: 1.0*
*Created: 2025-12-17*
