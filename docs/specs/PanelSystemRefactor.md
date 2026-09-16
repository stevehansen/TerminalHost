# Panel System Refactor — Audit & Deepening Candidates

> **Status**: Audit / pre-design.
> Originating discussion: `/improve-codebase-architecture panels/windows/pop-out/docking, seems we have multiple interpretations and implementations, wpf vs avalonia but even inside wpf as well`.

## Audit Summary

The codebase has a **partially-realized** unified panel system. `IPanelableViewModel` + `BasePanelViewModel` exist in `TerminalHost.Core` as a unifying abstraction, and `PanelHost` / `PanelWindow` provide reusable hosts — but they coexist with **four parallel presentation mechanisms** that do not route through this abstraction:

1. **Right-sidebar dock** — `PanelHost` (WPF) backed by `PanelWindowManager`; manages a tab strip of docked `IPanelableViewModel`s. Detach event bubbles up; dock-back event flows down.
2. **Center overlay** — Driven by an `ActiveCenterPanel` property on `TerminalPairTabViewModel`. View is selected via DataTemplate dispatch in `PanelContentTemplates.xaml`. Used for Unified Git Panel, File Viewer, Test Results, PR Review, Branch Comparison, Search, Markdown Preview.
3. **Main-window popups** —
   - **WPF**: Named `Popup` controls in `MainWindow.xaml`, each `.IsOpen`-bound to a separate boolean (`IsTabSwitcherOpen`, `Palette.IsOpen`, `IsHelpOpen`, etc.) on `MainViewModel`.
   - **Avalonia**: Visibility-bound `Panel` children of a shared `PopupHost` in `MainWindow.axaml`, each gated by an `IsXxxOpen` property — same model, different host.
4. **Detached windows** — Generic `PanelWindow` (both platforms) coexists with bespoke `FileViewerWindow`, `MarkdownPreviewWindow` (Avalonia), `ToastWindow`, `StatusOverlayWindow`, `SetupWindow`, `SparkCanvasWindow`, each with its own IsOpen tracking / owner-following / close-confirmation / dark-mode setup.

### Key structural problems

- **Panels can't transition across zones.** A right-sidebar panel cannot become a popup, a popup cannot become a center overlay, etc., without writing new wiring code. Each zone has a different state-property shape and a different event type (`CenterPanelRestoreRequested`, `RightPanelRestoreEventArgs`).
- **`MainViewModel` carries ~20 boolean overlay flags** that must be manually coordinated (no single-instance enforcement, no shared ESC/click-outside dismissal).
- **Capabilities leak into class identity for detached windows** — "always-on-top" or "non-activating" or "confirm-on-close" is encoded by *which Window subclass* is instantiated, rather than by capability flags.
- **Persistence is smeared.** `DirectorySettings` holds `ActiveCenterPanel`, `PanelStates`, `ActiveLeftPanel`, `ActiveRightPanel`, `LeftPanelSplitRatio`, `IsLeftPanelVisible`, `IsExplorerVisible`, plus per-VM `Width`/`Height`/`IsOpen`/`DisplayState`/`PreferredSide` properties that auto-persist via bindings. WPF and Avalonia each restore subsets via different event types.
- **Cross-platform drift.** `DirectorySettings` defines a left-dock; Avalonia never renders one. WPF has named `Popup` controls; Avalonia has visibility-bound overlay panels. The shared `IPanelableViewModel` abstraction exists but each platform composes it differently.

---

## Deepening Candidates

### Candidate 1 — Panel Presentation Router (the big one)

- **Cluster**: `PanelHost`, `PanelWindow`, `PanelWindowManager`, `ActiveCenterPanel` on `TerminalPairTabViewModel`, the WPF `Popup` controls and Avalonia overlay panels in each `MainWindow`, the `IsXxxOpen` booleans on `MainViewModel`, the `CenterPanelRestoreRequested` / `RightPanelRestoreEventArgs` events, and the per-zone state on `DirectorySettings`.
- **Why coupled**: They all answer the same question — *where is this view shown, and how does it move between zones?* — with four parallel answers, none of which compose.
- **Dependency category**: **In-process.** Pure WPF/Avalonia view composition + in-memory state. The platform difference (`Window`, `Popup`, `ContentControl`) is handled by a small platform-shim port.
- **Test impact**: One boundary test — *Given a panelable VM, when the presenter is told to show/dock/detach/close/move-zone, the resulting visual zone and persisted state match expectations* — replaces per-zone restore tests, per-popup boolean-toggle tests, and `PanelHost`-specific tests.

### Candidate 2 — Unified Window Host

- **Cluster**: `PanelWindow`, `FileViewerWindow`, `MarkdownPreviewWindow`, `ToastWindow`, `StatusOverlayWindow`, `SetupWindow`, `SparkCanvasWindow` across WPF and Avalonia (~10–12 bespoke window classes total).
- **Why coupled**: All do the same job — host an `IPanelableViewModel` in a top-level OS window with a small policy variation — but each is its own class. Capabilities (`AlwaysOnTop`, `NonActivating`, `MultiInstance`, `ConfirmOnClose`, `FollowsOwner`, `Transparent`) are baked into class identity rather than expressed as attributes.
- **Dependency category**: **In-process** with a thin platform-shim port over the OS window primitive.
- **Test impact**: Per-window code-behind tests replaced by a single contract test — *given a VM and capability flags, the host opens a window respecting the flags and round-trips dock/close events*.
- **Note**: Subsumed by Candidate 1 if that lands well — the window host is just one of the presenter's targets.

### Candidate 3 — Overlay/Popup Host

- **Cluster**: Command Palette, Tab Switcher, Tab Dropdown, Help, Reflog, Manage Worktrees, Repository Switcher, Detected Links, Quick Task, Quick Note, Scratch Pad, File History, File Blame, Merge Conflict overlays — plus the ~20 sibling `IsXxxOpen` booleans on `MainViewModel`, plus duplicate ESC/click-outside/focus-restoration logic in code-behind for each.
- **Why coupled**: "Show one transient overlay at a time with optional dismiss-on-click-outside and ESC handling" is implemented per-popup, in two divergent ways across platforms.
- **Dependency category**: **In-process.**
- **Test impact**: Per-popup tests replaced by host-boundary tests: single-instance enforcement, ESC handling, click-outside dismissal, placement preset honored, focus restored on close.
- **Note**: Smaller in scope than Candidate 1; viable as a standalone first step but risks shaping a popup API that doesn't compose with the center-panel mechanism.

### Candidate 4 — Panel State Persistence

- **Cluster**: `DirectorySettings` (`ActiveCenterPanel`, `PanelStates`, `ActiveLeftPanel`, `ActiveRightPanel`, `LeftPanelSplitRatio`, …), `CenterPanelRestoreEventArgs`, `RightPanelRestoreEventArgs`, the `LoadPanelStateFromDirectorySettings` / `WriteToDirectorySettings` methods on tab VMs, the implicit binding-driven persistence on `IPanelableViewModel.Width/Height/IsOpen/DisplayState/PreferredSide`.
- **Why coupled**: No single source of truth for "what was shown where, last time." State is smeared across the settings file, per-VM properties, and zone-specific event types.
- **Dependency category**: **Local-substitutable** — `IConfigurationService` / `IDirectorySettingsStore` are file-backed but mockable.
- **Test impact**: Per-event restore tests replaced by one round-trip test on a `IPanelStateRepository` boundary: save → reload → rehydrate for all zones.
- **Note**: Best done *after* Candidate 1 — persistence shape falls out naturally from the presenter contract. Doing it first risks locking in the fragmented model.

---

## Recommendation

**Start with Candidate 1.** It's the upstream cause of the friction in Candidates 2, 3, and 4. Solve it and the others either become trivial sub-cases or collapse entirely. Going piecemeal risks building (for example) a popup host that doesn't compose with the center-panel mechanism, just adding a fifth parallel system.

If Candidate 1 is too large to swallow whole, **Candidate 3 is the cleanest standalone first step** — most concentrated duplication, clearest boundary.

---

## Detailed File References (audit notes)

### WPF
- `src/TerminalHost.Core/Interfaces/IPanelableViewModel.cs` — contract: `PanelId`, `DisplayState`, `PreferredSide`, `SizePreset`, `Dock/Detach/Close` commands, `StateChangeRequested` event.
- `src/TerminalHost.Core/ViewModels/BasePanelViewModel.cs` — base implementation; defines `PanelDisplayState` (Panel/Window), `PanelSide`, `PanelSizePreset`.
- `src/TerminalHost/TerminalHost/Controls/PanelHost.xaml.cs` — TabControl-style dock for `IPanelableViewModel`s; raises `PanelDetachRequested` / `PanelCloseRequested`.
- `src/TerminalHost/TerminalHost/Services/PanelWindowManager.cs` — centralized window cache keyed by panel ID; resets `DisplayState = Panel` on close.
- `src/TerminalHost/TerminalHost/Views/PanelWindow.xaml.cs` — generic window for any `IPanelableViewModel`; raises `DockRequested`.
- `src/TerminalHost/TerminalHost/Views/FileViewerWindow.xaml.cs`, `Views/ToastWindow.xaml.cs` — bespoke windows.
- `src/TerminalHost/TerminalHost/MainWindow.xaml` (Popups), `Resources/PanelContentTemplates.xaml`, `Resources/TabContentTemplates.xaml` — host templates.
- `src/TerminalHost.Core/Domain/CenterPanelRestoreEventArgs.cs`, `RightPanelRestoreEventArgs` (in `MainViewModel.cs` ~line 2783).

### Avalonia
- `src/TerminalHost.Avalonia/MainWindow.axaml` — `PopupHost` Panel with ~20 visibility-bound overlay children.
- `src/TerminalHost.Avalonia/Views/PanelWindow.axaml(.cs)` — generic detached host (simpler than WPF counterpart).
- `src/TerminalHost.Avalonia/Views/FileViewerWindow.axaml`, `MarkdownPreviewWindow.axaml`, `ToastWindow.axaml`, `StatusOverlayWindow.axaml`, `SetupWindow.axaml`, `SparkCanvasWindow.axaml` — bespoke windows.
- `src/TerminalHost.Avalonia/Views/WorkspaceSidebar.axaml` — left workspace sidebar (only left-side surface; no `IPanelableViewModel` left-dock).

