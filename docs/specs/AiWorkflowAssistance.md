# PRD: AI Workflow Assistance

TerminalHost integrates the configured AI assistant (Claude, Gemini, etc.) directly into developer workflow panels — commit messages, merge conflicts, test failures, PR review, diffs, search, and changelogs.

## Implementation Status

| Feature | Location | Status |
|---------|----------|--------|
| AI Commit Message Generation | Git Changes panel (Alt+G) | **Implemented** |
| Merge Conflict Auto-Resolution | Merge Conflict viewer | **Planned** |
| Test Failure Root Cause Analysis | Test Results panel (F6) | **Planned** |
| PR Code Review Assistance | PR Review Mode (Ctrl+Shift+R) | **Planned** |
| Diff/Change Explanation | Git Changes panel (Alt+G) | **Implemented** |
| Regex Generation Assistance | Search Across Files (Ctrl+F3) | **Planned** |
| Changelog Generation | Commit History (Ctrl+H) | **Planned** |

---

## Problem Statement

Developers using TerminalHost already have Claude Code running in the terminal. But many in-app workflow panels require manual cognitive effort for tasks AI could handle instantly:

1. **Merge conflicts** — deciding which side to keep or how to blend both requires reading context across files
2. **Test failures** — a failing test output tells you *what* broke but not *why* or *how to fix it*
3. **PR review** — spotting bugs, security issues, and style violations takes time and focus
4. **Diffs** — reviewing someone else's staged changes before committing requires reconstructing intent
5. **Regex search** — writing correct regex for complex patterns is friction that slows search
6. **Changelogs** — summarising 30 commits into structured release notes is tedious busywork

The AI is already configured and available via `IProcessService`. These features extend the existing commit message pattern to the other panels where AI adds the most value.

---

## Goals

1. **Zero-configuration** — use the same AI assistant already configured in Settings
2. **Non-blocking** — AI suggestions are async with progress toasts; panels remain usable
3. **AI-first with fallback** — gracefully degrade when AI is unavailable or times out
4. **Consistent UX** — all AI actions follow the same button/toast/result pattern
5. **Respect context limits** — truncate large inputs (diffs, logs) with clear indication

---

## Established Pattern

All features follow the pattern from `GitFilesViewModel.GenerateWithAiAsync()`:

```csharp
// 1. Resolve the configured AI executable
var claudePath = ResolveAiExecutable(); // from config.Settings.CustomCommand
if (claudePath == null) return null;    // fall through to heuristic/manual

// 2. Build a focused prompt and pipe it via stdin
var prompt = $"{systemPrompt}\n\n{context}";
var result = await _processService.RunAsync(
    claudePath,
    "--no-session-persistence -p",
    workingDirectory,
    stdin: prompt,
    timeout: TimeSpan.FromSeconds(30)
);

// 3. Clean and return
return result?.Trim().Trim('`');
```

**Note:** `ResolveAiExecutable()` checks that the configured custom command filename contains a known AI identifier (e.g. "claude", "gemini"). Features degrade gracefully when the assistant is unavailable.

---

## Feature 1: Merge Conflict Auto-Resolution

### Concept

When a merge conflict is open in the Merge Conflict viewer, an **"AI Suggest"** button sends both sides plus the common ancestor to the AI and populates the Result panel with a suggested resolution.

### UI Change

Add to `MergeConflictViewModel` and `MergeConflictView.xaml`:

```
┌─ Merge Conflict: src/services/AuthService.cs ──────────────────────────┐
│  [← Prev]  Conflict 2 of 5  [Next →]          [AI Suggest ✨]  [Save] │
├──────────────────┬──────────────────┬──────────────────────────────────┤
│ Ours (main)      │ Theirs (feature) │ Result                           │
│                  │                  │                                  │
│  if (timeout     │  if (timeout     │  if (timeout > 0 &&              │
│    > 0)          │    > 0 &&        │    retries > 0)                  │
│    Retry();      │    retries > 0)  │    Retry(timeout);               │
│                  │    Retry(timeout)│                                  │
│ [Accept Ours]    │ [Accept Theirs]  │ ← AI populated this              │
└──────────────────┴──────────────────┴──────────────────────────────────┘
```

### Prompt Design

```
You are resolving a git merge conflict. Produce ONLY the resolved code — no explanation, no markdown fences.

