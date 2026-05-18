# Feature Parity: WPF Host ↔ Avalonia Host

Tracks what each host currently does, where the implementations differ, and what
is missing on either side. Not an aspirational roadmap — only things that exist
in code today.

Last verified: 2026-05-18 against branch `avalonia-windows-host`.

## Status legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Full implementation, on par with the other host |
| 🟡 | Present but reduced (smaller surface, fewer features, or no UI hookup) |
| ⬜ | Scaffolded/stubbed only (file or service exists but does nothing meaningful) |
| ❌ | Not present |
| ❓ | Needs verification — listed in code but behaviour not confirmed |

Most platform-agnostic services live in `TerminalHost.Core` and are consumed by
both hosts. Where a row shows ✅ on both, it usually means both hosts wire the
same Core implementation via DI. Rows where the implementation differs
materially (different concrete class, different surface) are called out in the
Notes column.

---

## 1. Service inventory

Services are grouped by area. The "WPF" and "Avalonia" columns mean "wired up
and used by that host's `App.*.cs` / `MainViewModel`."

### 1.1 Terminal, process, platform

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `ITerminalControlFactory` | ✅ | 🟡 | WPF uses `EasyTerminalControl` (ConPTY). Avalonia hosts it via `NativeControlHost` on Windows — currently renders black, root cause not yet identified. |
| `ISessionManager` | ✅ | ✅ | Core implementation shared. |
| `IProfileRegistry` | ✅ | ✅ | Core. |
| `ITerminalProfilesBuilder` | ✅ | ✅ | Core. |
| `IProcessService` | ✅ | ✅ | Core. |
| `ICommandComposer` | ✅ | ✅ | Core port; per-OS shell quirks. |
| `IFileSystem` | ✅ | ✅ | Core. |
| `IDispatcherService` | ✅ | ✅ | Per-host adapter over UI dispatcher. |
| `ITimerService` (host) | ✅ | ✅ | WPF: DispatcherTimer wrapper. Avalonia: Avalonia DispatcherTimer wrapper. |
| `Core.ITimerService` | ✅ | ✅ | Headless timer for Core services. |
| `IScreenService` | ✅ | ✅ | Per-host implementation. |
| `IClipboardService` | ✅ | ✅ | Per-host adapter. |
| `IFilePickerService` | ✅ | ✅ | Per-host adapter. Note: declared but unused in Avalonia `MainViewModel` (see §5). |
| `IFolderPickerService` | ✅ | ✅ | Per-host adapter. |
| `ISingleInstanceService` | ✅ | 🟡 | WPF: mutex + named pipe. Avalonia: Unix domain socket on macOS; Windows path uses the shared mutex shim — needs smoke test. |

### 1.2 Configuration, workspace, persistence

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IConfigurationService` | ✅ | ✅ | Core. Both hosts share `%APPDATA%\TerminalHost` on Windows after recent fix (commit 95d80eb). |
| `IDirectorySettingsStore` | ✅ | ✅ | Core. |
| `IWorkspaceService` | ✅ | ✅ | Core. |
| `IWorkspaceStateStore` | ✅ | ✅ | Core. |
| `ISessionStateStore` | ✅ | ✅ | Core. |
| `ITabFactory` | ✅ | ✅ | Per-host adapter (creates host-specific tab VMs). |
| `ITabRestoreCoordinator` | ✅ | ✅ | Core. |
| `IProjectMonitor` | ✅ | ✅ | Core. |
| `IApiStateProjector` | ✅ | ✅ | Core. |

### 1.3 Git

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IGitProcessRunner` | ✅ | ✅ | Core. |
| `IGitStatusService` | ✅ | ✅ | Core. |
| `IGitWorktreeService` | ✅ | ✅ | Core. |
| `IGitPrService` | ✅ | ✅ | Core; shells to `gh`. |
| `IGitWorkspaceFactory` | ✅ | ✅ | Core. |
| `IGitIgnoreService` | ✅ | ✅ | Core. |
| `IGitHubService` | ✅ | ✅ | Core. |
| `ICommitGraphService` | ✅ | ❓ | WPF renders a graph in `CommitHistoryContentView`. Avalonia's `CommitHistoryView.axaml` exists but graph drawing has not been confirmed. |