### Shared persistence
- `src/TerminalHost.Core/Domain/AppConfiguration.cs` lines 311–456 (`DirectorySettings`) — `LayoutMode`, `SplitRatio`, `ActiveCenterPanel`, `PanelStates[panelId]`, `ActiveRightPanel`, `ActiveLeftPanel`, `LeftPanelSplitRatio`, `IsLeftPanelVisible`, `GitPanelActiveTab`, etc.

---

---

## ✅ Chosen Design — Panel Presentation Router (Candidate 1)

> Outcome of `/design-interface` on 2026-05-22. Four designs explored in parallel (minimize / maximize-flexibility / optimize-common-case / ports-&-adapters). This is a hybrid of the common-case design (C) and the ports-&-adapters design (D), with the closed-enum discipline of the minimalist design (A). The flexibility-maximizing design (B) was rejected.

### Caller-facing surface (Core)

```csharp
namespace TerminalHost.Core.Panels;

public enum PanelZone { LeftDock, RightDock, Center, Popup, Window }

public readonly record struct PanelScope(string? TabId)
{
    public static readonly PanelScope AppShell = new((string?)null);
    public static PanelScope ForTab(string tabId) => new(tabId);
}

// Opt-in sibling — not added to IPanelableViewModel itself.
public interface IPanelPlacement
{
    PanelZone PreferredZone { get; }
    PanelScope PreferredScope => PanelScope.AppShell;
}

public sealed record PanelShowOptions(
    PanelZone? Zone = null,
    PanelScope? Scope = null,
    bool ForceShow = false,            // disable toggle (focus instead of close)
    bool AllowMultiInstance = false,   // bypass single-instance dedupe
    bool AlwaysOnTop = false,          // call-site capability (window zone)
    object? Anchor = null,             // popup anchor (FrameworkElement / Control)
    object? Context = null);           // payload for parameterized panels

public interface IPanelRouter
{
    // 95% case — one-liners
    void Show<TPanel>() where TPanel : IPanelableViewModel;
    void Show(IPanelableViewModel vm);

    // Advanced
    void Show(IPanelableViewModel vm, PanelShowOptions options);
    void Move(string panelId, PanelZone newZone);
    void Close(string panelId);
    void CloseZone(PanelZone zone, PanelScope scope);    // ESC handler hook
    bool IsOpen(string panelId);
    IPanelableViewModel? Get(string panelId);

    event EventHandler<PanelRoutedEventArgs>? Routed;    // for persistence/telemetry
}
```

### Platform-shim ports (Core)

```csharp
public interface IPanelSurface
{
    PanelZone Zone { get; }
    PanelScope Scope { get; }
    void Mount(IPanelableViewModel vm, PanelMountOptions options);
    void Unmount(string panelId);
    void Focus(string panelId);
    bool IsMounted(string panelId);
    event EventHandler<PanelDismissEventArgs>? DismissRequested; // ESC / click-outside / OS close
}

public sealed record PanelMountOptions(
    PanelSizePreset Size, bool DismissOnClickOutside, bool AlwaysOnTop, bool ConfirmOnClose);

public interface IUiDispatcher { void Post(Action a); bool IsOnUiThread { get; } }

public interface IPanelPersistence
{
    PanelLayoutSnapshot Load(PanelScope scope);
    void Save(PanelScope scope, PanelLayoutSnapshot snapshot);
}
```

Surfaces are resolved through DI by `(zone, scope)`. **No `IPanelSurfaceRegistry`** — it was identified as registry-of-registries over-abstraction in the design exploration.

### Usage at call sites

```csharp
// MainViewModel — Help popup
[RelayCommand] void OpenHelp() => _router.Show<HelpViewModel>();

// TerminalPairTabViewModel — git changes in its center slot
[RelayCommand] void ShowGitChanges() => _router.Show<GitFilesViewModel>();

// Parameterized open (file viewer)
[RelayCommand] void OpenFile(string path)
    => _router.Show(_fileViewer, new PanelShowOptions(Context: path));

// Force-window for multi-monitor users
[RelayCommand] void PopOutHelp()
    => _router.Show<HelpViewModel>(new PanelShowOptions(Zone: PanelZone.Window, ForceShow: true));

// Panel declares its home zone
public sealed class GitFilesViewModel : BasePanelViewModel, IPanelPlacement
{
    public PanelZone PreferredZone => PanelZone.RightDock;
    public PanelScope PreferredScope => PanelScope.ForTab(_tabId);
}
```

User-triggered pop-out / dock-back arrives via `BasePanelViewModel.StateChangeRequested`, which the router subscribes to once per registered panel. **Call sites do not wire events.**

### Test surface (in-memory adapter)

```csharp
public sealed class FakePanelSurface : IPanelSurface
{
    public PanelZone Zone { get; init; }
    public PanelScope Scope { get; init; }
    public IPanelableViewModel? Mounted { get; private set; }
    public int Mounts, Unmounts, Focuses;
    public void Mount(IPanelableViewModel vm, PanelMountOptions _) { Mounted = vm; Mounts++; }
    public void Unmount(string _) { Mounted = null; Unmounts++; }
    public void Focus(string _) => Focuses++;
    public bool IsMounted(string id) => Mounted?.PanelId == id;
    public event EventHandler<PanelDismissEventArgs>? DismissRequested;
    public void RaiseDismiss(string panelId)
        => DismissRequested?.Invoke(this, new(panelId, DismissTrigger.Escape));
}

// Router unit test — no WPF/Avalonia loaded
[Fact]
public void RightDock_to_Window_transitions_preserve_VM()
{
    var scope = PanelScope.ForTab("tab1");
    var dock = new FakePanelSurface { Zone = PanelZone.RightDock, Scope = scope };
    var win  = new FakePanelSurface { Zone = PanelZone.Window,    Scope = scope };
    var router = new PanelRouter(
        surfaces: new[] { dock, win },
        persistence: new InMemoryPanelPersistence(),
        dispatcher: new SyncDispatcher());
    var git = new GitFilesViewModel();

    router.Show(git, new(Zone: PanelZone.RightDock, Scope: scope));
    router.Move("gitFiles", PanelZone.Window);

    Assert.Null(dock.Mounted);
    Assert.Same(git, win.Mounted);
    Assert.Equal(PanelDisplayState.Window, git.DisplayState);
}
```

### What the router owns

- Single-instance dedupe by `PanelId` across all zones in a scope.
- Toggle vs focus vs no-op semantics (`Show` of an already-open panel in its current zone closes it unless `ForceShow`).
- Atomic zone transitions (unmount-old → update `DisplayState`/`PreferredSide` → mount-new).
- Subscription to `BasePanelViewModel.StateChangeRequested` — translates user-driven dock/detach into `Move`.
- Persistence emission on every `Routed` event, projected to `DirectorySettings.PanelStates`/`ActiveCenterPanel`/etc. via `IPanelPersistence`.
- Replay on startup: iterate `PanelLayoutSnapshot` and call `Show(vm, new(Zone: saved, ForceShow: true))` per restored panel.
- UI-thread marshalling via `IUiDispatcher` for events from background threads.

### What the router hides

- WPF `Popup` vs Avalonia visibility-bound overlay.
- WPF/Avalonia `Window` creation, owner-window pinning, always-on-top, non-activating, dark-mode chrome — capability flags on `PanelMountOptions`, surfaces translate.
- ESC / click-outside / OS-window-close dispatch — one handler per surface, funnels through `DismissRequested`.
- DataTemplate dispatch (the surface picks the view for a VM).
- The smeared `DirectorySettings` shape (`ActiveCenterPanel` + `PanelStates` + `ActiveLeft/RightPanel` + `LeftPanelSplitRatio` + `IsLeftPanelVisible`) is projected into one `PanelLayoutSnapshot`.

### Designs considered and rejected

- **Design B (Maximize flexibility — `[Flags] PanelCapability`, pluggable dismissal/instance-key/persistence strategies, builder DSL, open `ZoneId` record-struct).** Rejected: 5 zones is a closed set; the registry-of-zones flexibility doesn't pay for the startup-config burden, fuzzy capability negotiation, and `ZoneHints` typing escape hatch. Adding a 6th zone (e.g., `BottomDock`) is one enum value + one `IPanelSurface` impl — cheaper than the registration machinery.
- **Design A (Minimize — single `Show(key, vm, zone)` method).** Rejected as the *primary* shape: too austere at call sites; loses `Show<TPanel>()` type resolution; weak typing across the surface port. Kept its closed `PanelZone` enum and its philosophy that persistence is router-managed on every transition.
- **Design D's `IPanelSurfaceRegistry` (registry-of-registries).** Rejected — flagged by Agent D itself as the risky over-abstraction. Surfaces resolved directly by `(zone, scope)` through DI.

### Dependency strategy

| External touch | Port | WPF adapter | Avalonia adapter | Test adapter |
|---|---|---|---|---|
| `Popup` control / visibility overlay | `IPanelSurface(Popup)` | `WpfPopupSurface` | `AvaloniaOverlaySurface` | `FakePanelSurface` |
| Top-level `Window` | `IPanelSurface(Window)` | `WpfWindowSurface` (subsumes `PanelWindowManager`, `FileViewerWindow`, `MarkdownPreviewWindow`) | `AvaloniaWindowSurface` (same) | `FakePanelSurface` |
| `PanelHost` left/right tabs | `IPanelSurface(LeftDock/RightDock)` | `WpfPanelHostSurface` | `AvaloniaDockSurface` (new — left/right docks are currently unimplemented in Avalonia) | `FakePanelSurface` |
| Center `ContentControl` slot | `IPanelSurface(Center)` | `WpfCenterSurface` | `AvaloniaCenterSurface` | `FakePanelSurface` |
| Dispatcher | `IUiDispatcher` | `WpfDispatcher` | `AvaloniaDispatcher` | `SyncDispatcher` |
| Per-directory layout state | `IPanelPersistence` | `DirectorySettingsPanelPersistence` (Core, reuses existing `IDirectorySettingsStore`) | same | `InMemoryPanelPersistence` |

