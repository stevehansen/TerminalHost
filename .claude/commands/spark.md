---
description: "Context-prime the Spark Canvas feature (real-time force-directed AI session visualization)"
---

# Spark Canvas Context Primer

You are about to work on the **Spark Canvas** feature — a WebView2-hosted real-time force-directed visualization of AI agent sessions with a holographic aesthetic, 8 visual themes, and interactive panels.

## Step 1: Read the spec and current status

Read the full specification first:
- `docs/specs/SparkCanvas.md` — visual design, phases, architecture, color reference, themes
- `docs/specs/SessionLifecycle.md` — data model feeding the canvas (hooks, transcripts, activity events)

## Step 2: Read the core data model (TerminalHost.Core/Domain)

These files define the data structures the canvas visualizes:
- `src/TerminalHost.Core/Domain/SessionActivityState.cs` — main aggregate (session, agents, tools, files, messages)
- `src/TerminalHost.Core/Domain/ActivityEvent.cs` — event types flowing to the canvas
- `src/TerminalHost.Core/Domain/AgentInstance.cs` — agent nodes (state, model, context usage)
- `src/TerminalHost.Core/Domain/ToolCall.cs` — tool call cards (state, timing, tokens)
- `src/TerminalHost.Core/Domain/HookEvent.cs` — raw hook events from Claude Code
- `src/TerminalHost.Core/Domain/FileActivity.cs` — file access tracking
- `src/TerminalHost.Core/Domain/ConversationMessage.cs` — session transcript messages

## Step 3: Read the service layer

- `src/TerminalHost.Core/Interfaces/ISessionActivityService.cs` — service contract
- `src/TerminalHost.Core/Services/SessionActivityService.cs` — processes hooks/transcripts into SessionActivityState
- `src/TerminalHost.Core/Services/EventAggregatorService.cs` — bridges activity events to SSE stream
- `src/TerminalHost.Core/Services/ApiServer.cs` — REST endpoints (GET /api/sessions, /api/sessions/:id/state, SSE /api/events)

## Step 4: Read the ViewModel and Views

- `src/TerminalHost/TerminalHost/ViewModels/SparkCanvasViewModel.cs` — session management, WebView2 message bridge, JSONL loading
- `src/TerminalHost/TerminalHost/Views/SparkCanvasView.xaml` — WebView2 host layout
- `src/TerminalHost/TerminalHost/Views/SparkCanvasView.xaml.cs` — WebView2 init, virtual host mapping, C#<->JS bridge
- `src/TerminalHost/TerminalHost/Views/SparkCanvasWindow.xaml.cs` — standalone window

## Step 5: Read the web assets (the canvas itself)

All in `src/TerminalHost/TerminalHost/web/spark/`:
- `index.html` — HTML entry point, canvas element, session picker, control bar, panels
- `canvas.js` (~90K) — **main rendering engine**: force-directed graph, agent nodes (bloom glow, breathing, orbiting dots), tool cards (type-colored), data flow particles, multi-session observatory, message bubbles, context bars
- `simulation.js` — ForceSimulation class: nodes, edges, repulsion/center/collision forces, cluster support
- `events.js` — event processing from WebView2 postMessage + SSE, session state management, auto-create missing agents
- `fx.js` — SparkFX effects: spawn (hexagon ring), complete (radial flash), error (shatter); EdgeParticleSystem (comet trails); MessageBubbleSystem; per-theme FX overrides
- `themes.js` (~105K) — 8 themes (Holographic, Matrix, War Room, Tron, LCARS, Blade Runner, Swordfish, Minority Report) with ambient renders and color palettes
- `ui.js` — UI controls, session picker, filter toggles, agent detail panel, canvas search, tool-type-aware content rendering
- `panels.js` — Timeline/Gantt panel, File Attention panel, Session Transcript panel
- `style.css` — all CSS (deep void background, glass-morphism, control bar, panels, theme-aware)

## Architecture Summary

```
Data Flow:
  Hook Events → SessionActivityService.ProcessHookEvent() → ActivityEvent
    → EventAggregatorService → ApiServer SSE → WebView2 JavaScript (events.js)
    → canvas.js renders force-directed graph

  JSONL Transcripts → TranscriptParserService → SessionActivityState
    → SparkCanvasViewModel.LoadJsonlFileAsync() → WebView2 postMessage

  C# ↔ JS Bridge:
    C# → JS: webView.PostWebMessageAsString(json) or SSE stream
    JS → C#: window.chrome.webview.postMessage(json) → OnCanvasMessage()

Integration Points:
  - ViewModelFactory.CreateSparkCanvas() instantiates with DI
  - App.xaml.cs bridges ActivityEventProcessed → EventAggregator → SSE
  - PanelContentTemplates.xaml maps SparkCanvasViewModel → SparkCanvasView
  - Command palette: "Spark: Open Canvas" (Ctrl+Shift+V)
```

## Current Status (Phase 3d Partial)

**Complete:** Core canvas (3a), rich visualization with FX (3b), timeline/file/transcript panels (3c partial), multi-session observatory with collab edges (3d partial)

**Remaining work:**
- Discovery cards (floating cards for findings)
- File Attention click-to-open (needs C# bridge)
- Transcript syntax highlighting and node linking
- Cost visualization ($USD pills, cost breakdown)
- JSONL replay with playback controls (play/pause/seek/speed)
- Claude channels visualized as data flow
- macOS Avalonia WebView implementation
- Render cache / bloom pass optimization (deferred, no perf issues)

## Key Patterns

- **Event processing**: Each ActivityEvent type maps to a canvas action in `events.js` `applyActivityEvent()`
- **Agent auto-creation**: If an event references an unknown agent ID, a stub is created (supports mid-session connect)
- **Theme system**: Each theme exports colors + ambient render + optional FX overrides in `themes.js`
- **Panel system**: SparkCanvasViewModel extends BasePanelViewModel (can be popup/panel/window)
- **Multi-session**: "Multi" toggle shows all active sessions with cluster layout and session-colored accents

## macOS Counterpart

The Avalonia (macOS) version has a parallel SessionActivityService at:
- `src/TerminalHost.Avalonia/Services/SessionActivityService.cs`

The canvas web assets are shared cross-platform. Only the WebView hosting differs.

Now tell me what you'd like to work on within Spark Canvas.
