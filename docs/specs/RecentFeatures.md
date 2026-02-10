# PRD: Recent Features / What's New

Track and surface recently added features so users can discover new capabilities without reading changelogs.

## Problem

TerminalHost ships features frequently. With 60+ command palette entries, new additions are invisible unless the user happens to notice them. There's no "What's New" experience — users must read CLAUDE.md diffs or changelogs to learn what changed.

## Goals

1. **Feature discovery**: Surface recently added features grouped by date (newest first)
2. **Invocable**: Features can be launched directly from the What's New page
3. **Trackable**: Each feature carries an "introduced on" date, maintained as part of development workflow
4. **Read tracking**: Optionally remember what the user has already seen

## Non-Goals

- Release notes or version-based changelog (no versioning system yet)
- Feature announcements or popups on startup
- Tracking per-feature usage analytics

---

## Design

### 0. Empty State: Replace the Sample Terminal

Currently, when no tabs are open (or the last tab is closed):
- **Windows WPF**: Shows a live `cmd.exe` test terminal with a "+ Open Project" button
- **macOS Avalonia**: Shows a static "Welcome to TerminalHost" message

Both are low-value. The What's New page is a much better use of this space — it gives users something useful to see on launch and when between projects.

**Behavior:**
- When `SelectedTab == null`, show the What's New content **embedded directly** in the empty state area (not as a center panel — there's no tab to host a panel in)
- The same `RecentFeaturesViewModel` powers both the empty state and the center panel, but the empty state view includes an additional header with the "+ Open Project" button (preserving the existing call-to-action)
- When a tab is opened/selected, the empty state disappears as normal

**Empty state layout:**
```
┌──────────────────────────────────────────────────┐
│                                                  │
│          Welcome to TerminalHost                 │
│          [+ Open Project]                        │
│                                                  │
│  ─────────── What's New ───────────────────────  │
│                                                  │
│  ── 2025-W52 (Dec 23 - Dec 29) ──── NEW ──────  │
│                                                  │
│  🔀  Git Panel Enhancements              [Open]  │
│      File diffs, branch changes, pop-out         │
│                                                  │
│  📋  Command Palette: All Actions        [Open]  │
│      All invocable actions now in palette        │
│                                                  │
│  ── 2025-W51 (Dec 16 - Dec 22) ────────────────  │
│  ...                                             │
│                                                  │
└──────────────────────────────────────────────────┘
```

**Key details:**
- The empty state uses a **read-only view** of What's New — [Open] buttons that require a tab (terminal commands, git features) are hidden or show a tooltip "Open a project first"
- Features that don't require a tab (e.g., Settings, Profiles, Help) remain invocable
- `CanExecute` on `PaletteCommand` already handles this — commands requiring a tab check `SelectedTab != null`
- The "New since last visit" tracking still applies — viewing the empty state counts as viewing the page
- Scrollable if there are many features

**Replaces:**
- Windows: The `EasyTerminalControl` with `cmd.exe` in `MainWindow.xaml` (lines 61-86)
- macOS: The "Welcome to TerminalHost" `StackPanel` in `MainWindow.axaml` (lines 59-93)

### 1. Feature Metadata: `IntroducedOn` Date

Each `PaletteCommand` gains an optional `IntroducedOn` date property.

```csharp
// PaletteCommand.cs (TerminalHost.Core)
public class PaletteCommand
{
    // ... existing properties ...

    /// <summary>
    /// Date this feature was introduced or last significantly updated.
    /// Used by the "What's New" / Recent Features page.
    /// Null for features that predate the tracking system.
    /// </summary>
    public DateOnly? IntroducedOn { get; init; }
}
```

**Key decisions:**
- `DateOnly` (not DateTime) — we only care about the calendar date
- `null` means "predates tracking" — these features are excluded from Recent Features but could appear in an "All Features" section
- The date represents when the feature was first added, or updated to reflect a major change (e.g., adding hunk staging to Git Changes)

### 2. Dating Existing Features

Existing features need dates assigned retroactively. Strategy:

1. **Git blame/log**: Use `git log --diff-filter=A -- <file>` on `MainViewModel.cs` `InitializeCommandPalette()` to find when each command ID was first added
2. **Batch by commit date**: Features added in the same commit share the same date
3. **Approximate is fine**: The goal is relative ordering, not precision. A week-level accuracy is sufficient for features added months ago
4. **Undated fallback**: Features that can't be reliably dated remain `null` and appear in an "Earlier" section

**Implementation approach:**
- A one-time task runs `git log` analysis on `MainViewModel.cs` to build a mapping of command IDs to approximate introduction dates
- Results are applied as `IntroducedOn` values in `InitializeCommandPalette()`