All listed are **in-process** dependencies; the platform-shim ports are the only abstraction worth paying for. `IPanelPersistence` is a thin facade over existing services — kept because it hides the smeared `DirectorySettings` shape from the router.

### Test strategy (replace, don't layer)

**New boundary tests** on `IPanelRouter` against in-memory adapters:
- Single-instance dedupe across all zones in a scope.
- Toggle semantics (`Show` of currently-open panel closes it) and `ForceShow` override.
- Atomic zone transitions preserve VM identity and state (`Move` round-trip).
- `StateChangeRequested` from a VM translates to the expected `Move`.
- Persistence round-trip: `Show → snapshot → router restart → restored panels active in the same zones`.
- `CloseZone` from ESC dismisses every panel in that zone for that scope only.
- Scope isolation: `PanelScope.ForTab("a")` panels do not interfere with `PanelScope.ForTab("b")` or `AppShell`.

**Old tests to delete**:
- `PanelWindowManager` per-window unit tests (window cache, dock-back wiring).
- Per-popup boolean toggle tests on `MainViewModel` (`IsHelpOpen`, `IsTabSwitcherOpen`, `Palette.IsOpen`, etc.).
- `CenterPanelRestoreRequested` / `RightPanelRestoreEventArgs` event-fan-out tests.
- Any per-zone "is this VM in the right collection" tests against `RightPanels` / `LeftPanels` / `ActiveCenterPanel`.

**Backstop UI tests** (FlaUI + Avalonia.Headless) cover what in-memory adapters can't: WPF airspace, focus restoration, popup z-order, dark-mode chrome — one smoke test per surface adapter is sufficient.

### Migration sketch

1. Land the Core types + WPF adapters; route Help, Command Palette, Tab Switcher, Tab Dropdown through the router (popup-only proof of concept). Delete those four `IsXxxOpen` booleans from `MainViewModel`. **See Phase 1 design block below for the locked implementation shape.**
2. Migrate `PanelWindowManager` callers → `IPanelSurface(Window)` via `WpfWindowSurface`. Collapse `FileViewerWindow` / `MarkdownPreviewWindow` / `ToastWindow` / `StatusOverlayWindow` capabilities into `PanelMountOptions`.
3. Migrate `PanelHost` (right-dock) → `IPanelSurface(RightDock)` via `WpfPanelHostSurface`. Add `AvaloniaDockSurface` to give Avalonia the dock it currently lacks.
4. Migrate `ActiveCenterPanel` → `IPanelSurface(Center)`. Delete `CenterPanelRestoreRequested` / `RightPanelRestoreEventArgs`; `IPanelPersistence` now owns restore.
5. Last: `LeftDock` (currently used only by File Explorer on WPF) — straight port.

Expect ~40–60 call-site edits, mostly in `MainViewModel` and `TerminalPairTabViewModel`. The ~20 `IsXxxOpen` booleans all delete in one sweep at step 1.

---

*Document version: 1.1 — 2026-05-22 (added chosen design from `/design-interface`).*

---

## ✅ Phase 1 Design — WPF Popup Surface

> Outcome of `/design-interface phase 1` on 2026-05-22. The Phase 0 contracts (`IPanelRouter`, `IPanelSurface`, `IPanelPersistence`) are locked; this block records the six implementation-shape calls that the migration sketch left open, so the implementer doesn't relitigate them mid-flight.

### Scope of Phase 1

Route four WPF popups through the router: Help, Command Palette, Tab Switcher, Tab Dropdown. Delete `IsHelpOpen`, `IsTabSwitcherOpen`, `IsTabDropdownOpen`, and `Palette.IsOpen` from `MainViewModel`. Persistence adapter and DI wiring land in the same phase. Other zones (RightDock, Center, Window, LeftDock) wait for later phases.

### Six locked implementation calls

| # | Choice | Decision | Rationale |
|---|--------|----------|-----------|
| 1 | Single shared `Popup` vs N popups | **Single shared `Popup`** in `WpfPopupSurface`; one mounted VM at a time | UX is already exclusive; router enforces single-instance per (zone, scope) |
| 2 | View resolution | **Implicit `DataTemplate` keyed by VM type** (added to `App.xaml` or `PanelHostTemplates.xaml`) | Matches existing `PanelContentTemplates.xaml` / `TabContentTemplates.xaml` convention; no new port |
| 3 | Where popup chrome lives | **The mounted `UserControl` *is* the chrome.** `Popup.Child` is a `ContentPresenter` bound to the mounted VM | Zero XAML churn in popup views |
| 4 | VM extraction | **Extract** `TabSwitcherViewModel` and `TabDropdownViewModel` (taking the `Switcher*` / `Dropdown*` properties off `MainViewModel`). Promote `CommandPaletteViewModel` (already exists) and `HelpViewModel` (already exists) to implement `IPanelableViewModel`. | Wrappers would defeat the refactor's purpose. Real VMs now, not later. |
| 5 | Click-outside dismiss for popup zone | Surface computes `DismissOnClickOutside = true` for `Popup` zone by default; `PanelMountOptions.DismissOnClickOutside` stays as-is. **Do not extend `PanelShowOptions`** with zone-specific knobs. | Keeps `PanelShowOptions` zone-agnostic; zone defaults live in the surface |
| 6 | Persistence filter for popup zone | `DirectorySettingsPanelPersistence` (Core) **filters out entries with `Zone == Popup`** when saving | Popups are transient; never restore on cold start |

### Supporting decisions

- **DI for the router's view-model factory**: register `Func<Type, IPanelableViewModel?>` as `t => sp.GetService(t) as IPanelableViewModel`. No new port.
- **Popup mount target in `MainWindow.xaml`**: replace the four named `Popup` controls with a single `WpfPopupSurfaceHost` user control (or `<Popup x:Name="RoutedPopupHost"/>` if even that is overkill). `WpfPopupSurface` resolves the host after `MainWindow.Show()` in `App.OnStartup`.
- **ESC and dismiss flow**: existing per-view `PreviewKeyDown` handlers stay in code-behind but invoke the VM's `CloseCommand` instead of mutating `MainViewModel.IsXxxOpen`. Click-outside dismissal flows via `Popup.Closed` → surface raises `DismissRequested` → router calls `Close`.
- **Focus restoration**: `IPanelSurface.Focus(panelId)` re-focuses the popup's default input control (existing `Loaded`-event self-focus stays).
- **`PanelRouter.BuildMountOptions` adjustment**: currently passes `DismissOnClickOutside: false` hardcoded. Phase 1 either (a) leaves it hardcoded and the surface ignores the flag in favor of its zone default, or (b) the router asks the surface for its default. Pick (a) — simpler, the surface is the source of truth for zone-specific behavior. Document in `BuildMountOptions` that the field is currently a hint, not a contract.

### Designs considered and rejected for Phase 1

- **Thin wrapper VMs around `MainViewModel`-bound popups instead of extraction.** Rejected — would carry forward state smearing, and a follow-up extraction would touch the same four sites again. Extract now while we're already there.
- **Per-popup named `Popup` controls in `MainWindow.xaml`, surface picks one by id.** Rejected — more XAML, no functional gain over a single shared `Popup` with `ContentPresenter` + `DataTemplate`.
- **New `IViewResolver` / `IViewDescriptor` port.** Rejected — WPF `DataTemplate` already does this; a port would add testable surface area that FlaUI smoke tests already cover.
- **Extending `PanelShowOptions` with `DismissOnClickOutside` / `Placement` / `Anchor`.** Rejected for Phase 1 — popup zone has one sensible default (center, click-outside dismiss). Defer until a caller actually needs an anchored popup. (`PanelShowOptions.Anchor` already exists from Phase 0 but the popup surface ignores it in Phase 1.)
- **Centralizing ESC handling in the router via a global keybinding.** Rejected — per-view `PreviewKeyDown` still works fine when routed to `CloseCommand`. A global ESC fan-in can come later via `CloseZone(Popup, AppShell)` if needed.

### Files this phase will touch

- **New**:
  - `src/TerminalHost/Services/Panels/WpfPopupSurface.cs`
  - `src/TerminalHost/Services/Panels/DirectorySettingsPanelPersistence.cs` *(Core if portable, else WPF)*
  - `src/TerminalHost/ViewModels/TabSwitcherViewModel.cs`
  - `src/TerminalHost/ViewModels/TabDropdownViewModel.cs`
  - `src/TerminalHost/Resources/PanelHostTemplates.xaml` *(or extend `App.xaml` `Application.Resources`)*
- **Modified**:
  - `src/TerminalHost/App.xaml.cs` — DI registrations (`IPanelRouter`, `IPanelSurface(Popup, AppShell)`, `IPanelPersistence`, factory lambda)
  - `src/TerminalHost/MainWindow.xaml` — collapse 4 popups → 1 mount host
  - `src/TerminalHost/MainWindow.xaml.cs` — remove popup-coordination code where stale
  - `src/TerminalHost/ViewModels/MainViewModel.cs` — delete 4 `IsXxxOpen` properties + their `partial void OnXxxChanged` handlers + their setters in `[RelayCommand]` methods; replace with `_router.Show<HelpViewModel>()` etc.
  - `src/TerminalHost/ViewModels/HelpViewModel.cs`, `CommandPaletteViewModel.cs` — implement `IPanelableViewModel` (inherit `BasePanelViewModel`)
  - Existing popup views' code-behind (`HelpView.xaml.cs` etc.) — replace `IsXxxOpen = false` with `viewModel.CloseCommand.Execute(null)`