File: {filePath}
Language: {detectedLanguage}

<<<<<<< OURS ({ourBranch})
{oursContent}
=======
{theirsContent}
>>>>>>> THEIRS ({theirBranch})

Context (surrounding lines):
{contextLines}

Output the resolved version of the conflicted section only.
```

### Implementation

- **ViewModel**: `MergeConflictViewModel.SuggestResolutionAsync()` — sets `ResultText`, marks result as AI-generated
- **UI indicator**: Result panel header shows "AI Suggestion — review before saving" until manually edited
- **Fallback**: Button disabled with tooltip "AI assistant not configured" when unavailable
- **Input limit**: Send at most 200 lines of conflict + 40 lines of surrounding context

---

## Feature 2: Test Failure Root Cause Analysis

### Concept

When tests fail in the Test Results panel, an **"Analyze Failures ✨"** button sends the failure output plus the failing test source to the AI, which returns a concise diagnosis and suggested fix shown inline.

### UI Change

Add to `TestResultsViewModel` and `TestResultsView.xaml`:

```
┌─ Test Results ──────────────────────────────────────────────────────────┐
│ ✗ 2 failed  ✓ 140 passed                [Analyze Failures ✨]  [Re-run] │
├─────────────────────────────────────────────────────────────────────────┤
│ ✗ AuthServiceTests.ShouldRefreshOnExpiry                                │
│   Expected: true   Received: false   at AuthService.cs:142              │
│                                                                         │
│   💡 AI: The token expiry check uses `>=` but the refresh threshold     │
│   constant was changed from 60s to 300s in commit a3f9d2. The test     │
│   expects the old threshold. Update REFRESH_THRESHOLD_SECONDS to 60    │
│   or adjust the test setup to use a 300s-old token.                    │
│                                                                         │
│ ✗ AuthServiceTests.ShouldRejectExpiredToken                             │
│   ...                                                                   │
└─────────────────────────────────────────────────────────────────────────┘
```

### Prompt Design

```
You are diagnosing test failures. Be concise — 2-4 sentences max per failure.
Format: plain text, no markdown, no code fences.

Project: {projectName} ({projectType})
Working directory: {workingDir}

FAILING TESTS:
{failureOutput}

TEST SOURCE (if available):
{testFileContents}
```

### Implementation

- **ViewModel**: `TestResultsViewModel.AnalyzeFailuresAsync()` — attaches `AiDiagnosis` string to each failed `TestResult`
- **UI**: Diagnosis shown as a `💡 AI:` paragraph below the failure details, collapsed by default, expanded on click
- **Source loading**: Attempt to read test files referenced in stack traces via `IFileSystem`
- **Input limit**: Up to 10 failures, 100 lines of output each; test source truncated at 300 lines

---

## Feature 3: PR Code Review Assistance

### Concept

In PR Review Mode, an **"AI Review ✨"** button sends the entire PR diff to the AI, which returns a structured list of findings (bugs, security issues, suggestions) displayed as a collapsible panel above the file list.

### UI Change

Add to `PrReviewViewModel` and `PrReviewView.xaml`:

```
┌─ PR #423: Add retry logic to API client ────────────────────────────────┐
│  [AI Review ✨]  [Approve]  [Request Changes]  [Comment]                │
├─────────────────────────────────────────────────────────────────────────┤
│ ▼ AI Review                                                             │
│   🔴 Bug        RetryPolicy.cs:47 — maxDelay is never applied; the      │
│                 loop uses `delay` which resets to baseDelay each iter.  │
│   🟡 Suggestion client.ts:89 — Consider extracting the retry condition  │
│                 into a named predicate for readability.                  │
│   🟢 Looks good: Error handling and test coverage are solid.            │
│ ──────────────────────────────────────────────────────────────────────  │
│ Changed Files                                                           │
│   M src/api/client.ts                                          +89 -12  │
│   ...                                                                   │
└─────────────────────────────────────────────────────────────────────────┘
```

### Prompt Design

```
You are reviewing a pull request. List findings as:
🔴 Bug | 🟡 Suggestion | 🟢 Looks good

Format each finding as: {emoji} {category}  {file}:{line} — {description}
End with one 🟢 summary line. No other text.