### 3. Claude Commands / Dynamic Entries

Claude commands (global, project, plugin) and profile launch commands are **dynamic** — they come and go based on the filesystem. These are handled differently:

| Source | Tracking | Rationale |
|--------|----------|-----------|
| Built-in palette commands | `IntroducedOn` on `PaletteCommand` | Static, version-controlled |
| Claude commands (global/project/plugin) | Excluded from Recent Features | User-created, not "app features" |
| Profile launch commands | Excluded from Recent Features | User-created |

Claude commands and profiles appear in the command palette but are **not** shown in the Recent Features page. However, the Recent Features page can reference Claude command support as a feature itself (e.g., "Claude plugin commands support — 2025-06-15").

### 4. The Recent Features Page

A new center panel accessible via command palette and keyboard shortcut.

**Panel type:** Center panel (replaces terminal content, like Git GUI or PR Review)

**Layout:**
```
┌──────────────────────────────────────────────────┐
│  ✨ What's New                        [All] [×]  │
├──────────────────────────────────────────────────┤
│                                                  │
│  ── 2025-W52 (Dec 23 - Dec 29) ──── NEW ──────  │
│                                                  │
│  🔀  Git Panel Enhancements              [Open]  │
│      File diffs, branch changes, pop-out         │
│                                                  │
│  📋  Command Palette: All Actions        [Open]  │
│      All invocable actions now in palette        │
│                                                  │
│  ── 2025-W51 (Dec 16 - Dec 22) ────────────────  │
│                                                  │
│  🔊  Activity Alert Sound               [Open]  │
│      Sound when terminal waiting for input       │
│                                                  │
│  ◧  Panel-Based Layout                  [Open]  │
│      Center panels replace popups                │
│                                                  │
│  ── 2025-W50 (Dec 9 - Dec 15) ─────────────────  │
│  ...                                             │
│                                                  │
│  ── Earlier ────────────────────────────────────  │
│  (Features without dates or before tracking)     │
│                                                  │
└──────────────────────────────────────────────────┘
```

**Grouping:**
- Features grouped by ISO week (year-week format): `2025-W52 (Dec 23 - Dec 29)`
- Weeks shown newest first
- An "Earlier" section at the bottom for undated features (collapsed by default)
- Optional "All" toggle to show every feature (not just recent)

**Feature entries show:**
- Icon from `PaletteCommand.Icon`
- Name from `PaletteCommand.Name`
- Description from `PaletteCommand.Description`
- **[Open]** button that calls `PaletteCommand.Execute` to invoke the feature directly
- Grayed out [Open] if `CanExecute` returns false (e.g., no tab open for terminal commands)

### 5. "New Since Last Visit" Tracking

The page tracks the last-viewed date to highlight features the user hasn't seen.

```csharp
// AppSettings (TerminalHost.Core)
public class AppSettings
{
    // ... existing properties ...

    /// <summary>
    /// The last date the user opened the Recent Features page.
    /// Features introduced after this date are marked as "NEW".
    /// </summary>
    public DateOnly? RecentFeaturesLastViewedDate { get; set; }
}
```

**Behavior:**
- When the page opens, features with `IntroducedOn > RecentFeaturesLastViewedDate` show a **NEW** badge
- After 3 seconds on the page (debounced), `RecentFeaturesLastViewedDate` is updated to today
- If the user has never opened the page, all dated features show as NEW
- The "NEW" badge is a small pill/tag next to the week header or individual feature

**Optional toolbar indicator:**
- A subtle dot/badge on the What's New toolbar button or command palette entry when unseen features exist
- Requires comparing latest `IntroducedOn` across all commands against `RecentFeaturesLastViewedDate`

### 6. Keyboard Shortcut & Access