### 1.4 File tools, detection

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IFileExplorerService` | ✅ | ✅ | Core. |
| `IFilePreviewService` | ✅ | 🟡 | Core service registered. In Avalonia the field is injected into `MainViewModel` but unused — preview wiring is on `FileViewerView` directly. |
| `IFileEditService` | ✅ | 🟡 | Same situation as above. |
| `IMarkdownService` | ✅ | 🟡 | Same situation as above. |
| `ILinkDetectionService` | ✅ | ✅ | Core. |
| `IProjectDetectionService` | ✅ | ✅ | Core. |
| `IRunUrlDetectionService` | ✅ | ✅ | Core. |
| `IInputPromptDetectionService` | ✅ | ✅ | Core. |
| `IDiffParserService` | ✅ | ✅ | Core. |
| `IInvisibleChangeService` | ✅ | ✅ | Core. |

### 1.5 Claude Code & sessions

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IClaudeCommandService` | ✅ | ✅ | Core. |
| `IClaudeSessionIndexService` | ✅ | ✅ | Core. |
| `IClaudeTaskFileService` | ✅ | ✅ | Core. |
| `IClaudeTaskDetectionService` | ✅ | 🟡 | Avalonia injects but doesn't read it from `MainViewModel`. May be consumed by `ITaskAggregator`. |
| `ITaskService` | ✅ | ✅ | Core. |
| `ITaskAggregator` | ✅ | 🟡 | Avalonia injects but doesn't read from `MainViewModel`. |
| `ILiveSessionTracker` | ✅ | ✅ | Core. |
| `ISessionActivityService` | ✅ | ✅ | Core. |
| `ITimelineService` | ✅ | ✅ | Core. |
| `ITranscriptWatcher` | ✅ | ✅ | Core. |
| `ISessionArchiveService` | ✅ | ❓ | Need to confirm whether Avalonia DI registers this. |
| `IHookInstaller` | ✅ | ✅ | Per-platform adapter. |

### 1.6 AI, voice, audio

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IAiAssistantService` | ✅ | ✅ | Core. |
| `IAiExecutionService` | ✅ | ✅ | Core. |
| `IVoiceCommandService` | ✅ | 🟡 | WPF supports Windows SAPI + Whisper. Avalonia only supports Whisper (cross-platform). |
| `IAudioCaptureService` | 🟡 | ✅ | WPF wraps NAudio on Windows. Avalonia adds POSIX adapter. |
| `WhisperModelManager` | ✅ | ✅ | Core. |
| `ISoundService` | ✅ | ✅ | Per-host adapter. |

### 1.7 API, webhooks, integrations

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IApiServer` | ✅ | ✅ | Core. |
| `IEventAggregatorService` | ✅ | ✅ | Core. |
| `IWebhookDeliveryService` | ✅ | 🟡 | Avalonia injects but doesn't read from `MainViewModel`. |
| `ICollabService` (TerminalHost MCP) | ✅ | ❓ | Verify Avalonia registration. |
| `McpHandler` | ✅ | ❓ | Verify Avalonia registration. |
| `IEidetService` (memory) | ✅ | ✅ | Core `HttpEidetService`. |
| `IContainerService` | ✅ | ✅ | Core. |
| `IContainerConfiguration` | ✅ | ✅ | Core. |
| `IDebugLogService` | ✅ | ✅ | Core. |