PR: {prTitle}
Author: {prAuthor}

{prDiff}
```

### Implementation

- **ViewModel**: `PrReviewViewModel.RunAiReviewAsync()` — populates `AiReviewFindings` observable collection
- **Parsing**: Split output lines by emoji prefix into typed `AiReviewFinding` records
- **Input limit**: PR diff truncated at 30KB with a note in the prompt; large PRs show a warning
- **Persistence**: Findings cached in memory for the session; cleared on PR refresh

---

## Feature 4: Diff/Change Explanation

### Concept

In the Git Changes panel, an **"Explain ✨"** button next to the diff header sends the current staged diff to AI and shows a one-paragraph plain-English explanation of what the changes do — useful when reviewing someone else's staged work before committing.

### UI Change

Add to `GitFilesViewModel` and `GitFilesView.xaml`:

```
┌─ Git Changes ────────────────────────────────────────────────────────────┐
│  Staged (4 files)           [Generate Message ✨]  [Explain ✨]  [Commit]│
├──────────────────────────────────────────────────────────────────────────┤
│  💡 These changes add exponential backoff to the HTTP retry logic.       │
│     A new RetryPolicy class centralises retry configuration. The         │
│     client.ts update wires it up. Tests cover the new retry paths.       │
├──────────────────────────────────────────────────────────────────────────┤
│  M  src/api/client.ts                                                    │
│  A  src/api/RetryPolicy.ts                                               │
│  ...                                                                     │
└──────────────────────────────────────────────────────────────────────────┘
```

### Prompt Design

```
Explain the following git diff in 2-4 sentences of plain English.
Describe what changed and why (infer from context). No bullet points, no markdown.

{stagedDiff}
```

### Implementation ✅

- **ViewModel**: `GitFilesViewModel.ExplainChangesAsync()` — sets `DiffExplanation` string property; `ExplainFileDiffAsync()` — sets `FileDiffExplanation` for the currently selected file (staged or unstaged)
- **Shared helper**: `RunAiAsync(workingDirectory, systemPrompt, diffContent?)` — used by both commit message generation and both explain variants
- **UI (staged all-files)**: `💡` button in the commit form actions row; explanation callout with blue left-border accent appears above the conventional commit prefix chips; dismissed via ✕ button
- **UI (per-file)**: `💡 Explain` button in the diff panel header; explanation callout appears between the diff header and the diff content; auto-dismissed when a different file is selected
- **Trigger**: Separate from "Generate Message" — both can be run independently
- **Input limit**: Same 20KB truncation as commit message generation

---

## Feature 5: Regex Generation Assistance

### Concept

In the Search Across Files panel, a **"Generate regex ✨"** button opens a small input where the user describes the pattern in plain English. The AI returns a regex which is inserted into the search box.

### UI Change

Add to `SearchAcrossFilesViewModel` and `SearchAcrossFilesView.xaml`:

```
┌─ Search Across Files ────────────────────────────────────────────────────┐
│ 🔍 [(?:TODO|FIXME):\s+\w+                              ] [✨] [Aa] [.*] │
│    ✨ Describe pattern: [find TODO or FIXME comments with text     ] [→] │
├──────────────────────────────────────────────────────────────────────────┤
│  Results: 14 matches in 8 files                                          │
│  ...                                                                     │
└──────────────────────────────────────────────────────────────────────────┘
```

### Prompt Design

```
Generate a single regex pattern for the following description.
Output ONLY the regex — no explanation, no slashes, no flags.