- **Tests**:
  - `tests/TerminalHost.Tests/Panels/WpfPopupSurfaceTests.cs` — *only what's testable headlessly*; rely on FlaUI smoke for the rest

### Out of scope for Phase 1

- Right-dock migration (Phase 2)
- Window-zone migration / `PanelWindowManager` collapse (Phase 2 or 3)
- Center overlay migration (Phase 4)
- Avalonia parity (deferred until WPF surfaces all settle)

---

*Document version: 1.2 — 2026-05-22 (added Phase 1 design block).*

---

## ✅ Phase 2 Design — WPF Window Surface

> Outcome of `/design-interface phase 2` on 2026-05-22. Phase 0 locked `IPanelSurface`, `PanelMountOptions`, `IPanelRouter`; Phase 1 proved the popup surface. This block records the implementation-shape calls for the window zone so the implementer doesn't relitigate them mid-flight.

### Scope of Phase 2

Land `WpfWindowSurface : IPanelSurface(Window, AppShell)`. Collapse `PanelWindowManager` into the surface (window cache, owner pinning, dock-back wiring move inside). Delete `FileViewerWindow.xaml(.cs)` by expressing its only divergent capability — unsaved-changes confirmation — as a `IPanelCloseGuard` opt-in interface on the VM. Route the eight `_panelWindowManager?.ShowWindow/CloseWindow/GetWindow` call sites in `MainWindow.xaml.cs` through `IPanelRouter`.

**Toast and StatusOverlay are explicitly out of scope** — they host services, not `IPanelableViewModel`s, and collapsing them needs synthetic VM adapters that warrant their own design pass. Likewise `MarkdownPreviewWindow` (Avalonia-only) waits for Avalonia parity.

### Six locked implementation calls

| # | Choice | Decision | Rationale |
|---|--------|----------|-----------|
| 1 | One generic window class vs N | **One `PanelWindow.xaml` (existing).** Delete `FileViewerWindow.xaml(.cs)`. View resolution via `DataTemplate` keyed by VM type, same as the popup zone. | Capabilities-as-attributes, not capabilities-as-subclass. The chrome (dock button, dark-mode init, OnClosed → `IsOpen=false`) is already in `PanelWindow.xaml.cs` — no new work. |
| 2 | How `ConfirmOnClose` becomes dynamic | **Add opt-in sibling interface** `IPanelCloseGuard { bool CanClose(); }`. `PanelMountOptions.ConfirmOnClose` stays as a static *hint* the router computes from `vm is IPanelCloseGuard`; the surface invokes `CanClose()` from `OnClosing`. | A static bool can't express "ask only if modified". A `Func<bool>` in `PanelMountOptions` would leak surface concerns into the router. Sibling interface matches `IPanelPlacement` precedent. |
| 3 | Owner-window injection | **Lazy owner getter** in `WpfWindowSurface` constructor: `Func<Window> ownerProvider`. DI binding resolves `Application.Current.MainWindow` on first use. | Avoids the race where the surface is constructed before `MainWindow.Show()`. Mirrors `WpfPopupSurface.AttachHost` lazy-attach. |
| 4 | Dock-back target (center vs right-sidebar) | **Leave `IsCenterPanel(panelId)` switch in `MainWindow.xaml.cs` as-is**, but the dock-back path goes through `IPanelRouter.Move(panelId, target)` instead of `_panelWindowManager.CloseWindow` + `currentTab.ShowCenterPanel`. The router treats Center/RightDock as ordinary `Move` targets. | Unifying center-vs-right is Phase 4's job (Center surface migration). Phase 2 should not also change that policy decision. |
| 5 | `IsDetached` redundancy on `FileViewerViewModel` | **Delete `IsDetached`** — replace its 3 binding/check sites with `DisplayState == PanelDisplayState.Window`. | `ApplyDisplayState` already sets `DisplayState` before `Mount`; `IsDetached` was the workaround for the missing router. |
| 6 | Persistence: window-zone entries | `DirectorySettingsPanelPersistence` already passes `Zone == Window` through (popup-only filter stays). Restore on cold start re-mounts via the resolver lambda. **VM-owned `Width`/`Height` keep their existing auto-persist bindings** — not duplicated in `PanelLayoutSnapshot`. | Two-sources-of-truth is the trap to avoid. The snapshot says "this panel was open in zone X"; the VM says "at these dimensions". They compose. |

### Supporting decisions

- **`WpfWindowSurface` window cache**: keep `Dictionary<string, PanelWindow>` keyed by `PanelId`. On `Unmount(panelId)`, call `window.Close()` with a `_suppressClosedEvent` guard (same pattern as `WpfPopupSurface`) so the surface's `Closed` handler doesn't double-fire `DismissRequested` for router-initiated closes.
- **`Focus(panelId)`**: `window.Activate()` (matches existing `_panelWindowManager.GetWindow(...)?.Activate()` semantics).
- **`Mount` of an already-mounted panel**: focus, don't recreate. The router's `Show` / `Move` already guards via `IsMounted`, but the surface should be defensive.
- **Capabilities encoded in `PanelMountOptions`**: today's window panels need none of `NonActivating`, `Transparent`, `ToolWindow` — those are toast/overlay territory and are deferred to Phase 2b. Phase 2 leaves `PanelMountOptions` unchanged. `AlwaysOnTop` is honored if set.
- **OnClosing veto flow**: `PanelWindow.OnClosing` checks `DataContext is IPanelCloseGuard guard && !guard.CanClose()` → `e.Cancel = true; return`. Existing `OnClosed → vm.IsOpen = false` stays — that path triggers the router's `IsOpen` subscription from Phase 1, which calls `Close(panelId)` and unmounts cleanly.
- **`FileViewerViewModel` adoption**: implement `IPanelCloseGuard.CanClose()` returning `!(IsModified && Mode == FileViewerMode.Edit) || _dialogService.Confirm("Unsaved changes…")`. Delete `FileViewerWindow.OnClosing`. The MessageBox call in the current code-behind is technical debt the spec already calls out; route through `IDialogService` while we're here.
- **DI registration**: `services.AddSingleton<IPanelSurface>(sp => new WpfWindowSurface(() => Application.Current.MainWindow!, sp.GetRequiredService<IDispatcherService>()))`. Append to the `IPanelSurface` enumeration that `PanelRouter`'s constructor receives.
- **`MainWindow.xaml.cs` cleanup**: `OnPanelStateChanged` (line ~1218) becomes a thin shim that calls `_router.Move(panel.PanelId, PanelZone.Window | PanelZone.Center | PanelZone.RightDock)`. Remove `_panelWindowManager` field, `OnPanelWindowDockRequested`, and the eight call sites listed in the grounding sweep.

### Designs considered and rejected for Phase 2

- **Per-VM Window subclass, capability-by-identity (status quo).** Rejected — that's the friction being refactored away.
- **`Func<bool> CanClose` in `PanelMountOptions`.** Rejected — leaks surface concerns into the router record. `IPanelCloseGuard` keeps the policy on the VM where it belongs.
- **Closed enum `PanelCloseReason { ProgrammaticClose, UserOsClose, DockBack }` passed to `CanClose`.** Rejected for Phase 2 — only FileViewer needs the prompt, and it doesn't care about reason. Add when a second caller wants to differentiate.
- **`PanelMountOptions.Owner` field.** Rejected — owner is a zone-specific concept (only Window cares). The surface's constructor injection is the right scope.
- **Fold ToastWindow / StatusOverlayWindow into Phase 2.** Rejected — they're not `IPanelableViewModel`s. Phase 2b will design synthetic adapters or a separate `IFloatingOverlay` port.
- **Move `IsCenterPanel` decision into `IPanelPlacement.PreferredZone` now.** Rejected — that's the Center-surface migration (Phase 4) and a separate behavior change. Phase 2 should land cleanly without bundling.
- **Add a `WindowMountOptions : PanelMountOptions` subtype with NonActivating/Transparent/etc.** Rejected for Phase 2 — no current caller needs them. Re-evaluate during Phase 2b when toast/overlay shape is concrete.

### Files this phase will touch

- **New**:
  - `src/TerminalHost/Services/Panels/WpfWindowSurface.cs`
  - `src/TerminalHost.Core/Interfaces/IPanelCloseGuard.cs`
- **Modified**:
  - `src/TerminalHost/App.xaml.cs` — DI registration for `WpfWindowSurface`
  - `src/TerminalHost/MainWindow.xaml.cs` — collapse `_panelWindowManager` field; rewire `OnPanelStateChanged` and `OnPanelWindowDockRequested` through `IPanelRouter.Move`; remove the eight `_panelWindowManager?.ShowWindow/CloseWindow/GetWindow` call sites (lines ~1234, 1259, 1275, 1323, 1514, 1568, 1888, 1944)
  - `src/TerminalHost/Views/PanelWindow.xaml.cs` — add `OnClosing` veto via `IPanelCloseGuard`
  - `src/TerminalHost/ViewModels/FileViewerViewModel.cs` — implement `IPanelCloseGuard`; delete `IsDetached`; replace 3 callers with `DisplayState == PanelDisplayState.Window`
  - `src/TerminalHost.Core/Services/DirectorySettingsPanelPersistence.cs` — already filters popups; verify window-zone round-trip and add explicit test
- **Deleted**:
  - `src/TerminalHost/Services/PanelWindowManager.cs`
  - `src/TerminalHost/Views/FileViewerWindow.xaml`
  - `src/TerminalHost/Views/FileViewerWindow.xaml.cs`
- **Tests**:
  - `tests/TerminalHost.Tests/Panels/WpfWindowSurfaceTests.cs` — headless-testable surface contract (Mount/Unmount/Focus/IsMounted, displaced-panel dismissal, `IPanelCloseGuard` veto path). FlaUI backstop for dark-mode chrome + actual window lifecycle.
  - `tests/TerminalHost.Tests/Panels/PanelRouterTests.cs` — add window-zone tests: `Move(panel, Window)` round-trips `DisplayState`, dock-back from window via `StateChangeRequested → Move(target)`, persistence excludes popup but includes window entries.
  - `tests/TerminalHost.Tests/Panels/DirectorySettingsPanelPersistenceTests.cs` — extend with explicit window-zone round-trip test (currently only proves popup filtering).

