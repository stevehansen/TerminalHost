# Service Consolidation Report

This report compares Avalonia-specific service implementations with their Core counterparts to identify consolidation opportunities.

## Summary

| Merge Likelihood | Count | Services |
|------------------|-------|----------|
| Easy (95-100%) | 12 | AiAssistantService, ClaudeCommandService, DiffParserService, FileEditService, GitProcessRunner, JsonFileService, MarkdownService, ProfileRegistry, ProjectDetectionService, RunUrlDetectionService, TestRunnerService, GitHubService |
| Moderate (80-94%) | 2 | GitPrService, StatisticsService |
| Difficult (50-79%) | 1 | ConfigurationService |
| Major Rework (<50%) | 4 | GitWorktreeService, ShortcutConflictService, TimelineService, TranscriptParserService |

## Detailed Analysis

### Easy Merges (95-100% Similar)

These services are nearly identical and can be consolidated with minimal effort.

| Service | Core Lines | Avalonia Lines | Diff Lines | Similarity | Recommendation |
|---------|------------|----------------|------------|------------|----------------|
| ClaudeCommandService | 334 | 334 | 6 | 100% | Namespace diff only |
| MarkdownService | 441 | 440 | 6 | 100% | Namespace diff only |
| ProjectDetectionService | 491 | 490 | 8 | 100% | Namespace diff only |
| AiAssistantService | 216 | 216 | 6 | 99% | Namespace diff only |
| DiffParserService | 261 | 263 | 6 | 99% | Namespace diff only |
| RunUrlDetectionService | 191 | 192 | 7 | 99% | Namespace diff only |
| ~~TestRunnerService~~ | 462 | 462 | 12 | 99% | **KEEP SEPARATE** - Core uses `cmd.exe`, Avalonia uses `/bin/sh` |
| GitProcessRunner | 81 | 81 | 4 | 98% | Namespace diff only |
| ProfileRegistry | 105 | 105 | 6 | 98% | Namespace diff only |
| FileEditService | 143 | 144 | 11 | 97% | Minor differences |
| JsonFileService | 133 | 132 | 8 | 97% | Minor differences |
| ~~GitHubService~~ | 1168 | 1238 | 100 | 96% | **KEEP SEPARATE** - Core uses `powershell.exe`, Avalonia uses `/bin/sh` |

**Action:** Switch Avalonia DI to use Core implementations directly (except GitHubService - platform-specific).

### Moderate Merges (80-94% Similar)

These have some differences but should merge successfully with review.

| Service | Core Lines | Avalonia Lines | Diff Lines | Similarity | Notes |
|---------|------------|----------------|------------|------------|-------|
| GitPrService | 224 | 268 | 48 | 91% | Avalonia has extra methods |
| StatisticsService | 201 | 200 | 43 | 90% | Minor implementation differences |

**Action:** Review differences, merge extra functionality into Core, then consolidate.

### Difficult Merges (50-79% Similar)

These require significant review and potential refactoring.

| Service | Core Lines | Avalonia Lines | Diff Lines | Similarity | Notes |
|---------|------------|----------------|------------|------------|-------|
| ConfigurationService | 204 | 406 | 283 | 54% | Avalonia version is 2x larger |

**Action:** Analyze why Avalonia version is larger. May have platform-specific configuration handling.

### Major Rework Required (<50% Similar)

These have significantly diverged and require careful analysis.

| Service | Core Lines | Avalonia Lines | Diff Lines | Similarity | Notes |
|---------|------------|----------------|------------|------------|-------|
| TranscriptParserService | 325 | 267 | 376 | 37% | Different parsing approaches |
| TimelineService | 1313 | 555 | 1214 | 36% | Core is 2.4x larger |
| GitWorktreeService | 271 | 361 | 424 | 33% | Different implementations |
| ShortcutConflictService | 255 | 244 | 371 | 26% | Significantly different |

**Action:** These need detailed analysis to determine which version is more complete/correct, or if they serve different purposes.

## Recommended Consolidation Order

### Phase 1: Quick Wins (Easy Merges) - COMPLETED

Consolidate these immediately - just change DI registration:

| # | Service | Status |
|---|---------|--------|
| 1 | ClaudeCommandService | Done |
| 2 | MarkdownService | Done |
| 3 | ProjectDetectionService | Done |
| 4 | AiAssistantService | Done |
| 5 | DiffParserService | Done |
| 6 | RunUrlDetectionService | Done |
| 7 | ~~TestRunnerService~~ | **SKIPPED** | Platform-specific (Core=cmd.exe, Avalonia=sh) |
| 8 | GitProcessRunner | Done |
| 9 | ProfileRegistry | Done |
| 10 | FileEditService | Done |
| 11 | JsonFileService | Done |
| 12 | ~~GitHubService~~ | **SKIPPED** | Platform-specific (Core=PowerShell, Avalonia=sh) |

**Phase 1 completed:** 10 Avalonia service files deleted, DI updated to use Core implementations. GitHubService and TestRunnerService kept separate (platform-specific).

### Phase 2: Review and Merge - COMPLETED

| # | Service | Status | Notes |
|---|---------|--------|-------|
| 1 | GitPrService | Done | Removed unused `AutoDetectPrInfoAsync` (dead code) |
| 2 | StatisticsService | Done | Core version compatible (uses aliased property) |

**Phase 2 completed:** 2 Avalonia service files deleted, missing method merged to Core.

### Phase 3: Analysis Required
1. ConfigurationService - understand size difference
2. TranscriptParserService - compare parsing approaches
3. TimelineService - determine which is authoritative
4. GitWorktreeService - compare implementations
5. ShortcutConflictService - compare implementations

## Estimated Impact

- **Lines of code removed:** ~4,000 (Phase 1 & 2 completed)
- **Files deleted:** 15 service files (12 Phase 1 + GitStatusService + 2 Phase 2)
- **Remaining:** 5 service files to consolidate (Phase 3)
- **Risk:** Low for Phase 1 (done), Medium for Phase 2 (done), High for Phase 3

---
*Generated: 2026-01-05*
*Phase 1 completed: 2026-01-05*
*Phase 2 completed: 2026-01-05*
