# Avalonia Port Parity

> Bring the macOS (Avalonia) app up to date with recent WPF features: Hook Debug Dialog, Container Settings UI updates, and Spark Canvas visualization.

## Status: Phase 3d Complete (2026-03-29)

**Depends on**: SparkCanvas.md, ContainerizedWorkspaces.md, SessionLifecycle.md

### Scope

This spec covers porting three categories of features that exist in WPF but are missing or incomplete in Avalonia, identified from commits `324e13f` through `4be2b3f`:

| Feature | WPF Status | Avalonia Status | Effort |
|---------|-----------|----------------|--------|
| Hook Debug Dialog | Complete | Missing | Small |
| Container Settings UI parity | Complete | Minor gaps | Small |
| Spark Canvas (Phase 3a-3d) | Complete | Missing entirely | Large |

---

## Phase 1 — Hook Debug Dialog (Small)

> Priority: High | Effort: ~1 day

The WPF app has a Hook Debug Dialog (`HookDebugDialog.xaml`) accessible via command palette that displays `HookDebugEntry` records for troubleshooting webhook/hook execution. The data model (`HookDebugEntry`) is already in Core.

### What exists in WPF
- `Views/Dialogs/HookDebugDialog.xaml` + code-behind
- Command palette entry "Hook: Debug Log" in `MainViewModel.cs`
- Shows timestamped list of hook events with request/response details

### What needs to happen in Avalonia
- [ ] Create `Views/Dialogs/HookDebugDialog.axaml` + code-behind
  - Avalonia dialog following existing `NotificationDialog`/`InputDialog` patterns
  - DataGrid or ItemsControl showing `HookDebugEntry` list
  - Timestamp, hook name, status, request body, response body columns
  - Copy-to-clipboard button per entry
  - Close button
- [ ] Add command palette entry in Avalonia `MainViewModel.cs`
  - "Hook: Debug Log" with same keyboard shortcut (if assigned in WPF)
  - Wire to open the dialog, passing hook debug entries from `ApiServer`

### Acceptance criteria
- Command palette shows "Hook: Debug Log"
- Dialog opens, displays hook execution history
- Entries show timestamp, hook name, status, and expandable request/response

---

## Phase 2 — Container Settings UI Parity (Small)

> Priority: Medium | Effort: ~0.5 day

Both WPF and Avalonia expose the same `ContainerSettings` properties. The differences are minor binding behaviors, not missing fields. The bwrap/user namespace logic is hardcoded in `ContainerService.cs` (Core) and not exposed as UI settings in either platform.

### What needs to happen in Avalonia
- [ ] Add `UpdateSourceTrigger=PropertyChanged` equivalent to Avalonia TextBox bindings
  - Avalonia uses `UpdateSourceTrigger` on `Binding` or two-way mode by default
  - Verify that all container text fields (DockerPath, ImageName, ImageTag, NetworkMode, reference volume paths) update the ViewModel on each keystroke, not just on lost focus
  - If any field uses `Default` binding and needs immediate updates, switch to explicit `Mode=TwoWay` with appropriate trigger
- [ ] Verify "Recreate" button parity for stale containers
  - WPF shows a conditional "Recreate" button per container when config staleness is detected
  - Avalonia shows "Stop" and "Remove" — verify if "Recreate" logic is missing or handled differently
- [ ] Test container lifecycle operations match WPF behavior

### Acceptance criteria
- Text field changes propagate immediately (no need to tab out)
- Container list shows recreate option when config is stale
- All container CRUD operations work identically to WPF

---

## Phase 3 — Spark Canvas for Avalonia/macOS (Large)

> Priority: Medium | Effort: ~3-5 days

The Spark Canvas is a WebView2-hosted force-directed graph visualization. The WPF version uses `Microsoft.Web.WebView2.Wpf` which is Windows-only. Porting to Avalonia requires selecting a macOS-compatible WebView control and wiring up the same JS assets and C#-to-JS bridge.

### Architecture Decision: WebView Control (Resolved)

**Options evaluated:**

| Option | Package | Pros | Cons |
|--------|---------|------|------|
| **A. Avalonia.Controls.WebView** | Official Avalonia (11.3.x) | Native WKWebView, official API | Requires paid Accelerate license |
| **B. WebView.Avalonia.Cross** | Community MIT (11.3.1) | WKWebView on macOS, MIT licensed, WebView2 API compat | Community-maintained |
| **C. CefGlue** | `AvaloniaCefGlue` | Full Chromium | Heavy (~100MB), overkill |

**Chosen: Option B (`WebView.Avalonia.Cross` 11.3.1)** — MIT licensed, uses native WKWebView on macOS, API compatible with WebView2 patterns (`PostWebMessageAsString`, `WebMessageReceived`, `ExecuteScriptAsync`). No Avalonia version upgrade needed.

**Note:** Option A (`Avalonia.Controls.WebView`) was attempted first but requires an Avalonia Accelerate license at v11.3.x. The MIT release was announced for v11.4.0 which does not exist yet.