### 1.8 UI services

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IDialogService` | ✅ | ✅ | Per-host adapter. |
| `IToastService` | ✅ | ✅ | Per-host adapter. |
| `ISystemTrayService` | ✅ | 🟡 | WPF: Windows tray icon. Avalonia: macOS menubar; Windows tray needs verification. |
| `StatusOverlayService` | ✅ | ✅ | Per-host adapter. |
| `ExplorerEventRouter` | ✅ | ✅ | Core router used by both. |
| `LinkClickHandler` | ✅ | ✅ | Per-host wiring around Core service. |
| `ITaskbarProgressService` | ✅ | ❌ | Windows-only API (`ITaskbarList3`); no cross-platform equivalent. |
| `UiThreadWatchdog` | ✅ | ❌ | WPF-only diagnostic. Low priority for Avalonia. |
| `IViewModelFactory` | ✅ | ❌ | WPF abstraction; Avalonia composes VMs directly via DI. Likely fine. |
| `PanelWindowManager` | ✅ | 🟡 | WPF: pop-out panel windows. Avalonia: `PanelWindow.axaml` exists but pop-out manager not yet ported. |

### 1.9 Statistics, search, test

| Service | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| `IStatisticsService` | ✅ | ✅ | Core. |
| `ISearchService` | ✅ | ✅ | Per-host (file IO heavy). |
| `ITestRunnerService` | ✅ | ✅ | Per-host. |

---

## 2. View / feature inventory

Sourced from filesystem listings of `Views/` in each host.

### 2.1 Core chrome

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| Main window | ✅ | ✅ | Different shells, similar layout. |
| Tab strip with drag-reorder | ✅ | ✅ | |
| Tab dropdown | ✅ | ✅ | |
| Tab switcher (Ctrl+Shift+T) | ✅ | ✅ | |
| Command palette | ✅ | ✅ | |
| Workspace sidebar | ✅ | ✅ | `WorkspaceSidebarView` vs `WorkspaceSidebar`. |
| Setup window | ✅ | ✅ | |
| Settings | ✅ | ✅ | |
| Profiles | ✅ | ✅ | |
| Statistics | ✅ | ✅ | |
| Dashboard | ✅ | ✅ | |
| Recent features ("What's New") | ✅ | ✅ | `RecentFeaturesContentView` vs `RecentFeaturesView`. |
| Help | ✅ | ✅ | |
| Toasts | ✅ | ✅ | `ToastContainerView` + `ToastWindow` on both. |
| Status overlay window | ✅ | ✅ | |
| Voice bar | ✅ | ✅ | |
| Tab content: terminal pair | ✅ | 🟡 | Avalonia view exists; terminal control renders black on Windows. |
| Tab content: profile terminal | ✅ | ✅ | |

### 2.2 Git UI

| Feature | WPF view | Avalonia view | Status |
|---------|----------|---------------|--------|
| Branches list | `GitBranchesContentView.xaml` | `Popups/GitBranchView.axaml` | ✅ |
| Changed files / staging | `GitFilesContentView.xaml` | `Popups/GitFilesView.axaml` | ✅ |
| Stash | `GitStashContentView.xaml` | `Popups/GitStashView.axaml` | ✅ |
| Tags | `GitTagsContentView.xaml` | `Popups/GitTagsView.axaml` | ✅ |
| Commit history | `CommitHistoryContentView.xaml` | `Popups/CommitHistoryView.axaml` | 🟡 (commit graph parity unconfirmed) |
| Branch comparison | `BranchComparisonContentView.xaml` | `Popups/BranchComparisonView.axaml` | ✅ |
| Reflog | `Popups/ReflogView.xaml` | `Popups/ReflogView.axaml` | ✅ |
| File blame | `FileBlameContentView.xaml` | `Popups/FileBlameView.axaml` | ✅ |
| File history | `FileHistoryContentView.xaml` | `Popups/FileHistoryView.axaml` | ✅ |
| Merge conflict viewer | `MergeConflictView.xaml` | `Popups/MergeConflictView.axaml` | ✅ |
| Manage worktrees | `Popups/ManageWorktreesView.xaml` | `Popups/ManageWorktreesView.axaml` | ✅ |
| Repository switcher | `Popups/RepositorySwitcherView.xaml` | `Popups/RepositorySwitcherView.axaml` | ✅ |
| Unified git panel | `UnifiedGitPanelContentView.xaml` | `Popups/UnifiedGitPanelView.axaml` | ✅ |
| Create-worktree dialog | `Dialogs/CreateWorktreeDialog.xaml` | `Dialogs/CreateWorktreeDialog.axaml` | ✅ |

### 2.3 Files, search, markdown

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| File explorer panel | ✅ | ✅ | |
| File viewer (inline) | ✅ | ✅ | |
| File viewer window (pop-out) | ✅ | ✅ | |
| File viewer popup | ❌ | ✅ | `Popups/FileViewerPopup.axaml` is Avalonia-only; WPF uses the inline view. |
| File preview popup | ❌ | ✅ | `Popups/FilePreviewView.axaml` — Avalonia-only. |
| Markdown preview | ✅ (`MarkdownPreviewView.xaml`) | ✅ (`MarkdownPreviewWindow.axaml`) | Avalonia is a window; WPF is a content view. |
| Search across files | ✅ | ✅ | |
| Detected links | ✅ | ✅ | |

### 2.4 Sessions, timeline, AI

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| Timeline view | `TimelineView.xaml` + `TimelineWindow.xaml` | `TimelineModeView.axaml` | 🟡 — Avalonia has the view but no pop-out window; feature depth needs verification. |
| Sessions tree panel | `SessionsTreeContentView.xaml` | `Panels/SessionsTreePanelView.axaml` | ✅ |
| Claude tasks panel | `ClaudeTasksContentView.xaml` | `Panels/ClaudeTasksPanelView.axaml` | ✅ |
| Task panel (popup) | ❌ | `Popups/TaskPanelView.axaml` | Avalonia-only addition. |
| Workspace tasks | ❌ | `Panels/WorkspaceTasksPanelView.axaml` | Avalonia-only addition. |
| Spark Canvas | `SparkCanvasView.xaml` + `SparkCanvasWindow.xaml` | `SparkCanvasView.axaml` + `SparkCanvasWindow.axaml` | ✅ |
| Memory browser (Eidet) | `MemoryBrowserContentView.xaml` | `Panels/MemoryBrowserPanelView.axaml` | ✅ |
| Debug log | `DebugLogContentView.xaml` | `Panels/DebugLogPanelView.axaml` | ✅ |
| Hook debug dialog | `Dialogs/HookDebugDialog.xaml` | `Dialogs/HookDebugDialog.axaml` | ✅ |
| Scratch pad | `ScratchPadContentView.xaml` | `ScratchPadView.axaml` | ✅ |
| Quick note popup | ❌ | `Popups/QuickNoteView.axaml` | Avalonia-only. |
| Quick task popup | ❌ | `Popups/QuickTaskView.axaml` | Avalonia-only. |

### 2.5 GitHub, PR, tests

| Feature | WPF | Avalonia | Notes |
|---------|-----|----------|-------|
| PR review | `PrReviewContentView.xaml` | `Popups/PrReviewView.axaml` | ✅ |
| Test results | `TestResultsContentView.xaml` | `Popups/TestResultsView.axaml` | ✅ |

### 2.6 Dialogs (gaps)

| Dialog | WPF | Avalonia | Notes |
|--------|-----|----------|-------|
| Input dialog | ✅ | ✅ | |
| Notification dialog | ✅ | ✅ | |
| Create-worktree | ✅ | ✅ | |
| Create-intent | `Dialogs/CreateIntentDialog.xaml` | ❌ | Used for Timeline IDE intent creation. |
| Custom-button | `Dialogs/CustomButtonDialog.xaml` | ❌ | Used by settings to define custom toolbar buttons. |
| Icon picker popup | `Views/Controls/IconPickerPopup.xaml` | ❌ | Used inside Profiles/Settings to pick emoji/icon. |

---

## 3. Keyboard shortcut wiring

| Aspect | WPF | Avalonia |
|--------|-----|----------|
| KeyDown handlers in `MainWindow` code-behind | ~78 | ~108 |
| Source of truth | `ShortcutConflictService` (Core) | Same |
| InputBindings (XAML) | Used heavily | Not used — Avalonia handles everything via code-behind `KeyDown` |
| Cmd/Ctrl handling on macOS | n/a | Handled via per-platform mapping |

Shortcut coverage is roughly equivalent. Differences come from Avalonia needing
extra branches for macOS Cmd-vs-Ctrl and from `InputBindings` not being a
first-class Avalonia concept.

---

## 4. Known gaps and platform-only items

### WPF-only (not yet ported / not portable)

- `ITaskbarProgressService` — Win32 `ITaskbarList3`. Not portable.
- `UiThreadWatchdog` — WPF diagnostic.
- `IViewModelFactory` — likely an unnecessary abstraction for Avalonia.
- `CreateIntentDialog`, `CustomButtonDialog`, `IconPickerPopup` — small UI gaps.
- `PanelWindowManager` — pop-out floating panel windows. Avalonia has the host
  window (`PanelWindow.axaml`) but the manager that promotes/demotes panels is
  not yet ported.

### Avalonia-only

- `Popups/QuickNoteView`, `Popups/QuickTaskView` — quick-capture popups.
- `Popups/FileViewerPopup`, `Popups/FilePreviewView` — file inspection popups.
- `Panels/WorkspaceTasksPanelView` — workspace-scoped task panel.

### Cross-cutting observations

- Both hosts share %APPDATA%\TerminalHost as of commit 95d80eb, so config and
  session state move with the user regardless of which host they launch.
- The Avalonia Windows terminal renders black; root cause is somewhere between
  `NativeControlHost`, the `EasyTerminalControl` HWND parenting, and font/DPI
  initialization. Tracked separately.

---

## 5. Avalonia `MainViewModel`: unused injected services

These fields are declared and assigned in the constructor but never read
anywhere else in `src/TerminalHost.Avalonia/ViewModels/MainViewModel.cs`. They
inflate the constructor signature and the DI graph without doing work — either
the feature they back hasn't been wired up yet, or the responsibility moved to
another VM and the injection was forgotten.

| Field | Type | Line | Likely status |
|-------|------|------|---------------|
| `_filePreviewService` | `IFilePreviewService` | 32 | Used by `FileViewerView`/`FilePreviewViewModel`; can be removed from `MainViewModel`. |
| `_fileEditService` | `IFileEditService` | 33 | Same as above. |
| `_markdownService` | `IMarkdownService` | 38 | Used by markdown preview; can be removed from `MainViewModel`. |
| `_filePickerService` | `IFilePickerService` | 43 | No usage; either wire it for "Open file" / drop, or remove. |
| `_claudeTaskDetectionService` | `IClaudeTaskDetectionService?` | 47 | Probably superseded by `ITaskAggregator`; check whether it should be removed from the ctor. |
| `_taskAggregator` | `ITaskAggregator?` | 48 | Currently only assigned; likely needs to feed a tasks panel binding. |
| `_webhookDeliveryService` | `IWebhookDeliveryService?` | 52 | No usage; either expose start/stop commands or remove. |

Suggested follow-up: drop the fields from the constructor for the ones that
truly aren't needed in `MainViewModel`, and file a small issue for the ones
that should be wired (most likely `_taskAggregator`).

---

## 6. Things explicitly not covered yet

- A side-by-side test inventory (which tests target which host).
- Per-feature settings-page parity (Settings views have lots of sections; a
  full subsection-by-subsection check has not been done).
- `IApiServer` route-level parity — both hosts register `ApiServer`, but route
  coverage was not audited.
- Behaviour of `IGitTagsService`, `ITestRunnerService`, `ISearchService` under
  Avalonia at runtime — confirmed registered, not confirmed end-to-end.

When verifying any 🟡 or ❓ row, update this document in the same PR.