Description: {userDescription}
```

### Implementation

- **ViewModel**: `SearchAcrossFilesViewModel.GenerateRegexAsync(description)` — inserts result into `SearchQuery`, enables regex mode
- **UI**: Small inline input row toggled by the ✨ button; hidden after generation or on Escape
- **Validation**: Test the generated regex for syntax errors before inserting; show error toast if invalid
- **Fallback**: Input row still usable as a manual search description field if AI unavailable

---

## Feature 6: Changelog Generation

### Concept

In the Commit History viewer, a **"Generate Changelog ✨"** button takes the currently filtered commits and produces a structured changelog grouped by type (feat, fix, breaking, etc.). Output is shown in a scrollable panel and can be copied or saved.

### UI Change

Add to `CommitHistoryViewModel` and `CommitHistoryView.xaml`:

```
┌─ Commit History ─────────────────────────────────────────────────────────┐
│  Filter: [v1.2.0..HEAD           ]  [Generate Changelog ✨]  [Refresh]   │
├───────────────────────────────────────┬──────────────────────────────────┤
│  Commits (23)                         │ 📋 Changelog                     │
│  abc1234  feat: add retry logic       │                                  │
│  def5678  fix: null ref on logout     │ ## v1.3.0 — 2026-02-22           │
│  789abcd  refactor: extract policy    │                                  │
│  ...                                  │ ### Features                     │
│                                       │ - Add retry logic with backoff   │
│                                       │ - Dark mode support              │
│                                       │                                  │
│                                       │ ### Bug Fixes                    │
│                                       │ - Fix null ref on logout         │
│                                       │ - Resolve race in file watcher   │
│                                       │                                  │
│                                       │ [Copy]  [Save as CHANGELOG.md]   │
└───────────────────────────────────────┴──────────────────────────────────┘
```

### Prompt Design

```
Generate a changelog from these git commits. Use Keep a Changelog format.
Group into: Features, Bug Fixes, Breaking Changes, Other.
Omit chore/style/refactor commits unless significant.
Output markdown only.

Version: {tagOrRange}
Date: {today}

Commits:
{commitList}
```

### Implementation

- **ViewModel**: `CommitHistoryViewModel.GenerateChangelogAsync()` — populates `ChangelogText` string, shown in right panel
- **Commit input**: Use current filter's commit list (titles + hashes only, no diffs) — safe for large ranges
- **Save action**: Write to `CHANGELOG.md` in repo root via `IFileSystem`; prompt if file exists
- **Input limit**: Up to 200 commit messages; if more, show warning and truncate

---

## Implementation Priority

| Priority | Feature | Effort | Notes |
|----------|---------|--------|-------|
| ~~**High**~~ | ~~Diff/Change Explanation~~ | ~~Low~~ | ✅ Implemented — staged all-files + per-file in diff panel |
| **High** | Merge Conflict Auto-Resolution | Medium | Highest pain point; significant time savings |
| **High** | Test Failure Root Cause Analysis | Medium | Tight feedback loop; high daily use |
| **Medium** | PR Code Review Assistance | Medium | Adds to existing PR Review Mode |
| **Medium** | Regex Generation | Low | Small surface area, high friction reduction |
| **Low** | Changelog Generation | Low | Useful for releases, lower daily frequency |

---

## Technical Considerations

### AI Availability

All features check `ResolveAiExecutable()` before invoking. When AI is unavailable:
- Buttons show a tooltip: "Requires a configured AI assistant (Settings → General)"
- No buttons are hidden — users can see the feature exists and configure it

### Input Truncation

| Feature | Limit | Strategy |
|---------|-------|----------|
| Merge conflict resolution | 200 conflict lines + 40 context | Hard truncate |
| Test failure analysis | 10 failures × 100 lines | Select top N failures |
| PR review | 30KB diff | Truncate with `[...truncated]` note in prompt |
| Diff explanation | 20KB diff | Same as commit message generation |
| Regex generation | N/A (user description only) | — |
| Changelog generation | 200 commit messages | Filter to most recent |

### Timeout

All AI calls use a 30-second timeout (same as commit message generation). On timeout:
- Show error toast: "AI timed out — try again or use manual mode"
- Panel reverts to pre-AI state

### Multi-AI Support

Features use `IAiAssistantService` to resolve the active AI for the current project, consistent with per-project AI selection (see `MultiAiAssistants.md`). The prompt is AI-agnostic; any assistant that supports stdin via `-p` or equivalent works.

---

## Configuration

No new configuration required. All features use:
- `config.Settings.CustomCommand` — the AI executable path
- Per-project `activeAiAssistantId` — the active assistant per directory

Optional future addition:
```json
{
  "settings": {
    "aiWorkflow": {
      "enabledFeatures": ["commitMessage", "diffExplanation", "mergeConflict"],
      "timeoutSeconds": 30,
      "maxDiffKb": 20
    }
  }
}
```

---

*Document Version: 1.0*
*Created: 2026-02-22*
