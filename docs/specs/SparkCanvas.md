# Spark Canvas

> Real-time canvas visualization of AI agent execution — force-directed graph with holographic aesthetic, showing agents, tool calls, data flow particles, and context usage.

## Status: Phase 3d Partial (2026-03-26)

**Depends on**: SessionLifecycle.md (Phase 0-2, all complete)

### Phase 3a — Core Canvas (Complete)
- [x] Web assets: index.html, canvas.js, simulation.js, events.js, ui.js, style.css, themes.js
- [x] SparkCanvasViewModel.cs (BasePanelViewModel, session management, WebView2 message bridge)
- [x] SparkCanvasView.xaml (WebView2 host with virtual host mapping to spark.local)
- [x] SparkCanvasWindow.xaml (standalone window, Ctrl+Shift+V shortcut)
- [x] SSE event pipeline: App.xaml.cs bridges ActivityEventProcessed → EventAggregator → SSE
- [x] REST endpoints: GET /api/sessions, GET /api/sessions/:id/state
- [x] Agent nodes: multi-layer bloom glow, breathing, orbiting dots, state labels, context rings
- [x] Tool cards: type-colored borders (Read=cyan, Bash=amber, Write=green, Agent=purple), clickable detail panel
- [x] Data flow particles along parent-child edges
- [x] Edge fading synced with agent lifecycle
- [x] 8 visual themes with ambient renders (Holographic, Matrix Rain, War Room, Tron, LCARS, Blade Runner, Swordfish, Minority Report)
- [x] Session picker with auto-discovery and SSE auto-connect
- [x] Message feed with tool-type labels and color differentiation
- [x] Theme persistence via config.json (C#↔JS postMessage round-trip)
- [x] Auto-create agents when events reference unknown IDs (mid-session connect)
- [x] SSE resilience: per-event try-catch, quiet reconnects (no status flicker)
- [x] Subagent tool attribution via hook agent_id field
- [x] Relative file paths (strip workspace prefix) in tool summaries
- [x] CORS for spark.local WebView2 origin

### Phase 3b — Rich Visualization (Complete)

Visual polish and information density. New file: `fx.js` (SparkFX, EdgeParticleSystem, MessageBubbleSystem).

**Spawn & Completion Effects**
- [x] Agent spawn: expanding hexagon ring with white flash and scattered particles (SparkFX.spawnEffect)
- [x] Agent complete: radial flash with expanding ring and glow (SparkFX.completeEffect)
- [x] Agent error/fail: shatter effect with crack lines radiating from center (SparkFX.errorEffect)
- [x] State change detection triggering appropriate FX (processEvent: AgentSpawn/Complete/StateChange)
- [x] Theme-specific FX hooks: `fxSpawn`, `fxComplete`, `fxError` overrides per theme via `triggerSpawn/Complete/Error`
- [x] All 8 themes have custom FX: Matrix (binary rain/dissolve/glitch), War Room (radar ping/tactical flash/explosion), Tron (circuit trace/identity disc/derez), LCARS (transporter beam/panel sweep/red alert), Blade Runner (rain splatter/neon fade/neon flicker), Swordfish (wireframe expand/hex dissolve/digital shatter), Minority Report (glass ripple/precog flash/red ball), Holographic (default hexagon/flash/shatter)

**Tapered Bezier Edges**
- [x] Edges rendered as tapered polygons (wider at source, narrower at target) via _drawTaperedEdge
- [x] Parent-child edges thicker (4→1px taper) than tool edges
- [x] Dashed animation for inactive/waiting edges (animated lineDashOffset)

**Advanced Particles**
- [x] Comet trails on data flow particles (EdgeParticleSystem trail rendering)
- [x] Labeled particles (tool, return labels on edge particles)
- [x] Sinusoidal wobble motion along edges (perpendicular wobble with phase offset)
- [x] Different particle speeds/colors per type (ToolCalling=amber/fast, Active=cyan/slow, return=green)

**Message Bubbles**
- [x] In-canvas text bubbles near each agent (MessageBubbleSystem, max 2 per agent)
- [x] Word-wrapping, truncation, animated fade-in/fade-out (wrapText, life-based fade)
- [x] Thinking bubbles smaller (120px) and more translucent (alpha 0.5)

**Context Usage Bar**
- [x] Horizontal stacked bar below each agent: system prompt, user messages, tool results, reasoning, subagent results
- [x] Threshold-based glow: yellow at 80%, red pulsing at 90% (animated shadowBlur)
- [x] Percentage label when above 70%

**Tool Card Enhancements**
- [x] Two-line layout for completed/error cards (tool name + result/error summary)
- [x] Error state: red pulsing glow with crack lines (animated shadowBlur + decorative cracks)
- [x] Token cost shown on second line for completed cards
- [x] State icon (checkmark/cross) for completed/error cards
- [x] Short filename labels on cards (getToolCardLabel: "Edit ..parent/file.js"), full path in feed
- [x] Meta tool filtering: TaskCreate/Update/List/Get, SendMessage, TodoRead/Write etc. shown as dimmed "META" in feed, no on-canvas cards

**Render Performance**
- [x] Offscreen canvas for ambient layers (_ambientCanvas)
- [ ] Render cache: pre-rendered glow sprites, cached text measurements (deferred — no perf issues yet)
- [ ] Separate bloom pass with configurable blur and intensity (deferred)

### Phase 3c — Interactive Panels (Partial)

Side panels and overlays for deeper inspection. New file: `panels.js`.

**Timeline/Gantt Panel**
- [x] Sliding panel at bottom (toggle via control bar button), canvas-rendered
- [x] Agent rows as horizontal bars with color-coded tool call blocks
- [x] Time axis with grid lines, "now" marker, zoom via scroll wheel
- [x] Zoom in/out/fit buttons, horizontal scroll
- [ ] Playback controls: play/pause/seek, speed control (deferred — needs JSONL replay)

**File Attention Panel**
- [x] Right-side panel listing all files accessed by agents
- [x] Sorted by access count, showing read/write counts per file
- [x] Heat-color coded by frequency (cyan→amber→red), agent count per file
- [x] Strict file path extraction (rejects API paths, directories, regex patterns, junk fragments)
- [x] Readable stats labels ("3 read · 1 write · 2 agents" instead of "3R 1W 2A")
- [ ] Clickable to open in file viewer (requires C# bridge)

**Session Transcript Panel**
- [x] Full transcript view with timestamp, type label, agent name, message text
- [x] Searchable with highlight matches
- [x] User/assistant/thinking/tool entries recorded from events
- [ ] Syntax highlighting for code blocks (deferred)
- [ ] Linkable to canvas nodes (deferred)

**Discovery Cards**
- [ ] Floating cards on canvas showing files, patterns, findings discovered by agents
- [ ] Type-specific colored accents, animate from tool call positions
- [ ] Dashed connection lines to source agent
- [ ] Click for full content preview popup

**Rich Tool Content Renderer**
- [x] Tool-type-aware rendering in detail panel (renderToolContent in ui.js)
- [x] File paths: directory dimmed, filename highlighted
- [x] Bash: command styled with border-left accent, output with error/warning line coloring
- [x] Grep: pattern highlighted, path formatted
- [x] JSON: auto-detected, pretty-printed with key/string/number/bool colors
- [x] Diffs: auto-detected, +/-/@@/header lines colored (green/red/purple/gray)
- [x] Error content: red monospace with border accent

**Cost Visualization** (deferred)
- [ ] Floating $USD cost pills above agents
- [ ] Mini stacked bar chart breaking down token cost by tool type
- [ ] Summary panel with total session cost

**Control Bar Enhancements**
- [x] Filter toggles: show/hide tool cards, edges, bubbles (toggle buttons in control bar)
- [x] Panel toggle buttons: Timeline, Files, Transcript
- [x] Agent filter: "Focus" button in agent detail panel, dims all other agents and tools
- [x] Canvas search: input in control bar, highlights matching agents/tools, dims non-matches

### Phase 3d — Multi-Session & Replay (Partial)

**Multi-Session Observatory**
- [x] Show ALL active sessions simultaneously on one canvas (Multi toggle in control bar)
- [x] Session clustering by project/workspace (ForceSimulation cluster groups, ring layout)
- [x] Glass-morphism session boundaries with labels, agent/tool counts, active glow
- [x] Distinct accent color per session (8-color palette, auto-assigned)
- [x] Multi-mode SSE: unfiltered event stream, all sessions update in real-time
- [x] Observatory badge showing session count and live count
- [x] Session picker hidden in multi-mode, restored on exit
- [x] Collab edge visualization framework (dashed gradient edges with topic labels and flow particles)
- [x] Placeholder sessions: stub main agent when state unavailable (containerized sessions)
- [x] Adaptive cluster spacing and final fit-to-view after all sessions load
- [x] REST endpoints for collab: GET /api/collab/topics, GET /api/collab/sessions
- [x] Collab polling matches topic subscribers to canvas sessions by name/workingDir
- [ ] Claude channels visualized as data flow between sessions

**JSONL Replay Mode**
- [ ] `host --visualize <path.jsonl>` CLI argument
- [ ] Playback controls: play/pause/seek/speed
- [ ] Timeline scrubber synchronized with canvas state
- [ ] Export replay as video/GIF

**macOS (Avalonia)**
- [ ] Avalonia WebView implementation for macOS
- [ ] Same JS canvas, different hosting view

---

## Part 1: What We're Building

A WebView2-hosted canvas that renders a live, interactive force-directed graph of Claude Code sessions. Each agent is a glowing node. Tool calls appear as floating cards. Particles flow along edges to show data movement. The whole scene has a dark holographic aesthetic — deep navy background, cyan/amber/green state colors, bloom glow, and glass-morphism panels.

This replaces the current flat session card list in Timeline Mode with a rich visualization when the user wants deeper insight into what an agent is doing.

### Entry Points

- **Ctrl+Shift+I** (existing Timeline shortcut) — opens the canvas as a center panel
- **Command palette**: "Spark: Open Canvas"
- **Session card context menu**: "Visualize Session" — opens canvas focused on that session
- **Pop-out window**: Same as Timeline pop-out, but for the canvas

### Non-Goals (Phase 3)

- Multi-session tab switching (one session at a time)
- File editor integration (click-to-open is future)

### JSONL File Argument (Testing & Replay)

For development and testing, the canvas can be opened directly from a transcript file:

```bash
# Open a specific transcript file for visualization
host --visualize ~/.claude/projects/.../session-uuid.jsonl

# Short form
host -v path/to/transcript.jsonl
```

This parses the full JSONL file via `TranscriptParserService.ParseLines()`, builds the complete `SessionActivityState`, and renders the canvas in a static (non-live) mode. Events are replayed chronologically using the timestamps from the JSONL, with a playback scrubber to step through the session.

This is useful for:
- **Testing the canvas** without needing a live Claude Code session
- **Debugging visualization** by replaying known transcripts
- **Reviewing past sessions** in detail

---

## Part 2: Visual Design

### 2.1 Scene Background

- **Deep void**: `#050510` (dark navy-black)
- **Hex grid** (optional, togglable): Subtle `#0d0d1f` hexagonal pattern with slow pulse animation
- **Depth particles**: 40-60 small dots at 5-35% opacity drifting slowly — parallax layer for depth perception

### 2.2 Agent Nodes

Agents are circular nodes with glow halos, state-colored rings, and center icons.

| Property | Main Agent | Subagent |
|----------|-----------|----------|
| Radius | 28px | 20px |
| Center icon | Claude spark (SVG path) | Diamond |
| Context display | Rotating ring (progress arc) | Horizontal bar below |

**State colors and effects:**

| State | Color | Effect |
|-------|-------|--------|
| `Active` / `Idle` | Cyan `#66ccff` | Subtle breathing scale (sin wave, 1.5%) |
| `Thinking` | Cyan `#66ccff` | Faster breathing (3%) + orbiting particles |
| `ToolCalling` | Amber `#ffbb44` | Steady glow, tool card attached |
| `WaitingPermission` | Amber `#ffaa33` | Radar ripple rings + slow orbit |
| `Complete` | Green `#66ffaa` | Flash + fade out over ~1s |
| `Error` / `Failed` | Red `#ff5566` | Pulsing red glow |
| `TimedOut` | Gray `#888899` | Dim, no animation |

**Node rendering layers (back to front):**
1. Depth shadow (offset blur beneath)
2. Outer glow halo (state color, 15-30px blur)
3. Scanline stripe (animated horizontal line, subtle)
4. Node fill (`rgba(10, 15, 40, 0.5)`)
5. State ring (thin colored border, breathing)
6. Center icon (white, 45% of radius)
7. Label below node (agent name, 9px monospace)

### 2.3 Context Usage Visualization

**Main agent — rotating ring:**
- Arc shows percentage of context window used
- Fills clockwise from 12 o'clock
- Color transitions: cyan (< 70%) → orange (70-90%) → red (> 90%)
- Percentage label appears when > 70%

**All agents — stacked bar below node:**

| Segment | Color | What |
|---------|-------|------|
| System prompt | `#555577` | Fixed overhead |
| User messages | `#66ccff` | User input tokens |
| Tool results | `#ffbb44` | Most expensive — tool output |
| Reasoning | `#cc88ff` | Thinking blocks |
| Subagent results | `#66ffaa` | Child agent outputs |
| Unused | `rgba(170, 238, 255, 0.05)` | Remaining capacity |

### 2.4 Edges

**Parent → child edges:**
- Tapered bezier curve (3px → 1px)
- Color: Cyan `#66ccff`
- Idle opacity: 8%, active (particles flowing): 30%
- 15% curvature perpendicular to line of sight

**Agent → tool call edges:**
- Thinner tapered bezier (1.5px → 0.5px)
- Color: Amber `#ffbb44`
- Same opacity behavior

### 2.5 Particles (Data Flow)

Particles are small dots with comet trails that flow along edges to show data movement.

| Type | Color | Direction | When |
|------|-------|-----------|------|
| Dispatch | Purple `#cc88ff` | Parent → child | Subagent spawned |
| Tool call | Amber `#ffbb44` | Agent → tool | Tool started |
| Return | Green `#66ffaa` | Tool → agent | Tool completed |
| Subagent return | Green `#66ffaa` | Child → parent | Subagent completed |

**Trail**: 8 segments with decreasing size + alpha. Wobble perpendicular to path (sine wave).

### 2.6 Tool Call Cards

Floating rounded-rect cards near the agent that invoked them.

**Running state:**
- 170px max width, 24px height
- Dark background (`rgba(10, 15, 30, 0.7)`)
- Amber border with spinning ring animation (3 rad/s)
- Text: tool name + input summary (8px monospace, truncated)
- Pulsing opacity (sine wave)

**Completed state:**
- Expands to 30px height (adds token cost line)
- Green border, no spinning
- Fades out after 4 seconds minimum display

**Error state:**
- Red background with pulsing glow
- Red border (2px)
- Error message shown (2 lines)

### 2.7 Message Bubbles

Floating text above agents showing conversation flow.

| Role | Background | Text Color | Label |
|------|-----------|-----------|-------|
| Assistant | Blue-tinted | Light blue | "CLAUDE" |
| Thinking | Purple-tinted | Light purple | "THINKING" |
| User | Amber-tinted | Light amber | "USER" |

- Max width: 220px, word-wrapped, 8 lines max
- Fade-in: 0.3s, hold: 10s, fade-out: 1.5s
- Stack vertically above agent (6px gap)

### 2.8 UI Panels (Glass Morphism)

All panels use glass-morphism: `rgba(10, 15, 30, 0.85)` background + 20px backdrop blur + thin cyan border (`rgba(100, 200, 255, 0.15)`).

**Agent Detail Card (left side, on agent click):**
- Agent name + glowing state dot
- Context usage bar + percentage
- Token count: used / max (monospace)
- Stats: tool count, time alive, current state
- Current tool indicator (if active)

**Transcript Panel (right side, slide-in toggle):**
- Full session history merged chronologically
- Color-coded by role (user/assistant/thinking/tool)
- Thinking blocks collapsible (click to expand)
- Search bar with highlighting

**Message Feed (top-left, always visible):**
- Latest message preview (50 chars)
- Expandable to scrolling list
- Per-agent tabs

**Control Bar (bottom, always visible):**
- Session info: name, duration, agent count, total tokens
- Toggle buttons: Transcript, Hex Grid, File Attention
- Zoom controls: fit-to-view, zoom in/out, current zoom %
- Connection status: LIVE (green pulse) / CONNECTED / OFFLINE

**File Attention Panel (top-right, slide-in toggle):**
- Files sorted by token consumption (descending)
- Per file: name, heat bar, read/write counts, token cost
- Heat colors: red (>70% of max) → amber (40-70%) → cyan (<40%)

---

## Part 3: Layout Engine

### Force-Directed Graph (D3-force)

The visualization uses a physics simulation to position agents:

| Force | Value | Purpose |
|-------|-------|---------|
| Repulsion (charge) | -1200 | Keep agents apart |
| Center attraction | 0.03 | Prevent drift |
| Collision radius | 140px | Minimum separation |
| Link distance | 350px | Parent-child spacing |
| Link strength | 0.4 | Edge tension |
| Velocity decay | 0.4 | Damping |
| Alpha decay | 0.02 | Convergence rate |

### Interaction

| Input | Action |
|-------|--------|
| Click agent | Select → show detail card + chat |
| Drag agent | Pin position (override physics) |
| Click empty space | Deselect |
| Scroll wheel | Zoom (0.2x–4x, steps of 1.08x) |
| Click-drag canvas | Pan view |
| Right-click agent | Context menu (copy ID, open transcript folder) |
| Right-click empty | Context menu (zoom to fit, toggle grid, reset layout) |

Pan has inertia (velocity decay 0.94/frame). Zoom targets the cursor position.

### Auto-fit

On session start or agent spawn, the camera lerps to fit all nodes with 100px padding. Lerp factor: 0.06/frame (smooth, not jarring).

---

## Part 4: Data Pipeline

### Source: ActivityEvent Stream

The canvas is driven by the same `ActivityEvent` stream from Phase 1. Events arrive from two sources that are already wired:

1. **Hooks** (real-time via `ISessionActivityService.ActivityEventProcessed`)
2. **TranscriptWatcher** (incremental JSONL parsing, with dedup)

### Delivery to WebView2

The WebView2 canvas receives events via one of two mechanisms:

**Option A — SSE from API Server (preferred):**
```
ISessionActivityService.ActivityEventProcessed fires
  → IEventAggregatorService publishes as ApiEvent
    → ApiServer SSE endpoint pushes to all subscribers
      → WebView2 JavaScript connects via EventSource("/api/events")
        → Canvas processes event, updates simulation
```

New SSE event type: `activity.event` with the `ActivityEvent` JSON payload.

**Option B — Direct WebView2 postMessage:**
```
ISessionActivityService.ActivityEventProcessed fires
  → WebView2 host calls webView.CoreWebView2.PostWebMessageAsJson(event)
    → JavaScript window.chrome.webview.addEventListener("message", handler)
      → Canvas processes event
```

Option A is preferred because it works for both embedded WebView2 and the pop-out window scenario, and the SSE infrastructure already exists.

### Event → Canvas State Translation

```
ActivityEvent received:
  SessionStart    → Create main agent node, start simulation
  AgentSpawn      → Create child node + parent edge + dispatch particle
  AgentComplete   → Flash effect, fade out node after delay
  AgentStateChange → Update node color/effects
  ModelDetected   → Update agent detail card
  ToolCallStart   → Create tool card + edge + amber particle
  ToolCallEnd     → Complete tool card + green return particle + fade after 4s
  UserMessage     → Show user message bubble above agent
  AssistantMessage → Show assistant message bubble
  ThinkingBlock   → Show thinking bubble (collapsible)
  FileAccessed    → Update file attention panel
  SessionEnd      → Flash all agents, transition to completed state
  SessionTimeout  → Dim all agents, show "Timed Out" label
```

### Initial State Load

When the canvas opens for a session already in progress:
1. Query `ISessionActivityService.GetState(sessionId)` for full `SessionActivityState`
2. Build the graph from existing agents, tool calls, and file activities
3. Run force simulation for 100 ticks to stabilize layout
4. Connect to SSE for live updates going forward

---

## Part 5: Technical Architecture

### WebView2 Hosting

```
src/TerminalHost/TerminalHost/Views/
  SparkCanvasView.xaml           # WPF UserControl hosting WebView2
  SparkCanvasView.xaml.cs        # WebView2 initialization + message bridge
  SparkCanvasWindow.xaml         # Standalone fullscreen window

src/TerminalHost/TerminalHost/ViewModels/
  SparkCanvasViewModel.cs        # Manages session selection, SSE connection

src/TerminalHost/TerminalHost/web/spark/  # Static web assets served to WebView2
    index.html                   # Single HTML entry point
    canvas.js                    # Canvas renderer (main loop, scene graph)
    simulation.js                # Custom force-directed layout (no D3 dependency)
    events.js                    # SSE connection + event processing + WebView2 bridge
    ui.js                        # Glass panels, control bar, interactions
    style.css                    # Glass morphism, panel animations
```

### Canvas Rendering Pipeline (JavaScript, 60fps)

```javascript
function render(timestamp) {
  // 1. Clear to void (#050510)
  // 2. Draw depth particles (parallax)
  // 3. Draw hex grid (if enabled)
  // 4. Draw edges (tapered beziers with glow)
  // 5. Draw agents (composite: glow, scanline, ring, icon, label, context bar)
  // 6. Draw tool cards (rounded rects, spinning rings)
  // 7. Draw message bubbles (word-wrapped text)
  // 8. Draw particles (comet trails along edges)
  // 9. Apply bloom post-processing (half-res additive composite)
  // 10. Draw UI overlays (detail cards, panels via DOM)
  requestAnimationFrame(render);
}
```

**Two-canvas bloom technique:**
- Main canvas: full resolution, all scene elements
- Bloom canvas: half resolution, bright elements only → gaussian blur → composite additive at 50% intensity

### Performance Budget

| Metric | Target |
|--------|--------|
| Frame rate | 60fps steady state |
| Agent nodes | Up to 20 (main + subagents) |
| Active tool cards | Up to 10 visible |
| Particles | Up to 100 active |
| Edge count | Up to 30 |
| Canvas size | Viewport-matched (no fixed size) |

### Panel System Integration

The canvas is a center panel (like GitFiles, PRReview, etc.) using the existing `BasePanelViewModel` pattern:

- Registers as `sparkCanvas` panel type
- Terminals continue running in background when active
- Can be popped out to a separate window
- Persisted in panel state across restarts

---

## Part 6: Implementation Phases

### Phase 3a: Minimal Canvas (MVP)

**Goal**: Force-directed graph with agent nodes and tool cards. No bloom, no particles, no panels — just the core visualization proving the data pipeline works.

1. Create `web/spark/` static assets (index.html + canvas.js + events.js)
2. Create `SparkCanvasView.xaml` hosting WebView2 pointing to local assets
3. Create `SparkCanvasViewModel.cs` with session selection
4. Add `activity.event` SSE event type to `EventAggregatorService`
5. Implement force-directed layout (D3-force or custom, pure JS)
6. Render agent nodes (circles with state colors, labels)
7. Render edges (simple lines, not tapered yet)
8. Render tool cards (basic rounded rects)
9. Process `ActivityEvent` from SSE to update graph
10. Initial state load from REST endpoint
11. Register in command palette + keyboard shortcut
12. **`host --visualize <path.jsonl>`** — parse transcript, build full state, render canvas in replay mode with playback scrubber. Enables testing without a live session.

### Phase 3b: Visual Polish

**Goal**: The full holographic aesthetic.

1. Two-canvas bloom post-processing
2. Tapered bezier edges with glow
3. Particle system (comet trails along edges)
4. Agent node composite rendering (glow, scanline, state ring, icon)
5. Depth particles (background parallax layer)
6. Hex grid (optional, togglable)
7. Tool card spinning rings and fade-out animations
8. Message bubbles (floating text above agents)
9. Spawn/complete flash effects
10. Smooth camera (auto-fit with lerp)

### Phase 3c: Interactive Panels

**Goal**: Glass-morphism UI panels for detailed data.

1. Agent detail card (click to select)
2. Transcript panel (right side, slide-in)
3. Message feed (top-left)
4. File attention panel (top-right)
5. Control bar (bottom — session info, toggles, zoom)
6. Context usage visualization (ring + stacked bar)
7. Search in transcript
8. Collapsible thinking blocks
9. Keyboard shortcuts (Esc to deselect, T for transcript, G for grid)

### Phase 3d: Remaining Gaps

Items from SessionLifecycle.md Phase 3 known gaps:

1. **Permission request detection** — when a `ToolCallStart` event has no corresponding `ToolCallEnd` for >5 seconds, transition agent to `WaitingPermission` state (amber radar ripples)
2. **Context token breakdown** — track tokens by category from JSONL thinking/tool_result/user content blocks, display in context ring and stacked bar
3. **Subagent transcript watching** — extend `TranscriptWatcher` to watch `~/.claude/projects/<project>/<session>/subagents/*.jsonl` for richer subagent data

---

## Part 7: REST Endpoints for Canvas

New endpoints on the existing API server:

### GET /api/sessions/:id/state

Returns the full `SessionActivityState` for initial canvas load.

```json
{
  "sessionId": "uuid",
  "lifecycle": "Active",
  "startTime": "2026-03-26T10:00:00Z",
  "agents": {
    "uuid": { "id": "uuid", "name": "main", "isMain": true, "state": "ToolCalling", "model": "claude-opus-4-6", "tokensUsed": 45000, "toolCallCount": 12 }
  },
  "toolCalls": {
    "toolu_1": { "toolUseId": "toolu_1", "toolName": "Read", "state": "Complete", "inputSummary": "src/app.ts", "startTime": "...", "endTime": "..." }
  },
  "fileActivities": {
    "src/app.ts": { "readCount": 3, "writeCount": 1 }
  },
  "messages": [...]
}
```

### GET /api/events?filter=activity

Existing SSE endpoint with new event type:

```
event: activity.event
data: {"type":"ToolCallStart","sessionId":"uuid","agentId":"uuid","data":{"toolUseId":"toolu_1","toolName":"Read","inputSummary":"src/app.ts"}}
```

---

## Part 8: Color Reference

### Primary Palette

| Name | Hex | RGB | Usage |
|------|-----|-----|-------|
| Deep void | `#050510` | 5, 5, 16 | Canvas background |
| Hex grid | `#0d0d1f` | 13, 13, 31 | Grid lines |
| Glass bg | `rgba(10,15,30,0.85)` | — | Panel backgrounds |
| Glass border | `rgba(100,200,255,0.15)` | — | Panel borders |
| Node fill | `rgba(10,15,40,0.5)` | — | Agent node interior |

### State Colors

| State | Hex | Usage |
|-------|-----|-------|
| Cyan | `#66ccff` | Active/idle agents, parent edges |
| Bright cyan | `#aaeeff` | Primary UI text |
| Amber | `#ffbb44` | Tool calling, tool edges |
| Green | `#66ffaa` | Complete, return particles |
| Red | `#ff5566` | Error state |
| Purple | `#cc88ff` | Thinking, dispatch particles, reasoning tokens |
| Gray | `#888899` | Timed out, paused |

### Context Breakdown

| Category | Hex |
|----------|-----|
| System prompt | `#555577` |
| User messages | `#66ccff` |
| Tool results | `#ffbb44` |
| Reasoning | `#cc88ff` |
| Subagent results | `#66ffaa` |

### Text

| Level | Value |
|-------|-------|
| Primary | `#aaeeff` |
| Dim | `rgba(102,204,255,0.7)` |
| Muted | `rgba(102,204,255,0.3)` |

---

## Part 9: Performance Considerations

### Canvas Optimization

- **Object pooling**: Reuse particle, edge, and tool card objects instead of allocating per-frame
- **Spatial culling**: Don't render elements outside the visible viewport
- **Dirty-rect rendering**: Only redraw regions that changed (optional, complex)
- **Throttle updates**: Batch SSE events received within 16ms into a single render update
- **Bloom at half resolution**: The bloom canvas is 50% size — much cheaper to blur
- **Text caching**: Pre-render frequently-used text strings to offscreen canvases

### Memory Management

- Completed tool cards fade out after 4s, then are recycled to pool
- Particles are pooled (max 100 active)
- Message bubbles expire after 12s, recycled
- Completed agent nodes fade and are removed after animation completes
- `SessionActivityState` already limits data (no unbounded lists)

### WebView2 Considerations

- WebView2 runs in a separate process — no impact on WPF UI thread
- Communication via SSE is lightweight (text stream, no serialization overhead)
- Initial state load is a single REST call
- Canvas `requestAnimationFrame` is throttled by the browser's own vsync

---

## Part 10: Open Questions

1. **D3-force vs custom physics**: D3-force is well-tested but adds a dependency (~50KB). A minimal custom force simulation could work for our simple graph topology (usually <20 nodes). Start with D3-force in Phase 3a, consider replacing if bundle size matters.

2. **WebView2 availability**: WebView2 requires Edge Chromium runtime. It's pre-installed on Windows 10/11 but may need a fallback for older systems. WPF already supports WebView2 via `Microsoft.Web.WebView2` NuGet package.

3. **macOS (Avalonia)**: Avalonia has `WebView` control but it uses different backends (WebKit on macOS). The canvas JavaScript is platform-agnostic, but the hosting view needs a separate Avalonia implementation. Defer to Phase 3d or later.

4. **Zoom-to-fit vs manual layout**: Auto-fit is convenient but can be jarring when agents spawn/complete rapidly. Use lerp smoothing (0.06 factor) and only auto-fit on significant topology changes (new agent, not new tool call).

---

## Part 11: Visual Themes

The Spark Canvas supports switchable visual themes. Each theme is a self-contained CSS + rendering override that changes the ambient layer, color palette, node appearance, and information architecture. The underlying data model and force simulation are shared.

Themes are selected via the control bar dropdown or command palette.

### Theme 1: Holographic (Default)

The current implementation. Deep void background, cyan/amber/green state colors, glass-morphism panels, breathing node animations, bloom glow.

**Inspiration**: Iron Man HUD, generic sci-fi hologram displays.

### Theme 2: Matrix Rain

**Inspiration**: *The Matrix* (1999)

- **Background**: Black with cascading green katakana/code-rain columns as ambient layer
- **Agents**: Vertical "waterfalls" of characters — active agents cascade faster, idle ones slow/dim
- **Tool calls**: Horizontal glitch streaks that interrupt the rain briefly
- **Edges**: Characters flowing along the path (source→target) instead of lines
- **Messages**: Green-on-black typewriter text that fades into the rain
- **Palette**: Monochrome green (`#00ff41`, `#003b00`, `#00cc33`) except errors (red corruption artifacts)
- **Font**: Fixed-width, half-width katakana mixed with ASCII

### Theme 3: War Room / WOPR

**Inspiration**: *WarGames* (1983), cold-war era command centers

- **Background**: Dark with CRT phosphor glow, subtle scanlines, slight barrel distortion
- **Agents**: Blinking radar contacts with trailing afterglow on a sweeping radar arc
- **Tool calls**: Incoming/outgoing trajectory arcs between contacts (missile-track style)
- **Edges**: Dashed lines with moving dash animation (radar sweep reveals them)
- **Messages**: Scrolling terminal log at bottom, chunky pixel font
- **Palette**: Amber-on-black (`#ffaa00`, `#332200`, `#ff6600`), CRT green for secondary (`#33ff33`)
- **Special**: Rotating radar sweep line (one revolution per 10s), contacts flash on sweep pass

### Theme 4: Tron Circuit

**Inspiration**: *Tron: Legacy* (2010)

- **Background**: Dark blue-black flat grid with thin circuit lines
- **Agents**: Glowing discs at grid intersection nodes
- **Tool calls**: Light pulses traveling along grid lines (orthogonal paths, no curves)
- **Edges**: Straight horizontal/vertical segments with 90-degree turns (Manhattan routing)
- **Messages**: Floating translucent panels with hard geometric borders
- **Palette**: Black/deep blue (`#050515`) with bright cyan (`#00dfff`) and orange (`#ff6600`) accent lines
- **Layout**: No force simulation — agents snap to grid positions. New agents animate along grid lines to their position.

### Theme 5: LCARS

**Inspiration**: *Star Trek: The Next Generation* computer displays

- **Background**: Black with rounded rectangular bezels framing data regions
- **Agents**: Named "stations" in horizontal band layout — each agent gets a labeled strip
- **Tool calls**: Color-coded category bars scrolling in a vertical activity log
- **Edges**: Sweep curves connecting stations (quarter-circle arcs in LCARS style)
- **Messages**: Dense text in sans-serif, organized in labeled data blocks
- **Palette**: Mustard (`#ff9900`), salmon (`#cc6699`), lavender (`#9999cc`), sky blue (`#6688cc`), on black
- **Layout**: No force simulation — pure structured dashboard. Very information-dense, zero animation overhead.

### Theme 6: Blade Runner Noir

**Inspiration**: *Blade Runner* (1982), *Blade Runner 2049* (2017)

- **Background**: Dark moody atmosphere with rain/noise grain overlay, vignette
- **Agents**: Surveillance-style photo cards with thermal false-color heat map
- **Tool calls**: "ENHANCE" zoom-and-crop animations with scan lines
- **Edges**: String-and-pin evidence board connections (slightly curved, hand-drawn feel)
- **Messages**: Typewritten dossier cards, slightly yellowed
- **Palette**: Amber/teal split-tone (`#ff8844` / `#44aacc`), film grain, heavy vignette
- **Special**: Ambient rain particle layer, occasional lens flare on active nodes

### Theme 7: Swordfish Terminal

**Inspiration**: *Swordfish* (2001), hacker movie multi-monitor setups

- **Background**: Black with slowly rotating translucent wireframe cubes as ambient layer
- **Agents**: Floating translucent panels (each face shows different data view)
- **Tool calls**: Fast scrolling hex dumps and binary streams flowing between panels
- **Edges**: Wireframe geometry connecting active elements (3D perspective lines)
- **Messages**: Rapid-scroll green text on black terminals, multiple overlapping feeds
- **Palette**: Aggressive neon on black — cyan (`#00ffff`), magenta (`#ff00ff`), electric blue (`#0066ff`)
- **Special**: Multiple simultaneous data streams, "hacking in progress" aesthetic

### Theme 8: Minority Report

**Inspiration**: *Minority Report* (2002), gesture-based transparent displays

- **Background**: Frosted translucent white-blue with subtle depth layers (parallax on mouse)
- **Agents**: Glass cards with frosted borders, draggable/swipeable
- **Tool calls**: Timeline scrubber ribbons with pinch/zoom metaphor
- **Edges**: Thin translucent connection lines, barely visible until hovered
- **Messages**: Clean typography on frosted panels, lots of whitespace
- **Palette**: Blue-white-gray (`#e0eeff`, `#4488bb`, `#223344`), clean and clinical
- **Layout**: Content spreads across transparent depth layers — active session foreground, historical recedes into background blur

### Implementation Notes

- Each theme is a JS module exporting: `colors`, `renderNode()`, `renderEdge()`, `renderAmbient()`, `renderToolCard()`, CSS class name
- Themes can override the layout engine (e.g., Tron uses grid snapping, LCARS uses fixed layout)
- Theme selection persisted in `AppConfiguration.Settings.Spark.Theme`
- Ambient layers (rain, radar sweep, grid) run on a separate canvas layer for performance
- All themes share the same data model, event pipeline, and session picker

---

## Success Criteria

1. **Live visualization works** — open the canvas, start a Claude Code session, see agents and tools appear in real-time
2. **Force layout is stable** — agents don't jitter or overlap, graph settles within 2s
3. **Tool calls are visible** — can see what tool is running, its input summary, and when it completes
4. **Subagents appear** — when Claude spawns subagents, child nodes appear with parent edge and particle flow
5. **Status colors are accurate** — node colors match actual agent state from hooks + transcript
6. **Performance is acceptable** — 60fps with up to 10 agents and 50 active tool cards
7. **Initial load works** — opening the canvas mid-session shows the complete current state
8. **Context usage is visible** — can see how much of the context window each agent has consumed
