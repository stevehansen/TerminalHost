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