### Phase 3a — WebView Integration Spike (Complete)

- [x] Add `WebView.Avalonia.Cross` 11.3.1 + `WebView.Avalonia.Desktop.Cross` 11.3.1 to csproj
- [x] Register `UseDesktopWebView()` in `Program.cs` and `AvaloniaWebViewBuilder.Initialize` in `App.axaml.cs`
- [x] Create `SparkCanvasView.axaml` with `AvaloniaWebView.WebView` control
- [x] Local file loading: `SparkWebView.Url = new Uri($"file://{indexPath}")` — works
- [x] C#→JS messaging: `SparkWebView.PostWebMessageAsString(json, null)` (sync, returns `bool`)
- [x] JS→C# messaging: `WebMessageReceived` event with `WebViewMessageReceivedEventArgs.Message`
- [x] Web assets linked from WPF project via `<Content Include="..\..\src\TerminalHost\...\web\spark\**\*" Link="web\spark\...">` — copied to output
- [x] Update JS bridge (`events.js`): `notifyHost` now tries `invokeCSharpAction` as fallback for Avalonia
- [x] Create `SparkCanvasViewModel.cs` (ported from WPF, identical logic)
- [x] Create `SparkCanvasWindow.axaml` (pop-out window)
- [x] Command palette entry "Spark: Open Canvas" added

**API differences from WebView2:**

| Aspect | WebView2 (WPF) | WebView.Avalonia.Cross (macOS) |
|--------|----------------|-------------------------------|
| Element | `wv2:WebView2` | `wv:WebView` (xmlns `AvaloniaWebView`) |
| C#→JS | `PostWebMessageAsString(json)` | `PostWebMessageAsString(json, baseUri)` (sync) |
| JS→C# | `window.chrome.webview.postMessage(msg)` | Same (library abstracts it) |
| Local files | Virtual host mapping | `file://` URI |
| Init | `EnsureCoreWebView2Async()` | `UseDesktopWebView()` + `AvaloniaWebViewBuilder.Initialize()` |
| Navigation event | `NavigationCompleted` | `NavigationCompleted` (args: `WebViewUrlLoadedEventArg`) |
| Message event | `WebMessageReceived` (`.TryGetWebMessageAsString()`) | `WebMessageReceived` (`.Message` property) |
| Namespaces | `Microsoft.Web.WebView2.Core` | `WebViewCore.Events`, `AvaloniaWebView` |

### Phase 3b — SessionActivityService + TranscriptWatcher

> Effort: ~1 day

The Spark Canvas depends on live session activity data. The Avalonia `SessionActivityService` is currently a stub (all no-ops). `TranscriptWatcher` is missing entirely.

- [ ] Port `SessionActivityService` from WPF to Avalonia
  - The Core interface `ISessionActivityService` is already defined
  - WPF implementation in `src/TerminalHost.Windows/Services/` or `src/TerminalHost/TerminalHost/Services/`
  - Implement activity event processing, lifecycle state tracking
  - Wire `ActivityEventProcessed` event to `EventAggregator` (for SSE pipeline)
- [ ] Port `TranscriptWatcher` to Avalonia
  - Core interface `ITranscriptWatcher` already defined
  - Watches Claude Code JSONL transcript files for new entries
  - Parses transcript entries via `TranscriptParserService` (already in Core)
  - Emits events consumed by `SessionActivityService`
- [ ] Register both services in Avalonia DI container (`App.axaml.cs`)
- [ ] Verify SSE event pipeline works end-to-end:
  - TranscriptWatcher → SessionActivityService → EventAggregator → ApiServer SSE

### Phase 3c — SparkCanvasView for Avalonia

> Effort: ~1-2 days

Port the WPF views and ViewModel, adapting for the chosen WebView control.

- [ ] Copy `web/spark/` directory into Avalonia project
  - `index.html`, `canvas.js`, `simulation.js`, `events.js`, `ui.js`, `fx.js`, `panels.js`, `style.css`, `themes.js`
  - Configure as embedded resources or content files
  - Ensure the Avalonia build includes these in the output
- [ ] Create `SparkCanvasViewModel.cs` in Avalonia ViewModels
  - Port from WPF `SparkCanvasViewModel.cs`
  - Adapt WebView2-specific calls to chosen Avalonia WebView API
  - Key methods: `InitializeWebView`, `SendEventToCanvas`, `HandleWebMessage`
  - Theme persistence (postMessage round-trip with config.json)
- [ ] Create `Views/SparkCanvasView.axaml` + code-behind
  - WebView control with dark background
  - Loading overlay while WebView initializes
  - Navigation to local spark HTML (virtual host or file URI)
- [ ] Create `Views/SparkCanvasWindow.axaml` + code-behind (pop-out window)
  - Standalone window hosting SparkCanvasView
  - Window title, sizing, dark theme
- [ ] Add panel/data template registration
  - Register in `PanelContentTemplates` (or Avalonia equivalent)
  - SparkCanvasViewModel → SparkCanvasView mapping