### Out of scope for Phase 2

- `ToastWindow`, `StatusOverlayWindow` migration (Phase 2b — needs synthetic adapter design)
- `MarkdownPreviewWindow` (Avalonia-only — wait for Avalonia surface)
- `SparkCanvasWindow`, `TimelineWindow`, `SetupWindow` — don't route through `PanelWindowManager`; out unless future churn calls them in
- Right-dock surface (Phase 3)
- Center surface (Phase 4) — `IsCenterPanel(panelId)` switch stays in place until then
- Tab-scope persistence (Phase 4 — today's window panels are all AppShell-scoped)

---

*Document version: 1.3 — 2026-05-22 (added Phase 2 design block).*

---

> *Phase 3 design block was not checked in alongside the Phase 3 implementation (`860b3f6`). The Phase 3 outcome is captured in the commit message and the code itself; Phase 4 below references it where load-bearing. A backfill of the Phase 3 design block is tracked separately and is not part of Phase 4 scope.*

---

## ✅ Phase 4 Design — WPF Center Surface

> Outcome of `/design-interface phase 4` on 2026-05-23. Three designs explored in parallel (minimize / pluggable-dock-back / common-case). This is the common-case design (C) with one ingredient from the minimize design (A) and zero from the pluggable design (B). Rationale for the rejections in the trailing block.

### Scope of Phase 4

Land `WpfCenterSurface : IPanelSurface(Center, ForTab(...))` per tab, mirroring the Phase 3 right-dock pattern. Migrate the 12 center-zone panels (UnifiedGit, FileViewer, PrReview, TestResults, SearchAcrossFiles, BranchComparison, MarkdownPreview, RecentFeatures, MergeConflict, FileHistory, FileBlame, DebugLog) to declare `IPanelPlacement.PreferredZone = Center`. Collapse the `_legacyCenterShow` Func bridge, the `[Obsolete] SetOriginZone` API, the `_originZones` dict, the `TryHandleCenterDockBack` path, the `MainWindow.IsCenterPanel(panelId)` closed switch, the `OnCenterPanelRestoreRequested` 90-line switch, `CenterPanelRestoreEventArgs`, and `TabRestoreCoordinator` (purely a center concern). Replace `TerminalPairTabViewModel.PopOutCenterPanel` with the `panel.DetachCommand` flow already wired through Phase 1's `StateChangeRequested` subscription.

**Out of scope:** Avalonia parity, LeftDock (Phase 5), Toast/StatusOverlay synthetic adapters (Phase 2b), Phase 3 design-block backfill (tracked separately).

### Seven locked implementation calls

| # | Choice | Decision | Rationale |
|---|--------|----------|-----------|
| 1 | Where does Center live | **Per-tab `WpfCenterSurface`**, constructed by `TerminalPairTabViewModel` and registered with the router on tab init; unregistered on tab close. Mirrors Phase 3 right-dock exactly. | Consistency across surface migrations. Each tab independently owns its Center slot; no cross-tab coordination. |
| 2 | Single-slot semantics | **Surface enforces single-mount.** `WpfCenterSurface.Mount(vm)` evicts any prior mount (calls its own `Unmount` first) before mounting. Router still enforces single-instance per `(panelId, scope)`. | Mirrors `WpfPopupSurface`. Keeps "Center is one slot" in the surface (platform concern), out of the router (zone-agnostic). |
| 3 | Home-zone declaration | **`IPanelPlacement.PreferredZone = Center` on each of the 12 Center VMs.** `MainWindow.IsCenterPanel(panelId)` switch deletes. The `MainWindow.OnPanelShowRequested` `PanelDisplayState.Panel` branch becomes `_panelRouter.Show(panel)` — placement resolves from `IPanelPlacement`. | One source of truth on the VM, not a closed-list switch in the host. Replaces 12-entry switch with 12 single-line `PreferredZone` returns. |
| 4 | Dock-back from Window | **`Registration.LastDockedZone` auto-tracked.** On every `Move` whose target is a non-Window zone, snapshot the source zone (if not Window) into `LastDockedZone`. On `StateChangeRequested(Panel)` from a Window, resolve target as `existing.LastDockedZone ?? (args.DockSide == Left ? LeftDock : RightDock)`; fall back to `IPanelPlacement.PreferredZone` if neither set. `SetOriginZone` + `_originZones` + `_legacyCenterShow` + `TryHandleCenterDockBack` all delete. | Generalizes the Phase 3 transient bridge into a permanent first-class mechanism. Works for any zone round-trip, not just Center. The `args.DockSide` fallback preserves backwards compatibility for VMs that don't pass through Window first. |
| 5 | Pop-out from Center | **`TerminalPairTabViewModel.PopOutCenterPanel` deletes.** The `← Terminals` header in `TerminalPairView.xaml` adds a pop-out button bound to `Command="{Binding ActiveCenterPanel.DetachCommand}"`. `BasePanelViewModel.DetachCommand` already raises `StateChangeRequested(Window)`; the router's existing handler (PanelRouter.cs:628-635) calls `Move(Window)` — and because of #4, `LastDockedZone=Center` is snapshotted automatically. | Symmetric with right-dock pop-out. Zero special-case code paths. The Phase 3 `SetOriginZone(Center)` hack disappears. |
| 6 | Async data-load hook | **Opt-in `IPanelOpenContext { Task OnOpenedAsync(object? context); }` sibling interface** (from minimize design A). Router invokes `OnOpenedAsync(options.Context)` after every successful `Mount` (and `Move`-completion), if the VM implements it. The 12-case `OnCenterPanelRestoreRequested` switch's per-panel `await vm.OpenAsync(tab)` body migrates here. `SkipDataLoad` becomes a router-controlled flag: skip the `OnOpenedAsync` invocation on `Restore` if the tab isn't selected, fire it on first `Focus` of that tab. | Lets panels react to "I've just been opened with this context" without callers threading context through every site. Replaces both the 90-line switch and the `CenterPanelRestoreEventArgs.SkipDataLoad` flag. |
| 7 | View binding | **`TerminalPairView.xaml` keeps `{Binding ActiveCenterPanel}` and `{Binding IsTerminalsVisible}`** as the binding sites; both become **read-only proxies** on `TerminalPairTabViewModel` derived from `_centerSurface.MountedPanel` (observed via `INotifyPropertyChanged` on the surface, same pattern as Phase 3's `HasMounted` for right-dock). `ShowCenterPanel`/`CloseCenterPanel` setters on the tab VM delete; the surface mutates state via `Mount`/`Unmount` only. | Zero XAML churn. The tab VM's `ActiveCenterPanel` property survives as a read-only view-facing accessor. `IsTerminalsVisible` keeps deriving `=> ActiveCenterPanel == null`. |

### Supporting decisions

- **`tab.ShowCenterPanel(panel)` one-liner** mirrors Phase 3's `tab.ShowRightDockPanel(panel)`. Internally: `_registeredPanels[panel.PanelId] = panel; _router.Show(panel, new PanelShowOptions(Zone: PanelZone.Center, Scope: CenterScope));`. Rolls the `SetPanel + ShowCenterPanel` two-liner from ~30 call sites in `MainWindow.xaml.cs` into one call.
- **`tab.CloseCenterPanel()` becomes** `[RelayCommand] => _router?.CloseZone(PanelZone.Center, CenterScope)`. ESC handler in `MainWindow.xaml.cs:609-612` becomes `_router.CloseZone(Center, currentTab.CenterScope)`.
- **`tab.RestoreCenterPanels()`** mirrors `RestoreRightDockPanels`: `_router.Restore(_centerSurface.Scope, id => _registeredPanels.GetValueOrDefault(id))`. The Center entry from persistence carries `IsActive=true`; the router's `Restore` post-loop Focus call seeds the active panel.
- **Persistence** extends `DirectorySettingsPanelPersistence.LoadTabScope` / `SaveTabScope` to round-trip the Center entry via `DirectorySettings.ActiveCenterPanel` (existing field, single string). Save emits `{panelId, Center, IsOpen=true, IsActive=true}` (single entry per tab scope); load adds it alongside the RightDock entries. RightDock persistence logic untouched.
- **`GitPanelActiveTab` decouples.** Today threaded through `CenterPanelRestoreEventArgs.GitPanelActiveTab` to set `UnifiedGitPanelViewModel.ActiveTab` on restore. Phase 4 moves this to `UnifiedGitPanelViewModel` reading the value directly from `IConfigurationService` on `OnOpenedAsync` (it already has a config dependency). Field stays on `DirectorySettings`; the host stops being a courier.
- **`TabRestoreCoordinator` deletes** (`ITabRestoreCoordinator` + `TabRestoreCoordinator` + `CenterPanelRestoreEventArgs` + the `RestoreRequested` event). The "selected tab last" ordering moves into the tab-restore orchestration point (currently `MainViewModel` / wherever `OpenFolders` is replayed) and is a 5-line loop. `tests/TerminalHost.Tests/Services/TabRestoreCoordinatorTests.cs` deletes; the ordering invariant is covered by a single panel-restore-ordering test on the orchestration site.
- **DI registration** in `App.xaml.cs`: remove the `legacyCenterShow:` lambda from the `PanelRouter` ctor call (and remove the parameter from `PanelRouter`'s ctor). No new DI entries — `WpfCenterSurface` is constructed per-tab inside `TerminalPairTabViewModel.InitializeCenterSurface`, identical to right-dock.
- **`MainWindow.xaml.cs` cleanup**: `OnPanelShowRequested`'s `IsCenterPanel(panel)` branch collapses into `currentTab.ShowCenterPanel(panel)` (router resolves zone from `IPanelPlacement.PreferredZone`). `ShowCenterPanelInTab` deletes. The 30-ish `currentTab.SetPanel(vm); currentTab.ShowCenterPanel(vm);` two-liners collapse to one line each. `OnCenterPanelRestoreRequested` deletes entirely.
- **Race protection from C's trade-off note**: `RestoreCenterPanels` runs after `RestoreRightDockPanels` and after `_registeredPanels` is fully populated. Document the call order in `TerminalPairTabViewModel.InitializePanelSystem` and assert it.

### Designs considered and rejected for Phase 4

- **Pluggable-dock-back design (B): open `PanelZone` enum + `IPanelPlacementPolicy` strategy port + `PlacementIntent` enum + `PanelStateChangeRequestedEventArgs.TargetZone`.** Rejected as the Phase 0 Design B failure mode reincarnated. The policy IS three lines (`Requested ?? LastDockedZone ?? PreferredZone ?? RightDock`) — wrapping that in a port is registry-of-registries. The closed `PanelZone` enum was a deliberate Phase 0 decision; adding a sixth zone costs one enum value + one surface impl, which is cheaper than the open-enum tax (loses exhaustive switch, typo-friendly, harder to refactor). `PlacementIntent` tracks state the router does not need to know — `LastDockedZone` alone is sufficient.
- **Single `Open(vm, context)` entry point (minimize design A).** Rejected as the *primary* shape — breaks Phase 3 symmetry (`tab.ShowCenterPanel` mirrors `tab.ShowRightDockPanel`), loses caller control for unusual placements, and the `IActiveTabScopeProvider` it relies on is a hidden dependency that's worse than the explicit-scope pass-through. Kept its `IPanelOpenContext` opt-in sibling interface (Locked Call #6) because that ingredient is a strict improvement over routing `Context` via the bare `PanelShowOptions.Context` field.
- **Persist `LastDockedZone` across panel close/reopen.** Rejected — over-engineering for the one-window-roundtrip case. If a user closes a windowed center panel and reopens via Ctrl+O, "treat as fresh open and resolve via `PreferredZone`" is correct.
- **Keep `PopOutCenterPanel` as a relay command on the tab VM.** Rejected — `panel.DetachCommand` already does the job and avoids a parallel-command duplicate. The XAML `← Terminals` chrome adds a pop-out button bound to `DetachCommand`; the relay command goes away.
- **Move `GitPanelActiveTab` into `PanelShowOptions.Context`.** Rejected — `Context` is for parameterized opens (file path, branch name), not for VM-owned persistence state. The VM should read its own config; the host should not be a courier for VM state.
- **Centralize tab-restore ordering via a new `IPanelRestoreOrchestrator` port.** Rejected — the ordering is a 5-line loop with one observable invariant (selected-tab-last). A new port for one consumer with one invariant is over-abstraction; inline at the call site.
- **Make `WpfCenterSurface` stack panels (tabbed center).** Rejected as out of scope — the existing UX is exclusive. If a future feature wants stacked Center, surface changes; router doesn't.

### Files this phase will touch

- **New**:
  - `src/TerminalHost/TerminalHost/Services/Panels/WpfCenterSurface.cs`
  - `src/TerminalHost.Core/Interfaces/IPanelOpenContext.cs`
- **Modified**:
  - `src/TerminalHost.Core/Interfaces/IPanelRouter.cs` — remove `SetOriginZone`; document `LastDockedZone` semantics
  - `src/TerminalHost.Core/Services/PanelRouter.cs` — delete `_legacyCenterShow` ctor param, `_originZones`, `SetOriginZone`, `TryHandleCenterDockBack`, the call from `Move`; add `Registration.LastDockedZone` field auto-tracked in `MoveCore`; extend `SubscribeStateChanges` handler to resolve dock-back zone via `LastDockedZone ?? DockSide-fallback`; invoke `IPanelOpenContext.OnOpenedAsync` post-mount
  - `src/TerminalHost.Core/Services/DirectorySettingsPanelPersistence.cs` — extend tab-scope `Load`/`Save` to round-trip Center via `DirectorySettings.ActiveCenterPanel`
  - `src/TerminalHost/TerminalHost/App.xaml.cs` — remove `legacyCenterShow:` lambda
  - `src/TerminalHost/TerminalHost/MainWindow.xaml.cs` — delete `IsCenterPanel` switch, `ShowCenterPanelInTab`, `OnCenterPanelRestoreRequested`, `OnCenterPanelRestoreRequested` subscription. Replace 30-ish `SetPanel + ShowCenterPanel` two-liners with `tab.ShowCenterPanel(vm)`. ESC handler routes through `_router.CloseZone(Center, ...)`.
  - `src/TerminalHost/TerminalHost/ViewModels/TerminalPairTabViewModel.cs` — delete `PopOutCenterPanel[Command]`, `ToggleCenterPanel`. Add `InitializeCenterSurface`, `CenterScope`, `AttachCenter`, `RestoreCenterPanels`, `ShowCenterPanel`, `CloseCenterPanel` (RelayCommand). Convert `ActiveCenterPanel` / `IsTerminalsVisible` to read-only proxies over `_centerSurface.MountedPanel`. Update `Cleanup` to unregister Center surface. Remove `target.ActiveCenterPanel = …` + `GitPanelActiveTab = …` writes from `WriteToDirectorySettings` (persistence now owns them).
  - `src/TerminalHost/TerminalHost/Views/Tabs/TerminalPairView.xaml` — add pop-out button in `← Terminals` header bound to `ActiveCenterPanel.DetachCommand`; call `tab.AttachCenter(...)` from view's `OnLoaded`.
  - 12 center-panel VMs — implement `IPanelPlacement` with `PreferredZone = PanelZone.Center` and `PreferredScope = …` (scope passed explicitly by callers via the wrapper; placement default declares the zone). `UnifiedGitPanelViewModel` additionally implements `IPanelOpenContext.OnOpenedAsync` to load `GitPanelActiveTab` from config and trigger initial data load.
  - `src/TerminalHost.Avalonia/MainWindow.axaml.cs`, `src/TerminalHost.Avalonia/ViewModels/MainViewModel.cs` — Avalonia is out of scope for Phase 4, but the shared `CenterPanelRestoreEventArgs` deletion means Avalonia's subscriptions delete too. Avalonia's restore path becomes a no-op until Phase 5/6 brings it under the router.
- **Deleted**:
  - `src/TerminalHost.Core/Domain/CenterPanelRestoreEventArgs.cs`
  - `src/TerminalHost.Core/Services/TabRestoreCoordinator.cs`
  - `src/TerminalHost.Core/Interfaces/ITabRestoreCoordinator.cs`
  - `tests/TerminalHost.Tests/Services/TabRestoreCoordinatorTests.cs`
- **Tests**:
  - `tests/TerminalHost.Tests/Panels/WpfCenterSurfaceTests.cs` — headless surface contract: Mount evicts prior, Unmount clears `MountedPanel`, `HasMounted` / `MountedPanel` raise `PropertyChanged`, `Focus` is a no-op on single-slot.
  - `tests/TerminalHost.Tests/Panels/PanelRouterTests.cs` — add: `LastDockedZone` snapshot on every Move-to-non-Window; Center→Window→Center round-trip preserves zone via `LastDockedZone`; RightDock→Window→RightDock round-trip preserves zone via `LastDockedZone`; `IPanelOpenContext.OnOpenedAsync` fired post-Mount and post-Move; not fired on `Restore` when `SkipDataLoad`-equivalent (tab not selected). Delete the `_legacyCenterShow` + `SetOriginZone` tests added in Phase 3.
  - `tests/TerminalHost.Tests/Panels/DirectorySettingsPanelPersistenceTests.cs` — add: tab-scope round-trip of Center entry via `DirectorySettings.ActiveCenterPanel`; combined Center + RightDock round-trip on a single tab.
  - FlaUI smoke test backstop: open File Viewer via Ctrl+O, pop out to Window, dock back — lands on Center.

### Out of scope for Phase 4

- Avalonia parity (deferred until WPF surfaces fully settle)
- LeftDock migration (Phase 5)
- ToastWindow / StatusOverlayWindow synthetic adapters (Phase 2b)
- `SparkCanvasWindow`, `TimelineWindow`, `SetupWindow` — bespoke windows that don't route through the router; out unless future churn pulls them in
- Phase 3 design block backfill — captured in commit `860b3f6`; spec backfill is a doc-only task tracked separately

---

*Document version: 1.4 — 2026-05-23 (added Phase 4 design block).*

---

## ✅ Phase 5 Design — Avalonia Router Migration (staged)

> Outcome of `/design-interface next phase` on 2026-05-24. Four designs explored in parallel — A (WPF mirror), B (Avalonia-native), C (staged sub-phases), D (Avalonia.Headless tests). This is a hybrid of C's chassis, B's overlay choice, D's test infrastructure, and A's class signatures where the WPF mirror is uncontroversial.

### Scope of Phase 5

Bring Avalonia under the `IPanelRouter` that WPF migrated to over Phases 1–4. Today Avalonia has **zero** references to `IPanelRouter` / `IPanelSurface` / `PanelScope` / `PanelZone`; ~22 `IsXxxOpen` booleans drive a `PopupHost` overlay panel with ~22 visibility-bound children; a 22-entry ESC cascade lives at `MainWindow.axaml.cs:1017-1050`; `TerminalPairTabViewModel.ShowCenterPanel`/`CloseCenterPanel`/`ToggleCenterPanel` (lines 1782-1818) still mutate `ActiveCenterPanel` directly; and a tab-switch rebind hack at `MainWindow.axaml.cs:595-606` is now bit-rotted (its `// Note: Other center panel types are not yet implemented as IPanelableViewModel in Avalonia` comment is obsolete since Phase 4 deleted `CenterPanelRestoreEventArgs`).

After Phase 5: Avalonia's `MainWindow.axaml.cs` is structurally identical to WPF's post-Phase-4 shape for routed panels (~900 lines, down from 1760), the `IsXxxOpen` boolean zoo is gone, ESC routes through `_router.CloseZone`, and the same `IPanelOpenContext.OnOpenedAsync` mechanism the WPF center VMs adopted in Phase 4 carries the tab-rebind concern.

**Out of scope for Phase 5:**
- **Right-dock surface for Avalonia** — Avalonia has no right-dock today; adding one is *new-feature work*, not router parity. Tracked as Phase 6 (additive, not blocking).
- `ToastWindow` / `StatusOverlayWindow` / `SparkCanvasWindow` / `SetupWindow` — they don't host `IPanelableViewModel`s; Phase 2b territory.
- `MarkdownPreviewWindow` (Avalonia-only bespoke window) — survives Phase 5 unless `MarkdownPreviewViewModel` already inherits `BasePanelViewModel`, in which case it collapses into `AvaloniaWindowSurface` for free.
- Phase 3 design-block backfill — tracked separately.

### Sub-phase chassis

| Sub-phase | Lands | Deletes | Verification |
|---|---|---|---|
| **5a** — DI bootstrap + Overlay surface + test project | `IPanelRouter` / `IPanelPersistence` wired in `App.axaml.cs`. `AvaloniaOverlaySurface` (single `ContentControl` mount inside the existing `PopupHost` `Panel` — **not** Avalonia `Popup`). Route 4 transient popups (Help, Command Palette, Tab Switcher, Tab Dropdown). New `tests/TerminalHost.Avalonia.Tests/` project (Avalonia.Headless.XUnit) with the first surface-contract test. | `IsHelpOpen`, `IsTabSwitcherOpen`, `IsTabDropdownOpen`, `Palette.IsOpen` + their bindings + 4 overlay panel children. | Build, existing tests, new `AvaloniaOverlaySurfaceTests` headless. |
| **5b** — Window surface + bespoke window collapse | `AvaloniaWindowSurface : IPanelSurface(Window, AppShell)` mirroring `WpfWindowSurface`. Delete `Views/FileViewerWindow.axaml(.cs)` (adopt `IPanelCloseGuard` on `FileViewerViewModel` if not already). `MarkdownPreviewWindow` collapses too **iff** its VM implements `IPanelableViewModel`; otherwise it stays. | `FileViewerWindow.axaml(.cs)`, `CreatePopOutWindow` stub, the 8 bespoke-window call sites. | Build, smoke: file viewer pop-out and dock-back. |
| **5c** — Remaining overlay popups + ESC cascade | Route the remaining 11+ popup VMs (ScratchPad, FileViewer-as-popup, DetectedLinks, TaskPanel, ClaudeTasksPanel, MemoryBrowser, DebugLog, SearchAcrossFiles, FileHistory, FileBlame, Reflog, ManageWorktrees, PrReview, RecentFeatures, MergeConflict) through `AvaloniaOverlaySurface`. Replace the 22-entry ESC array with `_router.CloseZone(PanelZone.Popup, PanelScope.AppShell)` then (if a tab is selected) `_router.CloseZone(PanelZone.Center, currentTab.CenterScope)`. | Remaining `IsXxxOpen` bools, 22-entry ESC array in `MainWindow.axaml.cs:1017-1050`, `CloseAllPopups()`. | Build, FlaUI ESC priority smoke. |
| **5d** — Center surface + per-tab scope + tab-rebind hack deletion | `AvaloniaCenterSurface : IPanelSurface(Center, ForTab(...))` per tab, mirroring Phase 4 WPF. Convert `ActiveCenterPanel` / `IsTerminalsVisible` to read-only proxies over `_centerSurface.MountedPanel`. Delete `ShowCenterPanel` / `CloseCenterPanel` / `ToggleCenterPanel`. Replace the ~9 `terminalTab.ShowCenterPanel(_unifiedGitPanelViewModel); _ = vm.OpenOnTabAsync(...)` two-liners with one router call + `IPanelOpenContext`. **Delete the tab-rebind hack at `MainWindow.axaml.cs:595-606`** — replaced by the same `OnOpenedAsync(this)` mechanism WPF Phase 4 adopted. Remove `target.ActiveCenterPanel = …` from `WriteToDirectorySettings` (persistence owns it now). | `ShowCenterPanel` / `CloseCenterPanel` / `ToggleCenterPanel`, the ~9 dispatch sites, the rebind hack, `IsExplorerVisible` mutator (becomes derived from the dock surface only if right-dock lands — until then `IsExplorerVisible` keeps its current mutator on Avalonia, **flagged as Phase 6 cleanup**). | Build, tests, smoke: Cmd+B opens git on Branches; switching tabs preserves correct VM state via `OnOpenedAsync`. |

5a is intentionally small — it bootstraps DI, proves the surface skeleton, and stands up the headless test project so subsequent sub-phases grow into it. 5c is largest by volume but lowest by risk. 5d is highest-risk because the tab-rebind hack must die cleanly.

### Locked implementation calls

| # | Choice | Decision | Rationale |
|---|--------|----------|-----------|
| 1 | Overlay mount primitive | **`ContentControl` inside the existing `PopupHost` `Panel`** — **not** Avalonia `Popup` | Avalonia's `PopupRoot` is a separate visual tree that doesn't inherit `App.axaml` brushes cleanly. The existing `PopupHost` `Panel` already z-orders above tabs; collapsing 22 visibility-bound children to one `ContentPresenter` with DataTemplate dispatch matches the established Avalonia idiom and avoids airspace issues. |
| 2 | View resolution | **Implicit `DataTemplate` keyed by VM type** in `App.axaml` `Application.Resources` | Matches existing Avalonia DataTemplate convention. No new port. |
| 3 | Single-instance / single-mount semantics | Surface enforces single-slot (single mounted VM at a time). Router enforces single-instance per `(panelId, scope)`. | Mirrors WPF Phase 1+4 locked call. |
| 4 | Click-outside dismiss | Wire `PanelMountOptions.DismissOnClickOutside` to Avalonia's **`LightDismissOverlayBehavior`** when the overlay surface mounts. Surface raises `DismissRequested(ClickOutside)` from the behavior's dismiss event. | Native Avalonia idiom; testable in `Avalonia.Headless` (where WPF cannot test click-outside without FlaUI). The one Avalonia-side ergonomic win worth keeping. |
| 5 | ESC handling | Per-VM `OnKeyDown` handlers stay (matches WPF Phase 1). Global ESC in `MainWindow.OnKeyDown` calls `_router.CloseZone(Popup, AppShell)` then `_router.CloseZone(Center, currentTab.CenterScope)` — replaces the 22-entry array. | Two-line replacement for the cascade; ordering preserved (popup wins over center, matching today's priority). |
| 6 | Persistence | **Reuse `Core/Services/DirectorySettingsPanelPersistence` unchanged** | It's already platform-agnostic Core; projects through `IConfigurationService` which Avalonia already has. Zero schema drift. |
| 7 | Tab-rebind hack at `MainWindow.axaml.cs:595-606` | **Delete in 5d.** Replaced by the same `IPanelOpenContext.OnOpenedAsync(this)` mechanism WPF Phase 4 adopted; `UnifiedGitPanelViewModel` already implements it. | No clone of WPF's `HydrateActiveCenterPanelAsync` is needed — the router invokes `OnOpenedAsync` post-Mount and post-Move (`PanelRouter.cs:574-588`), so tab-switch re-binding is centralized. |
| 8 | Avalonia.Headless test scaffold | **Land the test project in 5a**, not as a separate phase. Grows per sub-phase. | Sub-phases without tests are weaker. Adding tests later is the well-known "we'll backfill" anti-pattern. |
| 9 | Right-dock (Avalonia first dock surface) | **Out of scope for Phase 5; tracked as Phase 6.** | Avalonia has no right-dock today. Adding one is feature work, not router parity. Phase 5's success criterion is "Avalonia routed-panel behavior matches WPF" — that's preserved even without a dock surface because Avalonia currently has nothing in the dock zone. |
| 10 | `MarkdownPreviewWindow` | Collapses into `AvaloniaWindowSurface` in 5b **iff** `MarkdownPreviewViewModel` already inherits `BasePanelViewModel`; otherwise stays bespoke in Phase 5 and rides Phase 6 cleanup. | Don't bundle a VM migration into a host migration; the spec already established that Avalonia-bespoke windows survive when their VMs aren't `IPanelableViewModel`s. |

### Supporting decisions

- **Per-tab surfaces** (Center) are constructed by `TerminalPairTabViewModel` and registered/unregistered with the router on tab init/close — identical pattern to WPF Phase 3+4.
- **AppShell surfaces** (Overlay, Window) are DI singletons; their `AttachHost(...)` method binds the actual XAML mount point after `MainWindow.Opened` (mirrors the lazy-attach pattern from `WpfPopupSurface`).
- **DI registration order** matches WPF Phase 1: register `IPanelPersistence`, `Func<Type, IPanelableViewModel?>` factory, then `IPanelSurface` enumeration, then `IPanelRouter` ctor consumes them. New: register the same `IDispatcherService` instance both as Avalonia's existing port and as `IUiDispatcher` (one-line adapter if signatures don't match).
- **Phase 5a deletes 4 bools, 5c deletes ~11 more + the ESC array, 5d deletes the rebind hack** — positive momentum every sub-phase. No sub-phase lands sideways.
- **Between 5a and 5c**, Avalonia briefly has TWO popup mechanisms running side-by-side (router-driven for 4 VMs; `IsXxxOpen`-bound for ~18). Document this transitional state with a `// PHASE-5C-PENDING:` comment on the `IsXxxOpen` properties scheduled for migration.
- **Between 5b and 5d**, the tab-rebind hack at lines 595-606 stays in place. Reviewers should not be asked to delete it incrementally — its full elimination is a 5d acceptance criterion, not a 5b/5c side-effect.
- **Sub-phase landing cadence**: 5a + 5b can ship in one PR if reviewer bandwidth permits (5b is small, gives 5d's pop-out a real target). 5c standalone. 5d standalone, gated on 5a+5b being live.

### Designs considered and rejected for Phase 5

- **Design A as primary (WPF mirror, Avalonia `Popup`).** Rejected as primary — Avalonia `Popup` fights `App.axaml` brush inheritance (separate `PopupRoot` visual tree). Kept A's class signatures (`AvaloniaWindowSurface`, `AvaloniaCenterSurface`) for the surfaces where the WPF mirror is uncontroversial.
- **Design B's full Avalonia-native rethink.** Rejected as the *primary* framing — Window/Center are uncontroversial mirrors; rebadging them as "native" adds nothing. Kept B's overlay choice (`Panel` + `ContentControl`) and its `LightDismissOverlayBehavior` wiring because those are genuine Avalonia ergonomic wins.
- **Big-bang Phase 5 (one PR).** Rejected on conflict-surface grounds — concurrent Spark Canvas / Eidet work would collide with a ~1800-line PR. Staged is 4 PRs of ~500 lines, each independently reviewable.
- **Bundling right-dock (Avalonia's first dock surface) into Phase 5.** Rejected — that's feature work, not parity. Phase 5 ships when Avalonia's routed-panel behavior matches WPF; right-dock is Phase 6 and explicitly additive.
- **Design D's hypothetical additive ports (`SynthesizeDismiss(DismissTrigger)` test seam).** Rejected — D itself rejected them. Real `KeyDown(Escape)` against a real `Window` in `Avalonia.Headless` is more faithful than a test-only seam. Adopted D's stance unchanged.
- **Deferring the Avalonia.Headless test project to "after parity lands".** Rejected — that's the classic backfill-debt anti-pattern. The test project goes in 5a and grows with each sub-phase.
- **Keeping the tab-rebind hack at `MainWindow.axaml.cs:595-606` indefinitely.** Rejected — it's the visible scar of Phase 4 and accretes one new entry per migrated center VM. Must die in 5d; non-negotiable.
- **Adding a new `IPanelSurface` method or sibling port for Avalonia.** Rejected — Phase 5 is implementation work against the locked Phase 0 contracts. If the existing contract can't accommodate Avalonia, that's a Phase 0 bug, not a Phase 5 design.

### Dependency strategy

| External touch | Port | Avalonia adapter | WPF counterpart | Test adapter |
|---|---|---|---|---|
| Overlay mount (`PopupHost` Panel) | `IPanelSurface(Popup, AppShell)` | `AvaloniaOverlaySurface` (uses `ContentControl` inside existing `Panel`; `LightDismissOverlayBehavior` for click-outside) | `WpfPopupSurface` | `FakePanelSurface` (router tests); real headless instance (surface tests) |
| Top-level `Window` | `IPanelSurface(Window, AppShell)` | `AvaloniaWindowSurface` (wraps `Views/PanelWindow.axaml`; `Topmost` for `AlwaysOnTop`) | `WpfWindowSurface` | `FakePanelSurface`; real headless instance |
| Per-tab center slot | `IPanelSurface(Center, ForTab(...))` | `AvaloniaCenterSurface` (single-slot, `ContentControl` binding) | `WpfCenterSurface` | `FakePanelSurface`; real headless instance |
| Per-tab right dock | — | **Out of scope (Phase 6)** | `WpfRightDockSurface` | — |
| UI thread | `IUiDispatcher` | Existing `Services/DispatcherService.cs` (wraps `Avalonia.Threading.Dispatcher.UIThread`) | `WpfDispatcher` | `SyncDispatcher` |
| Per-directory layout state | `IPanelPersistence` | **`DirectorySettingsPanelPersistence` reused unchanged** (Core, platform-agnostic) | same | `InMemoryPanelPersistence` |

All in-process. No new ports.

### Test strategy

**New project: `tests/TerminalHost.Avalonia.Tests/`** — references `Avalonia.Headless.XUnit 11.3.16` (matching app version). One file per surface with contract tests against a real Avalonia visual tree:

- `AvaloniaOverlaySurfaceTests.cs` — Mount evicts prior, ESC routes through real focus chain raises `DismissRequested(Escape)`, click-outside (via `LightDismissOverlayBehavior`) raises `DismissRequested(ClickOutside)`, focus restoration on Unmount.
- `AvaloniaWindowSurfaceTests.cs` — Mount creates `PanelWindow`, `IPanelCloseGuard.CanClose() = false` cancels real `WindowClosingEventArgs`, `Topmost` flag wires through, dock-back via `vm.IsOpen = false`.
- `AvaloniaCenterSurfaceTests.cs` — Single-slot eviction on Mount, `MountedPanel` / `HasMounted` raise `PropertyChanged`, `Focus` is no-op.

**Shared `PanelRouterTests` in `TerminalHost.Tests`** stays — Avalonia is not loaded for router-boundary tests. `FakePanelSurface` remains for those.

**FlaUI backstop** for what headless can't reach: macOS dark-mode title-bar chrome, OS focus stealing on AlwaysOnTop windows, system tray.

**Test build cost:** ~3 packages added (`Avalonia.Headless`, `Avalonia.Headless.XUnit`), ~15MB restore, tests run ~200ms each on Linux CI (10× faster than FlaUI smoke).

**Honest caveat:** WPF surfaces gain no new test fidelity from this — `Microsoft.UI.Xaml.Testing` doesn't cover WPF. Avalonia gets tighter coverage than WPF. That's an asymmetry in Avalonia's favor, not a regression.

### Files this phase will touch

**5a — bootstrap:**
- *New*: `src/TerminalHost.Avalonia/Services/Panels/AvaloniaOverlaySurface.cs`; `tests/TerminalHost.Avalonia.Tests/TerminalHost.Avalonia.Tests.csproj` + `HeadlessAppBuilder.cs` + `Panels/AvaloniaOverlaySurfaceTests.cs`.
- *Modified*: `App.axaml.cs` (DI: `IPanelRouter`, `IPanelPersistence`, `AvaloniaOverlaySurface`, factory lambda); `MainWindow.axaml` (collapse 4 overlay `Panel` children → one `<ContentControl x:Name="OverlayMount"/>`); `MainWindow.axaml.cs` (`OnOpened` calls `_overlaySurface.AttachHost(PopupHost, OverlayMount)`; 4 popup keybindings route through `_router.Show<TPanel>()`); `ViewModels/MainViewModel.cs` (delete `IsHelpOpen`, `IsTabSwitcherOpen`, `IsTabDropdownOpen`, `Palette.IsOpen` props + handlers).

**5b — window surface:**
- *New*: `src/TerminalHost.Avalonia/Services/Panels/AvaloniaWindowSurface.cs`.
- *Modified*: `App.axaml.cs` (DI append); `MainWindow.axaml.cs` (`OnFilePopOutRequested` + `OnFileViewerDetachRequested` route through `_router.Move(panelId, Window)`); `ViewModels/FileViewerViewModel.cs` (implement `IPanelCloseGuard` if not already; remove `IsDetached` if present); `Views/PanelWindow.axaml.cs` (`OnClosing` veto via `IPanelCloseGuard`).
- *Deleted*: `Views/FileViewerWindow.axaml(.cs)`. `MarkdownPreviewWindow.axaml(.cs)` *iff* its VM is `IPanelableViewModel`; otherwise stays.
- *New tests*: `AvaloniaWindowSurfaceTests.cs`.

**5c — remaining overlay popups + ESC:**
- *Modified*: `MainWindow.axaml` (collapse remaining 11+ overlay panel children); `MainWindow.axaml.cs` (replace 22-entry ESC cascade lines 1017-1050 with two `_router.CloseZone` calls; delete `CloseAllPopups()`); `ViewModels/MainViewModel.cs` (delete remaining `IsXxxOpen` props); the 11+ popup VMs adopt `BasePanelViewModel` if not already (most already do — verify).

**5d — center surface + tab-rebind hack deletion:**
- *New*: `src/TerminalHost.Avalonia/Services/Panels/AvaloniaCenterSurface.cs`.
- *Modified*: `ViewModels/TerminalPairTabViewModel.cs` (add `InitializeCenterSurface`, `CenterScope`, `AttachCenter`, `RestoreCenterPanels`; delete `ShowCenterPanel` / `CloseCenterPanel` / `ToggleCenterPanel`; convert `ActiveCenterPanel` / `IsTerminalsVisible` to read-only proxies over `_centerSurface.MountedPanel`; update `Cleanup` to unregister; remove `target.ActiveCenterPanel = …` writes from `WriteToDirectorySettings`); `MainWindow.axaml.cs` (delete the tab-rebind hack at lines 595-606; replace 9 git-tab dispatch two-liners with one router call each; ESC cascade in 5c gains the Center cascade arm); `Views/Tabs/TerminalPairView.axaml(.cs)` (`OnLoaded` calls `tab.AttachCenter(CenterContentControl)`); the 9+ Avalonia center VMs implement `IPanelPlacement.PreferredZone = Center` if not already (most already do — verify).
- *New tests*: `AvaloniaCenterSurfaceTests.cs`; extend `PanelRouterTests` if anything Avalonia-specific surfaces.

### Out of scope for Phase 5

- **Avalonia right-dock** (Phase 6 — new feature work)
- `ToastWindow`, `StatusOverlayWindow`, `SparkCanvasWindow`, `SetupWindow` (Phase 2b synthetic-adapter design)
- `MarkdownPreviewWindow` if its VM is not `IPanelableViewModel` (5b touches it or skips it depending on VM state at landing time)
- `WpfRightDockSurface` / `WpfCenterSurface` / `WpfPopupSurface` / `WpfWindowSurface` — untouched; WPF must stay green at every sub-phase boundary
- Phase 3 design-block backfill — doc-only task tracked separately

---

*Document version: 1.5 — 2026-05-24 (added Phase 5 design block).*