| Access Method | Details |
|---------------|---------|
| Keyboard shortcut | `Ctrl+F1` (What's New — adjacent to F1 Help) |
| Command palette | "What's New" / "Recent Features" |
| Help view | Link at top: "See what's new" |
| Toolbar | Optional button (can be added to touch mode toolbar) |

### 7. ViewModel

```csharp
// RecentFeaturesViewModel.cs
public partial class RecentFeaturesViewModel : BasePanelViewModel
{
    // Data
    public ObservableCollection<FeatureWeekGroup> WeekGroups { get; }
    public bool ShowAllFeatures { get; set; }  // Toggle between recent-only and all
    public bool HasUnseenFeatures { get; }     // For toolbar badge

    // Commands
    [RelayCommand] void ExecuteFeature(PaletteCommand command);
    [RelayCommand] void ToggleShowAll();
    [RelayCommand] void Close();

    // Lifecycle
    void OnOpened();   // Load features, mark as viewed after delay
    void OnClosed();   // Save last-viewed date
}

public class FeatureWeekGroup
{
    public string WeekLabel { get; }       // "2025-W52 (Dec 23 - Dec 29)"
    public DateOnly WeekStart { get; }
    public bool HasNewFeatures { get; }    // Any feature in this week is NEW
    public List<FeatureEntry> Features { get; }
}

public class FeatureEntry
{
    public PaletteCommand Command { get; }
    public bool IsNew { get; }             // Introduced after last-viewed date
}
```

### 8. Feature Grouping Logic

```
1. Collect all PaletteCommands where IntroducedOn != null
2. Group by ISO week number: ISOWeek.GetYear() + ISOWeek.GetWeekOfYear()
3. Sort groups descending (newest first)
4. Within each group, sort features alphabetically by name
5. If ShowAllFeatures, append an "Earlier" group with IntroducedOn == null features
6. Compare each feature's IntroducedOn against RecentFeaturesLastViewedDate for NEW badge
```

---

## Implementation Plan

### Phase 1: Core Infrastructure

1. **Add `IntroducedOn` to `PaletteCommand`** — single property addition in Core
2. **Add `RecentFeaturesLastViewedDate` to `AppSettings`** — persisted setting
3. **Date existing features** — git log analysis, apply dates in `InitializeCommandPalette()`
4. **Create `RecentFeaturesViewModel`** — extends `BasePanelViewModel`
5. **Create WPF view** — `RecentFeaturesView.xaml` as reusable UserControl for the feature list
6. **Replace empty state** — swap the test terminal in `MainWindow.xaml` with a welcome header + embedded `RecentFeaturesView`
7. **Wire up center panel** — register as center panel for `Ctrl+F1` access when a tab is open
8. **Register in command palette** — "What's New" entry with `Ctrl+F1`
9. **Update documentation** — CLAUDE.md, SHORTCUTS.md, ShortcutConflictService

### Phase 2: Polish

10. **"NEW" badge logic** — track last-viewed, highlight unseen features
11. **Toolbar indicator** — subtle badge when unseen features exist
12. **Help view link** — "See what's new" in F1 help

### Phase 3: Cross-Platform

13. **Avalonia empty state** — replace welcome message in `MainWindow.axaml` with What's New
14. **Avalonia center panel** — `RecentFeaturesView.axaml` for macOS
15. **Shared ViewModel** — ensure ViewModel works with both platforms

---

## CLAUDE.md Workflow Integration

When implementing new features, developers (and Claude Code) should:

1. **When adding a new `PaletteCommand`**: Always set `IntroducedOn = new DateOnly(YYYY, MM, DD)` using today's date
2. **When significantly updating a feature**: Update the `IntroducedOn` date to reflect the major change
3. **When adding a non-palette feature**: If the feature deserves discovery (e.g., a new keyboard shortcut without a palette entry), consider adding a palette entry for it

**Add to CLAUDE.md "Important: Documentation Maintenance" section:**

```markdown
- **When adding new command palette entries:**
  - Set `IntroducedOn = new DateOnly(YYYY, MM, DD)` with today's date
  - This ensures the feature appears in the "What's New" page
```

---

## Edge Cases

| Scenario | Handling |
|----------|----------|
| Feature removed | It disappears from Recent Features naturally (no PaletteCommand = not shown) |
| Feature renamed | Same `Id` → same entry, just new name. Date unchanged unless major rework |
| Multiple features same day | All grouped under same week, shown alphabetically |
| User never opens page | All features marked NEW on first open, then caught up |
| Features with CanExecute=false | Shown but with disabled [Open] button and explanatory tooltip |
| Dynamic name/description | Use `NameProvider`/`DescriptionProvider` at render time, same as palette |
| Empty state with no features dated yet | Show welcome header + "+ Open Project" button + "No features tracked yet" message |
| [Open] clicked in empty state (no tab) | Commands requiring a tab are disabled via `CanExecute`; tab-free commands (Settings, Help) work normally |
| Empty state viewed briefly | Still counts as viewed — `RecentFeaturesLastViewedDate` updates after 3s debounce |

---

## Open Questions

1. ~~**Should the page auto-open on first launch after new features are added?**~~ Resolved — the empty state (no tabs open) now shows What's New automatically. Users see it on launch and between projects without any intrusive popups.
2. **Should we show feature count in the What's New badge?** e.g., "3 new" vs just a dot. Leaning toward just a dot for simplicity.
3. **Should "Earlier" features be searchable/filterable?** Could add a search box for finding features across all time periods.

---

*Document Version: 1.0*
*Last Updated: 2025-02-10*