- [ ] Wire SSE event bridge in `App.axaml.cs`
  - Bridge `ActivityEventProcessed` → EventAggregator → SSE (same pattern as WPF `App.xaml.cs`)

### Phase 3d — Command Palette + Entry Points

> Effort: ~0.5 day

- [ ] Add command palette entries in Avalonia `MainViewModel.cs`:
  - "Spark: Open Canvas" — opens as center panel
  - "Spark: Open Canvas (Window)" — opens pop-out window
  - Set `IntroducedOn` to current date for What's New
- [ ] Add keyboard shortcut (Ctrl+Shift+V or as assigned in WPF)
- [ ] Add session card context menu "Visualize Session" (if timeline view exists)
- [ ] Verify CORS configuration for the WebView origin (if using virtual host mapping)

### Phase 3e — Collab & Multi-Session Features

> Effort: ~0.5 day

These build on top of the base canvas and should work automatically since the JS is shared. Verification only.

- [ ] Verify multi-session observatory mode works (Multi toggle)
- [ ] Verify collab edge visualization (dashed gradient edges with topic labels)
- [ ] Verify all 12 themes render correctly (Holographic, Matrix, War Room, Tron, LCARS, Blade Runner, Swordfish, Minority Report, WarGames, StarCraft, Zerg Hive, Protoss)
- [ ] Verify canvas search and agent focus
- [ ] Verify session picker, message feed, and all three side panels (Timeline, Files, Transcript)

---

## Implementation Order

```
Phase 1: Hook Debug Dialog ──────────────────────── (~1 day)
Phase 2: Container Settings UI ──────────────────── (~0.5 day)
Phase 3a: WebView Spike ─────────────────────────── (~1 day)
   │
   ├── Phase 3b: SessionActivityService + Watcher ─ (~1 day, parallel with 3a if spike succeeds)
   │
   └── Phase 3c: SparkCanvasView port ───────────── (~1-2 days, depends on 3a + 3b)
          │
          ├── Phase 3d: Palette + Entry Points ──── (~0.5 day)
          └── Phase 3e: Verification ────────────── (~0.5 day)
```

**Total estimated effort: ~5-6 days**

Phases 1 and 2 are independent and can be done in any order. Phase 3a is the critical path — its outcome determines whether Phase 3c-3e proceed or the Spark Canvas port is deferred pending a better WebView solution.

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| Avalonia WebView package lacks postMessage bridge | Blocks Spark Canvas | Spike in Phase 3a; fallback to CefGlue |
| WKWebView doesn't support virtual host mapping | Moderate — affects resource loading | Use `file://` URIs or embedded resource server |
| SessionActivityService port has hidden WPF dependencies | Delays Phase 3b | Core interfaces are well-defined; implementation should be portable |
| WebView adds significant binary size | Low impact | Avalonia.WebView uses system WKWebView (no extra binary) |
| JS canvas has WebView2-specific postMessage format | Minor rework | Abstract message bridge in ViewModel; JS side uses standard `window.postMessage` |

---

## Files to Create/Modify

### Phase 1
| Action | File |
|--------|------|
| Create | `src/TerminalHost.Avalonia/Views/Dialogs/HookDebugDialog.axaml` |
| Create | `src/TerminalHost.Avalonia/Views/Dialogs/HookDebugDialog.axaml.cs` |
| Modify | `src/TerminalHost.Avalonia/ViewModels/MainViewModel.cs` (palette entry) |

### Phase 2
| Action | File |
|--------|------|
| Modify | `src/TerminalHost.Avalonia/Views/SettingsView.axaml` (binding tweaks, recreate button) |

### Phase 3
| Action | File |
|--------|------|
| Modify | `src/TerminalHost.Avalonia/TerminalHost.Avalonia.csproj` (WebView package) |
| Create | `src/TerminalHost.Avalonia/web/spark/*` (copy from WPF, 9 files) |
| Create | `src/TerminalHost.Avalonia/ViewModels/SparkCanvasViewModel.cs` |
| Create | `src/TerminalHost.Avalonia/Views/SparkCanvasView.axaml` |
| Create | `src/TerminalHost.Avalonia/Views/SparkCanvasView.axaml.cs` |
| Create | `src/TerminalHost.Avalonia/Views/SparkCanvasWindow.axaml` |
| Create | `src/TerminalHost.Avalonia/Views/SparkCanvasWindow.axaml.cs` |
| Modify | `src/TerminalHost.Avalonia/Services/SessionActivityService.cs` (implement from stub) |
| Create | `src/TerminalHost.Avalonia/Services/TranscriptWatcher.cs` (or reuse Core impl) |
| Modify | `src/TerminalHost.Avalonia/App.axaml.cs` (DI registration, SSE bridge) |
| Modify | `src/TerminalHost.Avalonia/ViewModels/MainViewModel.cs` (palette entries, shortcuts) |
| Modify | Panel content templates (SparkCanvas data template) |
