/**
 * Spark Canvas — Main rendering engine.
 * Renders force-directed graph with agent nodes, edges, and tool cards.
 * Colors and ambient rendering are driven by the active theme (see themes.js).
 *
 * Phase 3b: tapered bezier edges, advanced particles, FX, message bubbles,
 *           context bars, enhanced tool cards, offscreen ambient canvas.
 * Phase 3d: multi-session observatory — all active sessions on one canvas
 *           with session clustering, boundaries, and collab edges.
 */

// ─── Debug Logging ─────────────────────────────────────
// Accumulates log entries in a global array for crash debugging.
// Read from C# via: webView.ExecuteScriptAsync("window._getSparkLog()")
// Or inspect visually via the on-canvas debug overlay (last 10 errors).
const _sparkLog = [];
window._sparkLog = _sparkLog;
window._getSparkLog = () => JSON.stringify(_sparkLog, null, 2);
window._clearSparkLog = () => { _sparkLog.length = 0; _updateSparkLogOverlay(); };

function sparkLog(level, msg, data) {
    const entry = {
        t: Date.now(),
        ts: new Date().toISOString(),
        level,
        msg,
        data: data ? String(data).substring(0, 500) : undefined,
    };
    _sparkLog.push(entry);
    if (_sparkLog.length > 200) _sparkLog.shift();
    if (level === 'error') _updateSparkLogOverlay();
}

function _updateSparkLogOverlay() {
    let el = document.getElementById('sparkDebugLog');
    if (!el) {
        el = document.createElement('pre');
        el.id = 'sparkDebugLog';
        Object.assign(el.style, {
            position: 'fixed', bottom: '8px', right: '8px',
            maxWidth: '420px', maxHeight: '220px', overflow: 'auto',
            background: 'rgba(20,0,0,0.85)', color: '#ff6666',
            font: '11px/1.4 monospace', padding: '8px', borderRadius: '6px',
            zIndex: 99999, pointerEvents: 'auto', whiteSpace: 'pre-wrap',
            border: '1px solid rgba(255,80,80,0.4)',
        });
        document.body.appendChild(el);
    }
    const errors = _sparkLog.filter(e => e.level === 'error').slice(-10);
    if (errors.length === 0) { el.style.display = 'none'; return; }
    el.style.display = 'block';
    el.textContent = errors.map(e =>
        `[${e.ts.substring(11, 23)}] ${e.msg}: ${e.data || ''}`
    ).join('\n');
    el.scrollTop = el.scrollHeight;
}

// ─── Session Color Palette ──────────────────────────────
const SESSION_COLORS = [
    '#66ccff', '#ff88aa', '#88ff88', '#ffbb44',
    '#cc88ff', '#44dddd', '#ff8844', '#88aaff',
];

// Fallback colors (used if no theme loaded yet)
const COLORS = {
    void: '#050510', cyan: '#66ccff', brightCyan: '#aaeeff', amber: '#ffbb44',
    green: '#66ffaa', red: '#ff5566', purple: '#cc88ff', gray: '#888899',
    nodeFill: 'rgba(10, 15, 40, 0.5)',
};
const STATE_COLORS = {
    Active: '#66ccff', Idle: '#66ccff', Thinking: '#66ccff',
    ToolCalling: '#ffbb44', WaitingPermission: '#ffbb44',
    Complete: '#66ffaa', Error: '#ff5566', Failed: '#ff5566', TimedOut: '#888899',
};

/** Get the current theme's colors, falling back to defaults. */
function tc() { return getTheme()?.colors || COLORS; }
function tsc() { return getTheme()?.stateColors || STATE_COLORS; }

/** Claude spark SVG path (simplified) */
const SPARK_PATH = new Path2D();
SPARK_PATH.moveTo(0, -8);
SPARK_PATH.bezierCurveTo(2, -3, 3, -2, 8, 0);
SPARK_PATH.bezierCurveTo(3, 2, 2, 3, 0, 8);
SPARK_PATH.bezierCurveTo(-2, 3, -3, 2, -8, 0);
SPARK_PATH.bezierCurveTo(-3, -2, -2, -3, 0, -8);
SPARK_PATH.closePath();

// ─── Pre-computed Hexagon Offsets ────────────────────────
const HEX_OFFSETS = Array.from({ length: 6 }, (_, i) => {
    const angle = (Math.PI / 3) * i - Math.PI / 2;
    return { cos: Math.cos(angle), sin: Math.sin(angle) };
});

function drawHexagon(ctx, cx, cy, r) {
    ctx.beginPath();
    for (let i = 0; i < 6; i++) {
        const px = cx + HEX_OFFSETS[i].cos * r;
        const py = cy + HEX_OFFSETS[i].sin * r;
        i === 0 ? ctx.moveTo(px, py) : ctx.lineTo(px, py);
    }
    ctx.closePath();
}

// ─── Glow Sprite Cache ──────────────────────────────────
// Pre-renders radial gradient glows to offscreen canvases, keyed by color+size.
const _glowCache = new Map();

function getGlowSprite(color, innerR, outerR, alphaHex) {
    const key = `${color}_${innerR}_${outerR}_${alphaHex}`;
    if (_glowCache.has(key)) return _glowCache.get(key);

    const size = outerR * 2;
    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext('2d');
    const grad = ctx.createRadialGradient(outerR, outerR, innerR, outerR, outerR, outerR);
    grad.addColorStop(0, color + alphaHex);
    grad.addColorStop(0.6, color + _hexAlpha(parseInt(alphaHex, 16) / 255 * 0.4));
    grad.addColorStop(1, color + '00');
    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, size, size);

    _glowCache.set(key, canvas);
    return canvas;
}

function _hexAlpha(a) {
    return Math.max(0, Math.min(255, Math.round(a * 255))).toString(16).padStart(2, '0');
}

// ─── Tool Card Constants (agent-flow aligned) ────────────
const TOOL_CARD_W = 160;
const TOOL_CARD_H = 28;
const TOOL_SLOT = {
    maxRings: 5,
    baseDistance: 90,
    ringIncrement: 35,
    baseSteps: 5,
    stepsPerRing: 2,
    fallbackDistance: 90,
};

class SparkCanvas {
    constructor(canvasEl) {
        this.canvas = canvasEl;
        this.ctx = canvasEl.getContext('2d');
        this.sim = new ForceSimulation();
        this.dpr = window.devicePixelRatio || 1;

        // Let simulation use actual rendered session bounds for overlap detection
        this.sim.setRenderedBoundsCallback((groupId) => {
            const session = this.sessions.get(groupId);
            return session?._smoothBounds || null;
        });

        // Camera
        this.camera = { x: 0, y: 0, zoom: 1.0, targetX: 0, targetY: 0, targetZoom: 1.0 };
        this.autoFit = true;

        // State
        this.agents = new Map();       // id -> AgentNode
        this.toolCards = new Map();    // toolUseId -> ToolCard
        this.edges = [];               // { sourceId, targetId, type, active, opacity }
        this.selectedAgentId = null;
        this.selectedToolId = null;
        this.hoveredAgentId = null;

        // Interaction state
        this._dragging = false;
        this._dragNode = null;
        this._dragOffset = { x: 0, y: 0 };
        this._panStart = null;
        this._panCameraStart = null;

        // Animation time
        this._time = 0;
        this._lastFrame = performance.now();

        // Session info (single-session mode)
        this.sessionId = null;
        this.sessionName = '';
        this.sessionStart = null;

        // Multi-session observatory (Phase 3d)
        this.multiMode = false;
        this.sessions = new Map();     // sessionId -> { id, name, projectPath, startTime, isActive, color, agentCount, toolCount }
        this._sessionColorIdx = 0;
        this.collabEdges = [];         // { sourceSessionId, targetSessionId, topic, lastMessageTime, opacity }
        this.collabSubscriptions = new Map(); // topic -> Set<sessionId> — built from activity events
        this.collabTopicNodes = new Map();    // topic name -> { name, description, x, y, sessions, messageCount }

        // Search/filter (Phase 3c)
        this.searchTerm = '';
        this.highlightedAgentId = null;  // Agent filter — highlight one agent
        this._hoveredDismissSession = null;  // Dismiss button hover state

        // Phase 3b systems
        this.fx = new SparkFX();
        this.edgeParticles = new EdgeParticleSystem();
        this.bubbles = new MessageBubbleSystem();

        // Offscreen canvas for ambient layer
        this._ambientCanvas = document.createElement('canvas');
        this._ambientCtx = this._ambientCanvas.getContext('2d');
        this._ambientDirty = true;
        this._lastTheme = null;

        // Edge particle spawn timer
        this._edgeParticleTimer = 0;

        this._resize();
        this._bindEvents();
        this._animate();
    }

    // ─── Public API ─────────────────────────────────────────

    /** Enable/disable multi-session mode */
    setMultiMode(enabled) {
        if (this.multiMode === enabled) return;
        this.multiMode = enabled;
        if (enabled) {
            // Transition: current single-session becomes first session in multi
            if (this.sessionId && !this.sessions.has(this.sessionId)) {
                this._registerSession(this.sessionId, this.sessionName, null, this.sessionStart, true);
                // Tag existing agents with session group
                for (const [id, agent] of this.agents) {
                    if (!agent.sessionId) agent.sessionId = this.sessionId;
                    const node = this.sim.getNode(id);
                    if (node) node.groupId = this.sessionId;
                }
            }
            this.sim.arrangeGroups();
            this.sim.reheat();
        } else {
            // Back to single mode — clear groups
            this.sim.groups.clear();
            for (const node of this.sim.nodes) node.groupId = null;
            this.sim.reheat();
        }
        updateControlBar(this);
    }

    /** Register a session for multi-mode tracking */
    _registerSession(sessionId, name, projectPath, startTime, isActive, source, containerName) {
        if (this.sessions.has(sessionId)) return this.sessions.get(sessionId);
        const color = SESSION_COLORS[this._sessionColorIdx % SESSION_COLORS.length];
        this._sessionColorIdx++;
        const session = {
            id: sessionId,
            name: name || 'Session',
            projectPath: projectPath || '',
            startTime: startTime ? new Date(startTime) : new Date(),
            isActive: isActive !== false,
            color,
            agentCount: 0,
            toolCount: 0,
            source: source || 'Local',
            containerName: containerName || null,
        };
        this.sessions.set(sessionId, session);
        this.sim.setGroup(sessionId, 0, 0);
        this.sim.arrangeGroups();
        return session;
    }

    /** Load initial state from a SessionActivityState object */
    loadState(state) {
        sparkLog('info', 'loadState', state?.sessionId || state?.SessionId || '(multi)');
        try { this._loadStateInner(state); } catch (e) { sparkLog('error', 'loadState', e.message + '\n' + e.stack); }
    }

    _loadStateInner(state) {
        const sessionId = state.sessionId;

        if (this.multiMode) {
            this._loadSessionState(state);
            return;
        }

        // Single-session mode
        this.sessionId = sessionId;
        this.sessionName = state.workingDirectory
            ? state.workingDirectory.split(/[/\\]/).filter(Boolean).pop() || 'Session'
            : 'Session';
        this.sessionStart = state.startTime ? new Date(state.startTime) : new Date();

        // Create agents
        if (state.agents) {
            for (const [id, agent] of Object.entries(state.agents)) {
                this._addAgent(id, agent);
            }
        }

        // Create tool cards for active tools + populate timeline/file panels
        if (state.toolCalls) {
            for (const [id, tc] of Object.entries(state.toolCalls)) {
                if (tc.state === 'Running') {
                    this._addToolCard(tc);
                }
                // Record all tool calls for timeline/file panels
                const startTime = tc.startTime ? new Date(tc.startTime) : this.sessionStart;
                const endTime = tc.endTime ? new Date(tc.endTime) : (tc.state !== 'Running' ? new Date() : null);
                recordTimelineEvent(tc.agentId, tc.toolUseId || id, tc.toolName, tc.state || 'Complete', startTime, endTime);
                const filePath = extractFilePath(tc.inputSummary);
                if (filePath) recordFileAccess(filePath, tc.toolName, tc.agentId);
            }
        }

        // Stabilize layout
        this.sim.stabilize(100);
        this._fitToView(false);

        updateControlBar(this);
    }

    /** Load a single session's state in multi-mode (additive) */
    _loadSessionState(state) {
        sparkLog('info', 'loadSessionState', state?.sessionId || state?.SessionId);
        try { this._loadSessionStateInner(state); } catch (e) { sparkLog('error', 'loadSessionState', e.message + '\n' + e.stack); return; }
    }

    _loadSessionStateInner(state) {
        const sessionId = state.sessionId;
        const name = state.workingDirectory
            ? state.workingDirectory.split(/[/\\]/).filter(Boolean).pop() || 'Session'
            : 'Session';

        this._registerSession(sessionId, name, state.workingDirectory, state.startTime, true,
            state.source, state.containerName);

        // Create agents with session grouping
        if (state.agents) {
            for (const [id, agent] of Object.entries(state.agents)) {
                this._addAgent(id, { ...agent, sessionId });
            }
        }

        // Create tool cards for active tools
        if (state.toolCalls) {
            for (const [id, tc] of Object.entries(state.toolCalls)) {
                if (tc.state === 'Running') {
                    this._addToolCard({ ...tc, sessionId });
                }
                const startTime = tc.startTime ? new Date(tc.startTime) : new Date();
                const endTime = tc.endTime ? new Date(tc.endTime) : (tc.state !== 'Running' ? new Date() : null);
                recordTimelineEvent(tc.agentId, tc.toolUseId || id, tc.toolName, tc.state || 'Complete', startTime, endTime);
                const filePath = extractFilePath(tc.inputSummary);
                if (filePath) recordFileAccess(filePath, tc.toolName, tc.agentId);
                // Detect collab subscriptions from historical tool calls
                if (this.multiMode) {
                    this._detectCollabFromTool(tc.toolName, tc.inputSummary, sessionId);
                }
            }
        }

        // Update session stats
        const session = this.sessions.get(sessionId);
        if (session) {
            session.agentCount = state.agents ? Object.keys(state.agents).length : 0;
            session.toolCount = state.toolCalls ? Object.keys(state.toolCalls).length : 0;
        }

        this.sim.stabilize(80);
        this._fitToView(true);
        updateControlBar(this);
    }

    /** Set search term for highlighting matching agents/tools */
    setSearch(term) {
        this.searchTerm = (term || '').toLowerCase().trim();
    }

    /** Check if an agent matches the current search/filter */
    _isAgentHighlighted(agentId, agent) {
        if (this.highlightedAgentId) return agentId === this.highlightedAgentId;
        if (!this.searchTerm) return true; // No filter = all highlighted
        const term = this.searchTerm;
        return agent.name.toLowerCase().includes(term)
            || (agent.model || '').toLowerCase().includes(term)
            || (agent.task || '').toLowerCase().includes(term)
            || (agent.sessionId || '').toLowerCase().includes(term);
    }

    /** Check if a tool card matches the current search/filter */
    _isToolHighlighted(card) {
        if (this.highlightedAgentId) return card.agentId === this.highlightedAgentId;
        if (!this.searchTerm) return true;
        const term = this.searchTerm;
        return card.toolName.toLowerCase().includes(term)
            || (card.inputSummary || '').toLowerCase().includes(term)
            || (card.resultSummary || '').toLowerCase().includes(term);
    }

    /** Clear all multi-session data */
    clearAll() {
        this.agents.clear();
        this.toolCards.clear();
        this.edges.length = 0;
        this.sim.nodes.length = 0;
        this.sim.edges.length = 0;
        this.sim.groups.clear();
        this.sessions.clear();
        this.collabEdges.length = 0;
        this.collabSubscriptions.clear();
        this.collabTopicNodes.clear();
        this._sessionColorIdx = 0;
        this.sessionId = null;
        this.sessionName = '';
        this.sessionStart = null;
    }

    /** Remove a single session and all its agents/tools/edges */
    removeSession(sessionId) {
        if (!this.sessions.has(sessionId)) return;
        // Remove agents belonging to this session
        for (const [id, agent] of this.agents) {
            if (agent.sessionId === sessionId) {
                this.agents.delete(id);
                this.sim.removeNode(id);
            }
        }
        // Remove tool cards belonging to removed agents
        for (const [id, card] of this.toolCards) {
            if (!this.agents.has(card.agentId)) {
                this.toolCards.delete(id);
            }
        }
        // Remove edges referencing removed agents
        this.edges = this.edges.filter(e =>
            this.agents.has(e.source) && this.agents.has(e.target));
        // Remove collab edges for this session
        this.collabEdges = this.collabEdges.filter(e =>
            e.sourceSessionId !== sessionId && e.targetSessionId !== sessionId);
        // Remove session group from simulation
        this.sim.groups.delete(sessionId);
        this.sessions.delete(sessionId);
        // Deselect if the selected agent/tool was in this session
        if (this.selectedAgentId && !this.agents.has(this.selectedAgentId)) {
            this.selectedAgentId = null;
            hideAgentDetail();
        }
        if (this.selectedToolId && !this.toolCards.has(this.selectedToolId)) {
            this.selectedToolId = null;
            hideToolDetail();
        }
        this.sim.reheat();
    }

    /** Clear feed and transcript panels (for replay restart/seek) */
    clearFeedAndTranscript() {
        const feed = document.getElementById('feedContent');
        if (feed) feed.innerHTML = '<span class="feed-empty">Replaying...</span>';
        const transcript = document.getElementById('transcriptBody');
        if (transcript) transcript.innerHTML = '';
    }

    /** Process a single ActivityEvent */
    processEvent(evt) {
        if (['AgentSpawn', 'AgentComplete', 'SessionStart', 'SessionEnd', 'SessionTimeout'].includes(evt.type || evt.Type)) {
            sparkLog('event', evt.type || evt.Type, evt.sessionId || evt.SessionId);
        }
        try { this._processEventInner(evt); } catch (e) {
            if (!this._lastEventError || performance.now() - this._lastEventError > 2000) {
                sparkLog('error', 'processEvent', e.message + ' | evt: ' + JSON.stringify(evt).substring(0, 200));
                this._lastEventError = performance.now();
            }
        }
    }

    _processEventInner(evt) {
        const type = evt.type || evt.Type;
        const data = evt.data || evt.Data || {};
        const sessionId = evt.sessionId || evt.SessionId;

        // Multi-mode: ensure session is registered, re-activate if new activity arrives
        if (this.multiMode && sessionId) {
            if (!this.sessions.has(sessionId)) {
                // Only auto-register if we have a real working directory;
                // events without cwd would create phantom "Session" blocks.
                // Proper sessions arrive via SessionStart which always has cwd.
                if (data.cwd) {
                    const name = data.cwd.split(/[/\\]/).filter(Boolean).pop() || 'Session';
                    this._registerSession(sessionId, name, data.cwd, null, true, data.source, data.containerName);
                }
            } else if (type === 'ToolCallStart' || type === 'AgentSpawn' || type === 'AgentStateChange') {
                // Re-activate session on new meaningful activity
                const session = this.sessions.get(sessionId);
                if (session && !session.isActive) {
                    session.isActive = true;
                }
            }
        }

        switch (type) {
            case 'SessionStart':
                if (this.multiMode) {
                    const name = (data.cwd || '').split(/[/\\]/).filter(Boolean).pop() || 'Session';
                    this._registerSession(sessionId, name, data.cwd, new Date(), true, data.source, data.containerName);
                    // Immediately dedup: a new session for the same workspace should replace old ones
                    if (typeof deduplicateSessions === 'function') deduplicateSessions();
                } else {
                    this.sessionId = sessionId;
                    this.sessionStart = evt.timestamp ? new Date(evt.timestamp || evt.Timestamp) : new Date();
                    this.sessionName = (data.cwd || '').split(/[/\\]/).filter(Boolean).pop() || 'Session';
                }
                break;

            case 'AgentSpawn': {
                const agentId = data.agentId || sessionId;
                let agentName = data.name || (data.isMain ? 'main' : `agent-${this.agents.size}`);

                // For subagents: absorb the parent's "Agent" tool card to combine them
                // The Agent tool card has the description (e.g., "Explore: Analyze codebase")
                // which is more informative than just the agent type name
                if (!data.isMain && data.parentId) {
                    const parentAgentCards = [...this.toolCards.entries()]
                        .filter(([, c]) => c.agentId === data.parentId
                            && (c.toolName === 'Agent' || c.toolName === 'Task')
                            && c.state === 'Running');
                    if (parentAgentCards.length > 0) {
                        // Take the most recent running Agent card
                        const [cardId, card] = parentAgentCards[parentAgentCards.length - 1];
                        // Use the card's description as the agent name
                        if (card.inputSummary) {
                            agentName = truncate(card.inputSummary, 30);
                        }
                        // Absorb the card — complete it immediately with instant fade
                        card.state = 'Complete';
                        card.endTime = new Date();
                        card.fadeStart = this._time - 6; // Near-instant fade (already past 5.5s threshold)
                    }
                }

                this._addAgent(agentId, {
                    id: agentId,
                    name: agentName,
                    isMain: !!data.isMain,
                    parentId: data.parentId,
                    state: 'Active',
                    model: data.model,
                    task: data.task,
                    sessionId: this.multiMode ? sessionId : undefined,
                    spawnTime: new Date(),
                    toolCallCount: 0,
                    tokensUsed: 0,
                    tokensMax: data.model ? getModelMaxTokens(data.model) : 200000,
                    context: { systemPrompt: 0, userMessages: 0, toolResults: 0, reasoning: 0, subagentResults: 0 }
                });

                // Spawn FX
                const node = this.sim.getNode(agentId);
                const agent = this.agents.get(agentId);
                if (node && agent) {
                    const color = tsc()[agent.state] || tc().cyan;
                    this.fx.triggerSpawn(node.x, node.y, color, node.radius);
                }
                break;
            }

            case 'AgentComplete': {
                const agentId = data.agentId || sessionId;
                const agent = this.agents.get(agentId);
                if (agent) {
                    const prevState = agent.state;
                    agent.state = 'Complete';
                    agent.completeTime = new Date();
                    agent.currentToolUseId = null;

                    // Only start fade if no running tool cards belong to this agent
                    const hasRunningTools = [...this.toolCards.values()].some(
                        c => c.agentId === agentId && c.state === 'Running'
                    );
                    if (!hasRunningTools) {
                        agent.fadeStart = this._time;
                    }

                    const node = this.sim.getNode(agentId);
                    if (node) {
                        node.state = 'Complete';
                        // Complete FX
                        this.fx.triggerComplete(node.x, node.y, tc().green, node.radius);
                    }
                    // Complete any running tool cards belonging to this agent
                    for (const [tid, card] of this.toolCards) {
                        if (card.agentId === agentId && card.state === 'Running') {
                            card.state = 'Complete';
                            card.endTime = new Date();
                            card.fadeStart = this._time;
                        }
                    }
                }
                break;
            }

            case 'AgentStateChange': {
                const agentId = data.agentId || sessionId;
                this._ensureAgent(agentId, sessionId);
                const agent = this.agents.get(agentId);
                if (agent) {
                    const prevState = agent.state;
                    const newState = data.newState || data.state || 'Active';
                    agent.state = newState;

                    // Error FX
                    if ((newState === 'Error' || newState === 'Failed') && prevState !== 'Error' && prevState !== 'Failed') {
                        const node = this.sim.getNode(agentId);
                        if (node) {
                            this.fx.triggerError(node.x, node.y, tc().red, node.radius);
                        }
                    }
                }
                break;
            }

            case 'ModelDetected': {
                const agentId = data.agentId || sessionId;
                this._ensureAgent(agentId, sessionId);
                const agent = this.agents.get(agentId);
                if (agent && data.model) {
                    agent.model = data.model;
                    agent.tokensMax = getModelMaxTokens(data.model);
                }
                break;
            }

            case 'ToolCallStart': {
                const tcAgentId = evt.agentId || data.agentId || sessionId;
                this._ensureAgent(tcAgentId, sessionId);
                const tc = {
                    toolUseId: data.toolUseId,
                    agentId: tcAgentId,
                    toolName: data.toolName,
                    inputSummary: data.inputSummary || '',
                    state: 'Running',
                    startTime: new Date()
                };
                this._addToolCard(tc);

                // Update agent state
                const agent = this.agents.get(tc.agentId);
                if (agent) {
                    // If agent was marked Complete but a new tool arrives, revive it
                    if (agent.state === 'Complete') {
                        agent.fadeStart = null;
                        agent.completeTime = null;
                        const node = this.sim.getNode(tc.agentId);
                        if (node) node.state = 'ToolCalling';
                    }
                    agent.state = 'ToolCalling';
                    agent.currentToolUseId = tc.toolUseId;
                    agent.toolCallCount = (agent.toolCallCount || 0) + 1;
                }

                // Panel data: timeline + file attention + transcript
                recordTimelineEvent(tc.agentId, tc.toolUseId, tc.toolName, 'Running', new Date(), null);
                // Extract file path from inputSummary for file panel
                const filePath = extractFilePath(tc.inputSummary);
                if (filePath) recordFileAccess(filePath, tc.toolName, tc.agentId);
                recordTranscriptEntry(tc.agentId, 'tool', `${tc.toolName} ${tc.inputSummary || ''}`);

                // Track collab subscriptions from MCP tool calls (multi-session)
                if (this.multiMode && sessionId) {
                    this._detectCollabFromTool(tc.toolName, tc.inputSummary, sessionId);
                }
                break;
            }

            case 'ToolCallEnd': {
                const card = this.toolCards.get(data.toolUseId);
                if (card) {
                    const isError = !!data.error;
                    card.state = isError ? 'Error' : 'Complete';
                    card.endTime = new Date();
                    card.error = data.error;
                    card.resultSummary = data.resultSummary;
                    card.tokenCost = data.tokenCost;
                    card.fadeStart = this._time;

                    // Error FX on tool card
                    if (isError && card._bounds) {
                        this.fx.triggerError(
                            card._bounds.x + card._bounds.w / 2,
                            card._bounds.y + card._bounds.h / 2,
                            tc().red, 10
                        );
                    }
                }

                // Prefer tool card's agentId (from hooks, more accurate) over transcript event's agentId
                const agentId = card?.agentId || evt.agentId || data.agentId || sessionId;
                if (agentId !== sessionId) {
                    this._ensureAgent(agentId, sessionId);
                }
                const agent = this.agents.get(agentId);
                if (agent) {
                    // Clear active tool state if this is the tool the agent is currently running.
                    // The hook ToolCallEnd arrives first (tokenCost=0), then the transcript
                    // ToolCallEnd arrives later with actual tokenCost — don't gate token
                    // accumulation on currentToolUseId since it may already be cleared.
                    if (agent.currentToolUseId === data.toolUseId) {
                        agent.state = 'Active';
                        agent.currentToolUseId = null;
                    }
                    if (data.tokenCost) {
                        agent.tokensUsed = (agent.tokensUsed || 0) + data.tokenCost;
                        if (agent.context) agent.context.toolResults += data.tokenCost;
                    }

                    // If agent is already Complete and this was the last running tool, start fade now
                    if (agent.state === 'Complete' && !agent.fadeStart) {
                        const hasMoreRunning = [...this.toolCards.values()].some(
                            c => c.agentId === agentId && c.state === 'Running'
                        );
                        if (!hasMoreRunning) {
                            agent.fadeStart = this._time;
                        }
                    }
                }

                // Panel data: update timeline
                recordTimelineEvent(agentId, data.toolUseId, data.toolName, data.error ? 'Error' : 'Complete', null, new Date());
                break;
            }

            case 'UserMessage': {
                addFeedEntry('USER', truncate(data.content, 120), 'user');
                // Show bubble on main agent
                if (this.sessionId) {
                    this.bubbles.add(this.sessionId, data.content || '', 'user');
                }
                recordTranscriptEntry(this.sessionId || 'main', 'user', data.content || '');
                // Accumulate estimated tokens into agent context
                if (data.estimatedTokens) {
                    const msgAgentId = evt.agentId || sessionId;
                    if (msgAgentId !== sessionId) {
                        this._ensureAgent(msgAgentId, sessionId);
                    }
                    const msgAgent = this.agents.get(msgAgentId);
                    if (msgAgent) {
                        msgAgent.tokensUsed = (msgAgent.tokensUsed || 0) + data.estimatedTokens;
                        if (msgAgent.context) msgAgent.context.userMessages += data.estimatedTokens;
                    }
                }
                break;
            }

            case 'AssistantMessage': {
                addFeedEntry('CLAUDE', truncate(data.content, 120), 'assistant');
                if (this.sessionId) {
                    this.bubbles.add(this.sessionId, data.content || '', 'assistant');
                }
                recordTranscriptEntry(this.sessionId || 'main', 'assistant', data.content || '');
                // Accumulate estimated tokens into agent context
                if (data.estimatedTokens) {
                    const msgAgentId = evt.agentId || sessionId;
                    if (msgAgentId !== sessionId) {
                        this._ensureAgent(msgAgentId, sessionId);
                    }
                    const msgAgent = this.agents.get(msgAgentId);
                    if (msgAgent) {
                        msgAgent.tokensUsed = (msgAgent.tokensUsed || 0) + data.estimatedTokens;
                        if (msgAgent.context) msgAgent.context.userMessages += data.estimatedTokens;
                    }
                }
                break;
            }

            case 'ThinkingBlock': {
                addFeedEntry('THINKING', truncate(data.content, 80), 'thinking');
                if (this.sessionId) {
                    this.bubbles.add(this.sessionId, data.content || '', 'thinking');
                }
                recordTranscriptEntry(this.sessionId || 'main', 'thinking', data.content || '');
                // Accumulate estimated tokens into agent context (reasoning)
                if (data.estimatedTokens) {
                    const msgAgentId = evt.agentId || sessionId;
                    if (msgAgentId !== sessionId) {
                        this._ensureAgent(msgAgentId, sessionId);
                    }
                    const msgAgent = this.agents.get(msgAgentId);
                    if (msgAgent) {
                        msgAgent.tokensUsed = (msgAgent.tokensUsed || 0) + data.estimatedTokens;
                        if (msgAgent.context) msgAgent.context.reasoning += data.estimatedTokens;
                    }
                }
                break;
            }

            case 'SessionEnd':
            case 'SessionTimeout':
                for (const [id, agent] of this.agents) {
                    // In multi-mode, only affect agents belonging to this session
                    if (this.multiMode && agent.sessionId !== sessionId) continue;
                    if (agent.state !== 'Complete' && agent.state !== 'Error') {
                        agent.state = type === 'SessionTimeout' ? 'TimedOut' : 'Complete';
                        agent.completeTime = new Date();
                        agent.currentToolUseId = null;
                        agent.fadeStart = this._time;
                        const node = this.sim.getNode(id);
                        if (node) {
                            this.fx.triggerComplete(node.x, node.y,
                                type === 'SessionTimeout' ? tc().gray : tc().green,
                                node.radius);
                        }
                    }
                }
                // Complete all running tool cards for this session's agents
                for (const [tid, card] of this.toolCards) {
                    if (card.state !== 'Running') continue;
                    const cardAgent = this.agents.get(card.agentId);
                    if (!cardAgent) continue;
                    if (this.multiMode && cardAgent.sessionId !== sessionId) continue;
                    card.state = 'Complete';
                    card.endTime = new Date();
                    card.fadeStart = this._time;
                }
                // Mark session inactive in multi-mode
                if (this.multiMode) {
                    const session = this.sessions.get(sessionId);
                    if (session) session.isActive = false;
                }
                break;
        }

        updateControlBar(this);

        if (this.selectedAgentId) {
            updateAgentDetail(this, this.selectedAgentId);
        }
    }

    // ─── Internal: Agent Management ─────────────────────────

    /** Ensure an agent exists — auto-create if an event references an unknown agent. */
    _ensureAgent(agentId, sessionId) {
        if (this.agents.has(agentId)) return;
        // Auto-create: if agentId === sessionId it's the main agent
        const isMain = agentId === sessionId || this.agents.size === 0;
        this._addAgent(agentId, {
            name: isMain ? 'main' : 'agent',
            isMain,
            state: 'Active',
            sessionId: this.multiMode ? sessionId : undefined,
        });
    }

    _addAgent(id, agentData) {
        if (this.agents.has(id)) return;

        const agent = {
            id,
            name: agentData.name || 'agent',
            isMain: !!agentData.isMain,
            parentId: agentData.parentId,
            state: agentData.state || 'Active',
            model: agentData.model,
            task: agentData.task,
            sessionId: agentData.sessionId || null,
            spawnTime: agentData.spawnTime ? new Date(agentData.spawnTime) : new Date(),
            completeTime: agentData.completeTime ? new Date(agentData.completeTime) : null,
            toolCallCount: agentData.toolCallCount || 0,
            tokensUsed: agentData.tokensUsed || 0,
            tokensMax: agentData.tokensMax || 200000,
            context: agentData.context || { systemPrompt: 0, userMessages: 0, toolResults: 0, reasoning: 0, subagentResults: 0 },
            currentToolUseId: agentData.currentToolUseId,
            fadeStart: null,
        };

        this.agents.set(id, agent);

        // Add simulation node (with group for multi-session clustering)
        const radius = agent.isMain ? 28 : 20;
        const node = { id, radius, state: agent.state };
        if (agent.sessionId && this.multiMode) {
            node.groupId = agent.sessionId;
        }
        this.sim.addNode(node);

        // Add edge to parent
        if (agent.parentId && this.agents.has(agent.parentId)) {
            this.sim.addEdge(agent.parentId, id);
            this.edges.push({
                sourceId: agent.parentId,
                targetId: id,
                type: 'parent-child',
                active: true,
                spawnTime: this._time
            });
        }

        if (this.autoFit) {
            this._fitToView(true);
        }
    }

    _addToolCard(tc) {
        if (this.toolCards.has(tc.toolUseId)) return;

        const toolRole = getToolRole(tc.toolName);

        // Meta tools: add dimmed feed entry but no on-canvas card
        if (toolRole.isMeta) {
            addFeedEntry(toolRole.label, `${tc.toolName} ${truncate(tc.inputSummary, 60)}`, toolRole.css);
            return;
        }

        const card = {
            toolUseId: tc.toolUseId,
            agentId: tc.agentId,
            toolName: tc.toolName,
            inputSummary: tc.inputSummary || '',
            state: tc.state || 'Running',
            startTime: tc.startTime ? new Date(tc.startTime) : new Date(),
            endTime: tc.endTime ? new Date(tc.endTime) : null,
            error: null,
            resultSummary: null,
            tokenCost: null,
            fadeStart: null,
        };
        this.toolCards.set(tc.toolUseId, card);

        // Pre-compute slot position immediately so cards that start+complete
        // in the same frame (common during replay) still get unique positions
        const agent = this.agents.get(tc.agentId);
        const agentNode = agent ? this.sim.getNode(tc.agentId) : null;
        if (agentNode) {
            const slot = this._findToolSlot(agentNode, agent);
            card._slotOffset = { dx: slot.x - agentNode.x, dy: slot.y - agentNode.y };
            card._bounds = { x: slot.x, y: slot.y, w: TOOL_CARD_W, h: TOOL_CARD_H };
        }

        addFeedEntry(toolRole.label, `${tc.toolName} ${truncate(tc.inputSummary, 60)}`, toolRole.css);
    }

    // ─── Camera ─────────────────────────────────────────────

    _fitToView(smooth = true) {
        const bounds = this._getFullBounds(120);
        const vw = this.canvas.width / this.dpr;
        const vh = this.canvas.height / this.dpr;

        const scaleX = vw / bounds.width;
        const scaleY = vh / bounds.height;
        const zoom = Math.min(scaleX, scaleY, 2.0);

        const cx = bounds.x + bounds.width / 2;
        const cy = bounds.y + bounds.height / 2;

        if (smooth) {
            this.camera.targetX = cx;
            this.camera.targetY = cy;
            this.camera.targetZoom = zoom;
        } else {
            this.camera.x = this.camera.targetX = cx;
            this.camera.y = this.camera.targetY = cy;
            this.camera.zoom = this.camera.targetZoom = zoom;
        }
    }

    /** Get full bounding box including agent nodes AND visible tool cards */
    _getFullBounds(padding = 100) {
        const nodeBounds = this.sim.getBounds(0);
        let minX = nodeBounds.x;
        let minY = nodeBounds.y;
        let maxX = nodeBounds.x + nodeBounds.width;
        let maxY = nodeBounds.y + nodeBounds.height;

        // Expand to include visible tool cards
        for (const [, card] of this.toolCards) {
            if (!card._bounds) continue;
            if (card.fadeStart != null && (this._time - card.fadeStart) > 5.5) continue;
            const b = card._bounds;
            minX = Math.min(minX, b.x);
            minY = Math.min(minY, b.y);
            maxX = Math.max(maxX, b.x + b.w);
            maxY = Math.max(maxY, b.y + b.h);
        }

        return {
            x: minX - padding,
            y: minY - padding,
            width: (maxX - minX) + padding * 2,
            height: (maxY - minY) + padding * 2
        };
    }

    _updateCamera() {
        const lerp = 0.06;
        this.camera.x += (this.camera.targetX - this.camera.x) * lerp;
        this.camera.y += (this.camera.targetY - this.camera.y) * lerp;
        this.camera.zoom += (this.camera.targetZoom - this.camera.zoom) * lerp;
    }

    _screenToWorld(sx, sy) {
        const vw = this.canvas.width / this.dpr;
        const vh = this.canvas.height / this.dpr;
        return {
            x: (sx - vw / 2) / this.camera.zoom + this.camera.x,
            y: (sy - vh / 2) / this.camera.zoom + this.camera.y
        };
    }

    _worldToScreen(wx, wy) {
        const vw = this.canvas.width / this.dpr;
        const vh = this.canvas.height / this.dpr;
        return {
            x: (wx - this.camera.x) * this.camera.zoom + vw / 2,
            y: (wy - this.camera.y) * this.camera.zoom + vh / 2
        };
    }

    // ─── Rendering ──────────────────────────────────────────

    _animate() {
        try {
            const now = performance.now();
            const dt = Math.min((now - this._lastFrame) / 1000, 0.05);
            this._lastFrame = now;
            this._time += dt;
            this._dt = dt;

            // Physics
            this.sim.tick();

            // Camera
            this._updateCamera();

            // Update FX systems
            this.fx.update(dt);
            this.edgeParticles.update(dt);
            this.bubbles.update(dt);

            // Spawn edge particles periodically
            this._edgeParticleTimer += dt;
            if (this._edgeParticleTimer > 0.6) {
                this._edgeParticleTimer = 0;
                this._spawnEdgeParticles();
            }

            // Draw
            this._render();

            // Garbage collect faded tool cards
            this._gcToolCards();
        } catch (e) {
            sparkLog('error', 'animate', e.message + '\n' + e.stack);
        }
        requestAnimationFrame(() => this._animate());
    }

    _render() {
        const ctx = this.ctx;
        const w = this.canvas.width;
        const h = this.canvas.height;
        const theme = getTheme();

        // Clear with theme background
        if (theme?.clearCanvas) {
            theme.clearCanvas(ctx, w, h);
        } else {
            ctx.fillStyle = tc().void;
            ctx.fillRect(0, 0, w, h);
        }

        // Ambient layer (rendered directly — animated themes need fresh canvas each frame)
        if (theme?.renderAmbient) {
            theme.renderAmbient(ctx, w, h, this.dpr, this._dt || 0.016);
        }

        ctx.save();
        ctx.scale(this.dpr, this.dpr);

        // Apply camera transform
        const vw = w / this.dpr;
        const vh = h / this.dpr;
        ctx.translate(vw / 2, vh / 2);
        ctx.scale(this.camera.zoom, this.camera.zoom);
        ctx.translate(-this.camera.x, -this.camera.y);

        // Draw session boundaries (multi-mode, behind everything)
        if (this.multiMode && this.sessions.size > 0) {
            this._drawSessionBoundaries(ctx);
        }

        // Draw collab edges between sessions
        if (this.multiMode && this.collabEdges.length > 0) {
            this._drawCollabEdges(ctx);
        }

        // Draw edges (tapered bezier)
        const vis = typeof getFilters === 'function' ? getFilters() : {};
        if (vis.showEdges !== false) {
            this._drawEdges(ctx);
            this.edgeParticles.render(ctx, this._time);
        }

        // Draw FX (behind nodes)
        this.fx.render(ctx);

        // Draw tool cards
        if (vis.showCards !== false) {
            this._drawToolCards(ctx);
        }

        // Draw agents
        this._drawAgents(ctx);

        // Draw message bubbles
        if (vis.showBubbles !== false) {
            this.bubbles.render(ctx, this.agents, this.sim, this._time);
        }

        ctx.restore();
    }

    // ─── Tapered Bezier Edges ───────────────────────────────

    _drawEdges(ctx) {
        const theme = getTheme();
        const beamColor = theme?.edgeBaseColor || tc().cyan;

        for (const edge of this.edges) {
            const sourceNode = this.sim.getNode(edge.sourceId);
            const targetNode = this.sim.getNode(edge.targetId);
            if (!sourceNode || !targetNode) continue;

            // Fade edge if target agent is faded
            const targetAgent = this.agents.get(edge.targetId);
            let edgeAlpha = 1.0;
            if (targetAgent && !targetAgent.isMain && targetAgent.fadeStart != null) {
                const elapsed = this._time - targetAgent.fadeStart;
                if (elapsed > 60) {
                    edgeAlpha = Math.max(0, 1 - (elapsed - 60) / 3);
                }
            }
            if (edgeAlpha <= 0) continue;

            const isParentChild = edge.type === 'parent-child';
            const isActive = targetAgent && (targetAgent.state === 'Active' || targetAgent.state === 'ToolCalling' || targetAgent.state === 'Thinking');

            // Cubic bezier control points (agent-flow style)
            const dx = targetNode.x - sourceNode.x;
            const dy = targetNode.y - sourceNode.y;
            const dist = Math.sqrt(dx * dx + dy * dy) || 1;
            const curvature = dist * 0.15;
            const perpX = (-dy / dist) * curvature;
            const perpY = (dx / dist) * curvature;
            const cp1x = sourceNode.x + dx * 0.33 + perpX;
            const cp1y = sourceNode.y + dy * 0.33 + perpY;
            const cp2x = sourceNode.x + dx * 0.66 + perpX;
            const cp2y = sourceNode.y + dy * 0.66 + perpY;

            // Beam widths and alpha (agent-flow aligned: very subtle idle, visible active)
            const baseAlpha = isActive ? 0.20 : 0.08;
            const pulsing = isActive ? Math.sin(this._time * 4) * 0.1 + 0.9 : 1;
            const startW = isParentChild ? 2.5 : 1.2;
            const endW = isParentChild ? 0.8 : 0.4;

            ctx.save();

            // Always draw tapered bezier (agent-flow approach)
            this._drawTaperedBezier(ctx,
                sourceNode.x, sourceNode.y, cp1x, cp1y, cp2x, cp2y, targetNode.x, targetNode.y,
                startW, endW, beamColor, baseAlpha * pulsing * edgeAlpha);

            // Active glow beam (wider, dimmer overlay)
            if (isActive) {
                this._drawTaperedBezier(ctx,
                    sourceNode.x, sourceNode.y, cp1x, cp1y, cp2x, cp2y, targetNode.x, targetNode.y,
                    startW + 3, endW + 1, beamColor, 0.08 * edgeAlpha);
            }

            ctx.restore();
        }
    }

    /** Draw a tapered cubic bezier edge as a filled polygon (agent-flow aligned) */
    _drawTaperedBezier(ctx, fromX, fromY, cp1x, cp1y, cp2x, cp2y, toX, toY, startWidth, endWidth, color, alpha) {
        const steps = 16;
        ctx.beginPath();

        // Forward pass: left side
        for (let i = 0; i <= steps; i++) {
            const t = i / steps;
            const halfW = (startWidth + (endWidth - startWidth) * t) / 2;
            const p = this._bezierNormalAt(t, fromX, fromY, cp1x, cp1y, cp2x, cp2y, toX, toY, halfW);
            if (i === 0) ctx.moveTo(p.x + p.nx, p.y + p.ny);
            else ctx.lineTo(p.x + p.nx, p.y + p.ny);
        }

        // Reverse pass: right side
        for (let i = steps; i >= 0; i--) {
            const t = i / steps;
            const halfW = (startWidth + (endWidth - startWidth) * t) / 2;
            const p = this._bezierNormalAt(t, fromX, fromY, cp1x, cp1y, cp2x, cp2y, toX, toY, halfW);
            ctx.lineTo(p.x - p.nx, p.y - p.ny);
        }

        ctx.closePath();
        ctx.fillStyle = color + _hexAlpha(alpha);
        ctx.fill();
    }

    /** Compute cubic bezier position + perpendicular normal at parameter t */
    _bezierNormalAt(t, fromX, fromY, cp1x, cp1y, cp2x, cp2y, toX, toY, halfW) {
        const mt = 1 - t;
        const x = mt*mt*mt*fromX + 3*mt*mt*t*cp1x + 3*mt*t*t*cp2x + t*t*t*toX;
        const y = mt*mt*mt*fromY + 3*mt*mt*t*cp1y + 3*mt*t*t*cp2y + t*t*t*toY;
        const dt = 0.001;
        const t0 = Math.max(0, t - dt), t1 = Math.min(1, t + dt);
        const m0 = 1-t0, m1 = 1-t1;
        const tx = (m1*m1*m1*fromX + 3*m1*m1*t1*cp1x + 3*m1*t1*t1*cp2x + t1*t1*t1*toX)
                 - (m0*m0*m0*fromX + 3*m0*m0*t0*cp1x + 3*m0*t0*t0*cp2x + t0*t0*t0*toX);
        const ty = (m1*m1*m1*fromY + 3*m1*m1*t1*cp1y + 3*m1*t1*t1*cp2y + t1*t1*t1*toY)
                 - (m0*m0*m0*fromY + 3*m0*m0*t0*cp1y + 3*m0*t0*t0*cp2y + t0*t0*t0*toY);
        const len = Math.sqrt(tx*tx + ty*ty) || 1;
        return { x, y, nx: (-ty / len) * halfW, ny: (tx / len) * halfW };
    }

    // ─── Session Bounds Calculation (includes tool cards) ─────

    /** Get session bounds including both agent nodes and their visible tool cards */
    _getSessionBoundsWithTools(sessionId) {
        const padding = 50;
        const groupNodes = this.sim.nodes.filter(n => n.groupId === sessionId);
        const group = this.sim.groups.get(sessionId);

        if (groupNodes.length === 0 && !group) return null;

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

        // Include agent nodes
        for (const node of groupNodes) {
            minX = Math.min(minX, node.x - node.radius);
            minY = Math.min(minY, node.y - node.radius);
            maxX = Math.max(maxX, node.x + node.radius);
            maxY = Math.max(maxY, node.y + node.radius);
        }

        // Include visible tool cards belonging to this session's agents
        for (const [, card] of this.toolCards) {
            if (!card._bounds) continue;
            // Skip fully faded cards
            if (card.fadeStart != null && (this._time - card.fadeStart) > 5.5) continue;
            const cardAgent = this.agents.get(card.agentId);
            if (!cardAgent || cardAgent.sessionId !== sessionId) continue;
            const b = card._bounds;
            minX = Math.min(minX, b.x);
            minY = Math.min(minY, b.y);
            maxX = Math.max(maxX, b.x + b.w);
            maxY = Math.max(maxY, b.y + b.h);
        }

        if (minX === Infinity) {
            // No content — use group center with compact size
            return group ? { x: group.cx - 60, y: group.cy - 60, width: 120, height: 120 } : null;
        }

        return {
            x: minX - padding,
            y: minY - padding,
            width: (maxX - minX) + padding * 2,
            height: (maxY - minY) + padding * 2
        };
    }

    // ─── Session Boundaries (Multi-Mode) ──────────────────────

    _drawSessionBoundaries(ctx) {
        for (const [sessionId, session] of this.sessions) {
            // Include tool card positions in bounds calculation
            const rawBounds = this._getSessionBoundsWithTools(sessionId);
            if (!rawBounds) continue;

            // Smoothed boundary: grow fast, shrink gradually
            if (!session._smoothBounds) {
                session._smoothBounds = { ...rawBounds };
                session._lastActiveTime = this._time;
                session._boundsCreatedTime = this._time;
                session._prevRawBounds = { ...rawBounds };
                session._rawStableSince = this._time;
            }
            const sb = session._smoothBounds;

            // Track when session last had active tools/agents
            const hasActiveAgent = [...this.agents.values()].some(a => a.sessionId === sessionId && a.state !== 'Complete' && a.state !== 'Error' && a.state !== 'TimedOut');
            const hasRunningTools = [...this.toolCards.values()].some(t => {
                const a = this.agents.get(t.agentId);
                return a && a.sessionId === sessionId && t.state === 'Running';
            });
            if (hasActiveAgent || hasRunningTools) {
                session._lastActiveTime = this._time;
            }

            // Debounce: track when raw bounds became "stable" (stopped changing significantly)
            const prev = session._prevRawBounds;
            const rawDelta = Math.abs(rawBounds.x - prev.x) + Math.abs(rawBounds.y - prev.y)
                + Math.abs(rawBounds.width - prev.width) + Math.abs(rawBounds.height - prev.height);
            if (rawDelta > 5) {
                session._rawStableSince = this._time; // bounds still changing
            }
            session._prevRawBounds = { ...rawBounds };
            const stableFor = this._time - session._rawStableSince;

            // Determine lerp rates based on state
            const age = this._time - (session._boundsCreatedTime || 0);
            const timeSinceActive = this._time - (session._lastActiveTime || 0);
            const isFullyComplete = !hasActiveAgent && !hasRunningTools;

            let growRate, shrinkRate;
            if (age < 1.5) {
                // New session: snap quickly to position as simulation settles
                growRate = 0.5;
                shrinkRate = 0.4;
            } else if (isFullyComplete && timeSinceActive > 3.0) {
                // Completed session with no activity: shrink faster to actual content
                growRate = 0.15;
                shrinkRate = stableFor > 0.5 ? 0.08 : 0.02; // faster once bounds are stable
            } else {
                // Normal active session
                const canShrink = timeSinceActive > 3.0;
                growRate = 0.15;
                shrinkRate = canShrink ? 0.03 : 0.001;
            }

            // Lerp each dimension
            sb.x += (rawBounds.x - sb.x) * (rawBounds.x < sb.x ? growRate : shrinkRate);
            sb.y += (rawBounds.y - sb.y) * (rawBounds.y < sb.y ? growRate : shrinkRate);
            sb.width += (rawBounds.width - sb.width) * (rawBounds.width > sb.width ? growRate : shrinkRate);
            sb.height += (rawBounds.height - sb.height) * (rawBounds.height > sb.height ? growRate : shrinkRate);

            const { x, y, width: w, height: h } = sb;
            const color = session.color;
            const isActive = session.isActive;

            ctx.save();

            // Glass-morphism boundary background
            ctx.fillStyle = isActive
                ? `rgba(${hexToRgb(color)}, 0.04)`
                : `rgba(${hexToRgb(color)}, 0.02)`;
            roundRect(ctx, x, y, w, h, 12);
            ctx.fill();

            // Border
            ctx.strokeStyle = isActive ? color + '55' : color + '22';
            ctx.lineWidth = isActive ? 1.5 : 1;
            if (!isActive) {
                ctx.setLineDash([8, 6]);
                ctx.lineDashOffset = -this._time * 10;
            }
            roundRect(ctx, x, y, w, h, 12);
            ctx.stroke();
            ctx.setLineDash([]);

            // Active session glow
            if (isActive) {
                ctx.save();
                ctx.shadowColor = color;
                ctx.shadowBlur = 8;
                ctx.strokeStyle = color + '20';
                ctx.lineWidth = 2;
                roundRect(ctx, x, y, w, h, 12);
                ctx.stroke();
                ctx.restore();
            }

            // Session label (top-left of boundary)
            ctx.fillStyle = color;
            ctx.font = 'bold 11px monospace';
            ctx.textAlign = 'left';
            const label = session.name;
            const textY = y + 14;
            ctx.fillText(label, x + 10, textY);

            // Devcontainer cloud icon after name
            const isDevContainer = session.source === 'DevContainer';
            if (isDevContainer) {
                const labelWidth = ctx.measureText(label).width;
                ctx.fillStyle = color + 'aa';
                ctx.font = '10px sans-serif';
                ctx.fillText('\u2601', x + 10 + labelWidth + 5, textY);  // cloud symbol
                ctx.font = 'bold 11px monospace';
                ctx.fillStyle = color;
            }

            // Stats below label
            ctx.font = '9px monospace';
            ctx.fillStyle = color + '88';
            const agentCount = [...this.agents.values()].filter(a => a.sessionId === sessionId).length;
            const toolCount = [...this.toolCards.values()].filter(t => {
                const a = this.agents.get(t.agentId);
                return a && a.sessionId === sessionId;
            }).length;
            let statsText = `${agentCount} agent${agentCount !== 1 ? 's' : ''} \u00b7 ${toolCount} tools`;
            if (isDevContainer) {
                statsText += ' \u00b7 devcontainer';
                if (session.containerName) statsText += ` (${session.containerName})`;
            }
            ctx.fillText(statsText, x + 10, textY + 13);

            // Status indicator dot (active) or dismiss button (inactive)
            if (isActive) {
                const dotX = x + 10 + ctx.measureText(label).width + 8;
                const pulse = 0.4 + Math.sin(this._time * 3) * 0.3;
                ctx.fillStyle = color;
                ctx.globalAlpha = pulse;
                ctx.beginPath();
                ctx.arc(dotX, textY - 4, 3, 0, Math.PI * 2);
                ctx.fill();
                ctx.globalAlpha = 1;
            }

            // Dismiss button (× in top-right corner)
            if (this.multiMode) {
                const btnX = x + w - 18;
                const btnY = y + 6;
                const btnR = 8;
                const isHoverBtn = this._hoveredDismissSession === sessionId;
                ctx.globalAlpha = isHoverBtn ? 0.9 : 0.35;
                ctx.fillStyle = color;
                ctx.font = 'bold 11px monospace';
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillText('\u00d7', btnX, btnY + btnR);
                ctx.globalAlpha = 1;
                // Store bounds for hit testing
                session._dismissBtn = { x: btnX - btnR, y: btnY, w: btnR * 2, h: btnR * 2 };
            }

            ctx.restore();
        }
    }

    // ─── Collab Edges (Inter-Session) ────────────────────────

    _drawCollabEdges(ctx) {
        // Draw edges — either session-to-topic or session-to-session
        for (let edgeIdx = 0; edgeIdx < this.collabEdges.length; edgeIdx++) {
            const edge = this.collabEdges[edgeIdx];
            const sourceSession = this.sessions.get(edge.sourceSessionId);

            // Resolve target: either a session or a topic node
            const isTopicTarget = edge.targetSessionId?.startsWith('topic:');
            const topicNode = isTopicTarget ? this.collabTopicNodes.get(edge.topic) : null;
            const targetSession = isTopicTarget ? null : this.sessions.get(edge.targetSessionId);

            if (!sourceSession) continue;
            if (!targetSession && !topicNode) continue;

            const sourceBounds = this.sim.getGroupBounds(edge.sourceSessionId);
            if (!sourceBounds) continue;

            const sx = sourceBounds.x + sourceBounds.width / 2;
            const sy = sourceBounds.y + sourceBounds.height / 2;
            let tx, ty;

            if (topicNode) {
                tx = topicNode.x;
                ty = topicNode.y;
            } else {
                const targetBounds = this.sim.getGroupBounds(edge.targetSessionId);
                if (!targetBounds) continue;
                tx = targetBounds.x + targetBounds.width / 2;
                ty = targetBounds.y + targetBounds.height / 2;
            }

            // Bezier control
            const dx = tx - sx;
            const dy = ty - sy;
            const spread = (edgeIdx - (this.collabEdges.length - 1) / 2) * 0.08;
            const ctrlX = (sx + tx) / 2 + (-dy * (0.15 + spread));
            const ctrlY = (sy + ty) / 2 + (dx * (0.15 + spread));

            ctx.save();
            ctx.strokeStyle = sourceSession.color + '44';
            ctx.lineWidth = 1.5;
            ctx.setLineDash([10, 6]);
            ctx.lineDashOffset = -this._time * 30;
            ctx.beginPath();
            ctx.moveTo(sx, sy);
            ctx.quadraticCurveTo(ctrlX, ctrlY, tx, ty);
            ctx.stroke();
            ctx.setLineDash([]);

            // Flow particles
            for (let i = 0; i < 2; i++) {
                const t = ((this._time * 0.25 + i / 2) % 1);
                const inv = 1 - t;
                const px = inv * inv * sx + 2 * inv * t * ctrlX + t * t * tx;
                const py = inv * inv * sy + 2 * inv * t * ctrlY + t * t * ty;
                ctx.fillStyle = sourceSession.color;
                ctx.globalAlpha = 0.5;
                ctx.beginPath();
                ctx.arc(px, py, 2.5, 0, Math.PI * 2);
                ctx.fill();
            }

            ctx.restore();
        }

        // Draw topic nodes (hexagonal badges)
        for (const [name, topicNode] of this.collabTopicNodes) {
            const { x, y, sessions } = topicNode;
            if (sessions.size === 0) continue;

            ctx.save();
            ctx.translate(x, y);

            // Hexagonal background
            const r = 16;
            ctx.fillStyle = 'rgba(204, 136, 255, 0.12)';
            ctx.strokeStyle = 'rgba(204, 136, 255, 0.4)';
            ctx.lineWidth = 1;
            ctx.beginPath();
            for (let i = 0; i < 6; i++) {
                const angle = (i / 6) * Math.PI * 2 - Math.PI / 2;
                const hx = Math.cos(angle) * r;
                const hy = Math.sin(angle) * r;
                i === 0 ? ctx.moveTo(hx, hy) : ctx.lineTo(hx, hy);
            }
            ctx.closePath();
            ctx.fill();
            ctx.stroke();

            // Pulsing glow
            const pulse = 0.3 + Math.sin(this._time * 2) * 0.15;
            ctx.save();
            ctx.globalAlpha = pulse;
            ctx.shadowColor = '#cc88ff';
            ctx.shadowBlur = 10;
            ctx.stroke();
            ctx.restore();

            // Topic icon (speech bubble)
            ctx.fillStyle = '#cc88ff';
            ctx.font = '10px monospace';
            ctx.textAlign = 'center';
            ctx.fillText('\u2709', 0, 4); // envelope icon

            // Label below
            ctx.fillStyle = 'rgba(204, 136, 255, 0.8)';
            ctx.font = '9px monospace';
            ctx.fillText(name, 0, r + 14);

            // Session count
            ctx.fillStyle = 'rgba(204, 136, 255, 0.5)';
            ctx.font = '7px monospace';
            ctx.fillText(`${sessions.size} sessions`, 0, r + 24);

            ctx.restore();
        }
    }

    // ─── Collab Detection from Tool Calls ──────────────────

    /** Detect collab topic subscriptions from MCP tool call names */
    _detectCollabFromTool(toolName, inputSummary, sessionId) {
        const name = (toolName || '').toLowerCase();
        // Match collab subscribe, send_message, read_messages
        const isCollabTool = name.includes('collab__subscribe')
            || name.includes('collab__send_message')
            || name.includes('collab__read_messages');
        if (!isCollabTool) return;

        // Extract topic name from inputSummary
        const topic = this._extractCollabTopic(inputSummary);
        console.log(`[CollabDetect] tool=${toolName} summary="${inputSummary}" topic=${topic} session=${sessionId} onCanvas=${this.sessions.has(sessionId)}`);
        if (!topic) return;

        if (!this.collabSubscriptions.has(topic)) {
            this.collabSubscriptions.set(topic, new Set());
        }
        this.collabSubscriptions.get(topic).add(sessionId);
        console.log(`[CollabDetect] topic "${topic}" now has ${this.collabSubscriptions.get(topic).size} sessions: [${[...this.collabSubscriptions.get(topic)].join(', ')}]`);
        this._rebuildCollabEdges();
        console.log(`[CollabDetect] collabEdges: ${this.collabEdges.length}`);
    }

    /** Try to extract a topic name from a collab tool's inputSummary */
    _extractCollabTopic(summary) {
        if (!summary) return null;
        // inputSummary may be like "topic: cronos-api" or just "cronos-api"
        // or JSON-like "{topic: cronos-api, ...}"
        const m = summary.match(/(?:topic[:\s=]+)?["']?([a-zA-Z0-9_-]+)["']?/);
        return m ? m[1] : null;
    }

    /** Rebuild collabEdges from the subscriptions map and create topic nodes */
    _rebuildCollabEdges() {
        const edges = [];
        for (const [topic, sessionIds] of this.collabSubscriptions) {
            const sids = [...sessionIds].filter(s => this.sessions.has(s));
            if (sids.length < 2) continue;

            // Create topic node positioned between connected sessions
            if (!this.collabTopicNodes.has(topic)) {
                this.collabTopicNodes.set(topic, {
                    name: topic,
                    description: '',
                    x: 0, y: 0,
                    sessions: new Set(sids),
                    messageCount: 0,
                });
            }
            const topicNode = this.collabTopicNodes.get(topic);
            topicNode.sessions = new Set(sids);

            // Position topic node at centroid of connected sessions
            let cx = 0, cy = 0, count = 0;
            for (const sid of sids) {
                const bounds = this.sim.getGroupBounds(sid);
                if (bounds) {
                    cx += bounds.x + bounds.width / 2;
                    cy += bounds.y + bounds.height / 2;
                    count++;
                }
            }
            if (count > 0) {
                topicNode.x = cx / count;
                topicNode.y = cy / count;
            }

            // Create edges from each session to the topic node (star topology)
            for (const sid of sids) {
                edges.push({
                    sourceSessionId: sid,
                    targetSessionId: `topic:${topic}`,
                    topic,
                    lastMessageTime: new Date(),
                    opacity: 1.0,
                });
            }
        }

        // Remove stale topic nodes
        for (const [name] of this.collabTopicNodes) {
            if (!this.collabSubscriptions.has(name)) {
                this.collabTopicNodes.delete(name);
            }
        }

        this.collabEdges = edges;
    }

    // ─── Edge Particle Spawning ─────────────────────────────

    _spawnEdgeParticles() {
        const colors = tc();
        for (const edge of this.edges) {
            const sourceNode = this.sim.getNode(edge.sourceId);
            const targetNode = this.sim.getNode(edge.targetId);
            if (!sourceNode || !targetNode) continue;

            const targetAgent = this.agents.get(edge.targetId);
            if (!targetAgent) continue;
            const isActive = targetAgent.state === 'Active' || targetAgent.state === 'ToolCalling' || targetAgent.state === 'Thinking';
            if (!isActive) continue;

            // Only parent-child edges get flow particles
            if (edge.type !== 'parent-child') continue;

            // Cubic bezier control points (matching edge rendering)
            const dx = targetNode.x - sourceNode.x;
            const dy = targetNode.y - sourceNode.y;
            const dist = Math.sqrt(dx * dx + dy * dy) || 1;
            const curvature = dist * 0.15;
            const perpX = (-dy / dist) * curvature;
            const perpY = (dx / dist) * curvature;
            const cp1 = { x: sourceNode.x + dx * 0.33 + perpX, y: sourceNode.y + dy * 0.33 + perpY };
            const cp2 = { x: sourceNode.x + dx * 0.66 + perpX, y: sourceNode.y + dy * 0.66 + perpY };

            // Particle type based on agent state
            const isCalling = targetAgent.state === 'ToolCalling';
            const label = isCalling ? 'tool' : null;
            const color = isCalling ? colors.amber : colors.cyan;
            const speed = isCalling ? 0.5 : 0.35;

            this.edgeParticles.spawn(
                sourceNode, targetNode,
                cp1, cp2,
                { color, speed, label, size: 2.5, wobble: 3 }
            );

            // Bidirectional: return particle from target to source
            if (Math.random() < 0.3) {
                this.edgeParticles.spawn(
                    targetNode, sourceNode,
                    cp2, cp1,
                    { color: colors.green, speed: 0.3, label: 'return', size: 2, wobble: 3 }
                );
            }
        }
    }

    // ─── Agent Rendering ────────────────────────────────────

    _drawAgents(ctx) {
        const theme = getTheme();
        const colors = tc();
        const stateColors = tsc();

        for (const [id, agent] of this.agents) {
            const node = this.sim.getNode(id);
            if (!node) continue;

            const stateColor = stateColors[agent.state] || colors.cyan;
            const baseRadius = node.radius;
            const isSelected = id === this.selectedAgentId;
            const isHovered = id === this.hoveredAgentId;
            const isWaiting = agent.state === 'WaitingPermission';
            const isActive = agent.state === 'Active' || agent.state === 'ToolCalling' || agent.state === 'Thinking';

            // Fade out completed subagents (60s visible, then 3s fade)
            let alpha = 1.0;
            if (!agent.isMain && agent.fadeStart != null) {
                const elapsed = this._time - agent.fadeStart;
                if (elapsed > 60) {
                    alpha = Math.max(0, 1 - (elapsed - 60) / 3);
                }
            }
            if (alpha <= 0) continue;

            // Dim non-matching agents when search/filter is active
            const isSearchMatch = this._isAgentHighlighted(id, agent);
            if ((this.searchTerm || this.highlightedAgentId) && !isSearchMatch) {
                alpha *= 0.2;
            }

            // Breathing animation (agent-flow aligned: subtle)
            const breathe = isWaiting
                ? Math.sin(this._time * 1.2) * 0.08 + 1
                : agent.state === 'Thinking'
                ? Math.sin(this._time * 2) * 0.03 + 1
                : (agent.state === 'Active' || agent.state === 'Idle')
                ? Math.sin(this._time * 0.7) * 0.015 + 1
                : 1;
            const r = baseRadius * breathe;

            ctx.save();
            ctx.globalAlpha = alpha;

            // 1. Depth shadow (subtle, behind hex)
            ctx.save();
            ctx.shadowColor = 'rgba(0, 0, 0, 0.3)';
            ctx.shadowBlur = 10;
            ctx.shadowOffsetX = 2;
            ctx.shadowOffsetY = 3;
            drawHexagon(ctx, node.x, node.y, r * 0.85);
            ctx.fillStyle = 'rgba(10, 15, 40, 0.08)';
            ctx.fill();
            ctx.restore();

            // 2. Glow (pre-cached sprite — subtle)
            const glowAlpha = isHovered || isSelected ? 0.25 : isWaiting ? 0.2 : agent.state === 'Thinking' ? 0.15 : 0.08;
            const glowR = r + 14;
            const sprite = getGlowSprite(stateColor, Math.round(r * 0.4), Math.ceil(glowR), _hexAlpha(glowAlpha));
            ctx.drawImage(sprite, node.x - Math.ceil(glowR), node.y - Math.ceil(glowR));

            // Ambient outer hex ring (very faint)
            drawHexagon(ctx, node.x, node.y, r + 3);
            ctx.strokeStyle = stateColor + '25';
            ctx.lineWidth = 1;
            ctx.stroke();

            // 3. Inner hex fill (semi-transparent, lighter than agent-flow to not look like a dark block)
            drawHexagon(ctx, node.x, node.y, r);
            ctx.fillStyle = 'rgba(10, 15, 40, 0.35)';
            ctx.fill();

            // 4. Scanline effect (agent-flow style — sweeping gradient band)
            // Uses performance.now() for wall-clock accuracy (this._time can drift
            // behind real time when frames drop due to the dt cap) and a triangle
            // wave so the line smoothly bounces up/down instead of teleporting.
            const scanSpeed = agent.state === 'Thinking' || isHovered || isWaiting ? 40 : 15;
            const scanRange = r * 2;
            const scanPhase = (performance.now() / 1000 * scanSpeed) % (scanRange * 2);
            const scanOffset = scanPhase < scanRange ? scanPhase : scanRange * 2 - scanPhase;
            const scanY = node.y - r + scanOffset;
            ctx.save();
            drawHexagon(ctx, node.x, node.y, r);
            ctx.clip();
            const scanGrad = ctx.createLinearGradient(node.x, scanY - 4, node.x, scanY + 4);
            const scanAlpha = isHovered ? '35' : '20';
            scanGrad.addColorStop(0, stateColor + '00');
            scanGrad.addColorStop(0.5, stateColor + scanAlpha);
            scanGrad.addColorStop(1, stateColor + '00');
            ctx.fillStyle = scanGrad;
            ctx.fillRect(node.x - r, scanY - 4, r * 2, 8);
            ctx.restore();

            // 5. State ring (hexagonal outline — thinner, subtler)
            drawHexagon(ctx, node.x, node.y, r);
            ctx.strokeStyle = stateColor;
            ctx.lineWidth = (isSelected || isHovered) ? 2 : 1.5;
            if (agent.state === 'Complete') {
                ctx.setLineDash([4, 4]);
                ctx.strokeStyle = stateColor + '60';
                ctx.lineWidth = 1;
            } else if (isWaiting) {
                ctx.setLineDash([6, 4]);
                ctx.lineDashOffset = -this._time * 25;
                ctx.lineWidth = 2;
            }
            ctx.stroke();
            ctx.setLineDash([]);
            ctx.lineDashOffset = 0;

            // 6. Center icon
            if (isWaiting) {
                // Geometric lock icon
                const s = r * 0.3;
                ctx.save();
                ctx.strokeStyle = stateColor + '90';
                ctx.fillStyle = stateColor + '90';
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.roundRect(node.x - s * 0.6, node.y - s * 0.1, s * 1.2, s * 1.0, 2);
                ctx.fill();
                ctx.beginPath();
                ctx.arc(node.x, node.y - s * 0.15, s * 0.4, Math.PI, 0);
                ctx.stroke();
                ctx.restore();
            } else if (agent.isMain) {
                // Claude spark logo
                ctx.save();
                ctx.translate(node.x, node.y);
                const iconScale = r * 0.03;
                ctx.scale(iconScale, iconScale);
                ctx.fillStyle = stateColor + 'cc';
                ctx.shadowColor = stateColor;
                ctx.shadowBlur = 4 / iconScale;
                if (isActive) ctx.rotate(this._time * 0.3);
                ctx.fill(SPARK_PATH);
                ctx.restore();
            } else {
                // Diamond for subagent
                ctx.fillStyle = stateColor + '90';
                ctx.font = `${r * 0.45}px monospace`;
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.fillText(agent.state === 'ToolCalling' ? '\u2699' : '\u25C7', node.x, node.y);
            }

            // 7. Orbiting particles (thinking state)
            if (agent.state === 'Thinking') {
                for (let i = 0; i < 4; i++) {
                    const angle = this._time * 1.5 + (i / 4) * Math.PI * 2;
                    ctx.beginPath();
                    ctx.fillStyle = stateColor + '80';
                    ctx.arc(
                        node.x + Math.cos(angle) * (r + 12),
                        node.y + Math.sin(angle) * (r + 12),
                        1.5, 0, Math.PI * 2);
                    ctx.fill();
                }
            }

            // 8. Waiting ripples (radar effect)
            if (isWaiting) {
                for (let i = 0; i < 2; i++) {
                    const ripplePhase = ((this._time * 0.65 + i * 0.5) % 1.0);
                    const rippleR = r + 5 + ripplePhase * 45;
                    const rippleAlpha = (1 - ripplePhase) * 0.4;
                    drawHexagon(ctx, node.x, node.y, rippleR);
                    ctx.strokeStyle = stateColor + _hexAlpha(rippleAlpha);
                    ctx.lineWidth = 1.5 * (1 - ripplePhase);
                    ctx.stroke();
                }
                for (let i = 0; i < 3; i++) {
                    const angle = this._time * 0.8 + (i / 3) * Math.PI * 2;
                    ctx.beginPath();
                    ctx.fillStyle = stateColor + '70';
                    ctx.arc(
                        node.x + Math.cos(angle) * (r + 14),
                        node.y + Math.sin(angle) * (r + 14),
                        2, 0, Math.PI * 2);
                    ctx.fill();
                }
            }

            // 9. Agent label
            ctx.fillStyle = isHovered ? '#ffffff' : (theme?.labelColor || colors.brightCyan);
            ctx.font = theme?.labelFont || '10px monospace';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'top';
            ctx.fillText(agent.name, node.x, node.y + r + 8);

            // Context composition: ring for main agent, bar for all
            if (agent.state !== 'Complete' || alpha > 0.5) {
                if (agent.isMain) {
                    this._drawContextRing(ctx, agent, node, r);
                }
                this._drawContextBar(ctx, agent, node, r, alpha);
            }

            // State label above
            if (agent.state !== 'Active' && agent.state !== 'Idle') {
                ctx.fillStyle = stateColor;
                ctx.globalAlpha = alpha * 0.6;
                ctx.font = '7px monospace';
                ctx.textAlign = 'center';
                ctx.fillText(agent.state.toUpperCase(), node.x, node.y - (r + 10));
                ctx.globalAlpha = alpha;
            }

            ctx.restore();
        }
    }

    // ─── Context Ring (main agent, agent-flow style) ──────

    _drawContextRing(ctx, agent, node, radius) {
        const total = agent.tokensUsed || 0;
        if (!agent.tokensMax || total <= 0) return;

        const usage = total / agent.tokensMax;
        const ringR = radius + 8;
        const ringW = 4;
        const startAngle = -Math.PI / 2;

        // Background ring
        ctx.beginPath();
        ctx.arc(node.x, node.y, ringR, 0, Math.PI * 2);
        ctx.strokeStyle = 'rgba(102, 204, 255, 0.06)';
        ctx.lineWidth = ringW;
        ctx.stroke();

        // Filled segments
        const ctxData = agent.context || {};
        const segments = [
            { key: 'systemPrompt', color: '#6666aa' },
            { key: 'userMessages', color: '#4488cc' },
            { key: 'toolResults', color: tc().amber },
            { key: 'reasoning', color: tc().cyan },
            { key: 'subagentResults', color: tc().purple },
        ];
        let currentAngle = startAngle;
        for (const seg of segments) {
            const val = ctxData[seg.key] || 0;
            if (val <= 0) continue;
            const sweep = (val / agent.tokensMax) * Math.PI * 2;
            ctx.beginPath();
            ctx.arc(node.x, node.y, ringR, currentAngle, currentAngle + sweep);
            ctx.strokeStyle = seg.color;
            ctx.lineWidth = ringW;
            ctx.stroke();
            currentAngle += sweep;
        }

        // Warning glow at high usage
        if (usage > 0.8) {
            const warningColor = usage > 0.9 ? tc().red : tc().amber;
            const intensity = usage > 0.9
                ? 0.35 + Math.sin(this._time * 6) * 0.2
                : 0.15 + Math.sin(this._time * 3) * 0.1;
            ctx.save();
            ctx.beginPath();
            ctx.arc(node.x, node.y, ringR + 4, 0, Math.PI * 2);
            ctx.strokeStyle = warningColor;
            ctx.lineWidth = 2;
            ctx.globalAlpha = intensity;
            ctx.shadowColor = warningColor;
            ctx.shadowBlur = 12;
            ctx.stroke();
            ctx.restore();
        }

        // Percentage label when high
        if (usage > 0.7) {
            ctx.font = '7px monospace';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'bottom';
            ctx.fillStyle = usage > 0.9 ? tc().red : usage > 0.8 ? tc().amber : tc().cyan;
            ctx.fillText(`${Math.floor(usage * 100)}%`, node.x, node.y - radius - 10);
        }
    }

    // ─── Context Usage Bar ──────────────────────────────────

    _drawContextBar(ctx, agent, node, radius, alpha) {
        if (!agent.tokensMax || agent.tokensMax <= 0) return;
        const total = agent.tokensUsed || 0;

        const barW = Math.max(60, radius * 2.2);
        const barH = 6;
        const barX = node.x - barW / 2;
        const barY = node.y + radius + 22;
        const colors = tc();

        // Background card
        ctx.fillStyle = 'rgba(10, 15, 40, 0.7)';
        ctx.beginPath();
        ctx.roundRect(barX - 2, barY - 2, barW + 4, barH + 14, 3);
        ctx.fill();

        // Token count label
        ctx.fillStyle = 'rgba(136, 136, 153, 0.8)';
        ctx.font = '7px monospace';
        ctx.textAlign = 'center';
        ctx.fillText(`${formatTokens(total)} / ${formatTokens(agent.tokensMax)} tokens`, node.x, barY + barH + 9);

        // Stacked segments
        const ctxData = agent.context || {};
        const segments = [
            { key: 'systemPrompt', color: '#6666aa' },
            { key: 'userMessages', color: '#4488cc' },
            { key: 'toolResults', color: colors.amber },
            { key: 'reasoning', color: colors.cyan },
            { key: 'subagentResults', color: colors.purple },
        ];
        const maxWidth = barW * Math.min(1, total / agent.tokensMax);
        let xOff = 0;
        for (const seg of segments) {
            const val = ctxData[seg.key] || 0;
            if (val <= 0) continue;
            const segW = (val / total) * maxWidth;
            ctx.fillStyle = seg.color;
            ctx.fillRect(barX + xOff, barY, segW, barH);
            xOff += segW;
        }

        // Remaining capacity
        if (barX + xOff < barX + barW) {
            ctx.fillStyle = 'rgba(102, 204, 255, 0.05)';
            ctx.fillRect(barX + xOff, barY, barX + barW - barX - xOff, barH);
        }

        ctx.strokeStyle = 'rgba(102, 204, 255, 0.15)';
        ctx.lineWidth = 0.5;
        ctx.strokeRect(barX, barY, barW, barH);
    }

    // ─── Tool Card Rendering ────────────────────────────────

    /** Find a clear slot for a tool card using radial ring search (agent-flow style) */
    _findToolSlot(agentNode, agent) {
        const overlaps = (cx, cy) => {
            for (const tc of this.toolCards.values()) {
                if (!tc._bounds) continue;
                // Skip fully faded cards (invisible, about to be GC'd)
                if (tc.fadeStart != null && (this._time - tc.fadeStart) > 5.5) continue;
                if (Math.abs(cx - (tc._bounds.x + tc._bounds.w / 2)) < TOOL_CARD_W &&
                    Math.abs(cy - (tc._bounds.y + tc._bounds.h / 2)) < TOOL_CARD_H) return true;
            }
            return false;
        };

        // Compute outward direction: away from parent (or default upward for main agent)
        let outAngle = -Math.PI / 2;
        if (agent.parentId) {
            const parentNode = this.sim.getNode(agent.parentId);
            if (parentNode) {
                outAngle = Math.atan2(agentNode.y - parentNode.y, agentNode.x - parentNode.x);
            }
        }

        // Arc centered on outward direction, sweeping ±90°
        for (let ring = 1; ring <= TOOL_SLOT.maxRings; ring++) {
            const dist = TOOL_SLOT.baseDistance + ring * TOOL_SLOT.ringIncrement;
            const steps = TOOL_SLOT.baseSteps + ring * TOOL_SLOT.stepsPerRing;
            for (let i = 0; i < steps; i++) {
                const sweep = (i / (steps - 1) - 0.5) * Math.PI;
                const angle = outAngle + sweep;
                const cx = agentNode.x + Math.cos(angle) * dist;
                const cy = agentNode.y + Math.sin(angle) * dist;
                if (!overlaps(cx, cy)) return { x: cx - TOOL_CARD_W / 2, y: cy - TOOL_CARD_H / 2 };
            }
        }
        return {
            x: agentNode.x + Math.cos(outAngle) * TOOL_SLOT.fallbackDistance - TOOL_CARD_W / 2,
            y: agentNode.y + Math.sin(outAngle) * TOOL_SLOT.fallbackDistance - TOOL_CARD_H / 2
        };
    }

    _drawToolCards(ctx) {
        const theme = getTheme();
        const colors = tc();

        for (const [id, card] of this.toolCards) {
            const agent = this.agents.get(card.agentId);
            const agentNode = agent ? this.sim.getNode(card.agentId) : null;
            if (!agentNode) continue;

            const isRunning = card.state === 'Running';
            const isError = card.state === 'Error';
            const isCompleted = !isRunning;

            // Fade out completed cards
            let alpha = 1.0;
            if (card.fadeStart != null) {
                const elapsed = this._time - card.fadeStart;
                if (elapsed > 4) {
                    alpha = Math.max(0, 1 - (elapsed - 4) / 1.5);
                }
                if (alpha <= 0) continue;
            }

            // Dim non-matching tool cards when search/filter is active
            if ((this.searchTerm || this.highlightedAgentId) && !this._isToolHighlighted(card)) {
                alpha *= 0.15;
            }

            // Measure text to get dynamic card width (agent-flow style)
            ctx.font = '8px monospace';
            const cardLabel = getToolCardLabel(card.toolName, card.inputSummary);
            const toolLabel = truncate(`${card.toolName}: ${cardLabel}`, 24);
            const textWidth = Math.min(ctx.measureText(toolLabel).width + 12, TOOL_CARD_W);
            const hasTwoLines = isCompleted && (card.tokenCost || isError);
            // Add spinner width (18px) for running cards so text doesn't overflow
            const w = Math.max(60, textWidth + (isRunning ? 18 : 0));
            const h = hasTwoLines ? 30 : 24;

            // Radial tool slot placement — compute offset once, then follow agent
            if (!card._slotOffset) {
                const slot = this._findToolSlot(agentNode, agent);
                card._slotOffset = { dx: slot.x - agentNode.x, dy: slot.y - agentNode.y };
                // Eagerly set _bounds so subsequent cards in the same render pass
                // see this card's position and avoid overlapping it
                card._bounds = { x: slot.x + (TOOL_CARD_W - w) / 2, y: slot.y, w, h };
            }
            const x = agentNode.x + card._slotOffset.dx + (TOOL_CARD_W - w) / 2;
            const y = agentNode.y + card._slotOffset.dy;

            // Update world-space bounds for hit testing (agent may have moved)
            card._bounds = { x, y, w, h };

            ctx.save();
            ctx.globalAlpha = alpha;

            // Error glow
            if (isError) {
                ctx.shadowColor = colors.red;
                ctx.shadowBlur = 8 + Math.sin(this._time * 6) * 4;
            }

            // Background (stable fill, no flashing)
            const isSelectedCard = this.selectedToolId === card.toolUseId;
            ctx.beginPath();
            ctx.roundRect(x, y, w, h, 4);
            ctx.fillStyle = isError
                ? 'rgba(80, 20, 25, 0.7)'
                : isSelectedCard ? 'rgba(20, 40, 60, 0.6)' : 'rgba(10, 15, 30, 0.7)';
            ctx.fill();

            // Border
            const toolAccent = getToolAccentColor(card.toolName);
            ctx.strokeStyle = isError ? colors.red + '90'
                : isSelectedCard ? colors.cyan + 'aa'
                : isRunning ? toolAccent + '60' : colors.green + '40';
            ctx.lineWidth = isError ? 2 : isSelectedCard ? 1.5 : 1;
            ctx.stroke();
            ctx.shadowBlur = 0;

            // Running indicator: small spinner dot at left edge of card
            if (isRunning) {
                const spX = x + 10;
                const spY = y + h / 2;
                const angle = this._time * 4;
                ctx.strokeStyle = toolAccent;
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.arc(spX, spY, 3.5, angle, angle + Math.PI * 1.3);
                ctx.stroke();
            }

            // Error crack lines
            if (isError) {
                ctx.save();
                ctx.strokeStyle = colors.red + '40';
                ctx.lineWidth = 0.8;
                for (let i = 0; i < 3; i++) {
                    const a = (i / 3) * Math.PI * 2 + 0.5;
                    ctx.beginPath();
                    ctx.moveTo(x + w / 2, y + h / 2);
                    ctx.lineTo(x + w / 2 + Math.cos(a) * w * 0.5, y + h / 2 + Math.sin(a) * h * 0.6);
                    ctx.stroke();
                }
                ctx.restore();
            }

            // Tool label (agent-flow: 8px monospace, centered)
            ctx.font = '8px monospace';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';

            if (isRunning) {
                // Offset text right to avoid spinner overlap (spinner ends at ~x+14)
                const textAreaLeft = x + 18;
                const textCenterX = textAreaLeft + (x + w - textAreaLeft) / 2;
                ctx.fillStyle = toolAccent;
                ctx.fillText(toolLabel, textCenterX, y + h / 2);
            } else if (isError) {
                ctx.fillStyle = colors.red;
                ctx.fillText(truncate(`${card.toolName}: FAILED`, 24), x + w / 2, y + h / 2 - 5);
                ctx.font = '6px monospace';
                ctx.fillStyle = colors.red + 'aa';
                ctx.fillText(truncate(card.error || '', 24), x + w / 2, y + h / 2 + 7);
            } else {
                ctx.fillStyle = colors.green;
                ctx.fillText(toolLabel, x + w / 2, y + h / 2 - (hasTwoLines ? 5 : 0));
                if (card.tokenCost) {
                    ctx.fillStyle = toolAccent + '90';
                    ctx.font = '6px monospace';
                    ctx.fillText(`${card.tokenCost} tok`, x + w / 2, y + h / 2 + 7);
                }
            }

            ctx.restore();
        }
    }

    _gcToolCards() {
        const now = Date.now();
        for (const [id, card] of this.toolCards) {
            // Remove fully faded cards
            if (card.fadeStart != null && (this._time - card.fadeStart) > 7) {
                this.toolCards.delete(id);
                continue;
            }
            // Auto-expire running cards after 2 minutes (missed ToolCallEnd)
            if (card.state === 'Running' && card.startTime && (now - card.startTime.getTime()) > 120000) {
                card.state = 'Complete';
                card.endTime = new Date();
                card.fadeStart = this._time;
            }
        }
    }

    // ─── Event Handling ─────────────────────────────────────

    _resize() {
        this.canvas.width = window.innerWidth * this.dpr;
        this.canvas.height = window.innerHeight * this.dpr;
        this.canvas.style.width = window.innerWidth + 'px';
        this.canvas.style.height = window.innerHeight + 'px';
        this.sim.setCenter(0, 0);
        this._ambientDirty = true;

        // Re-init theme ambient (stars, particles, grids sized to canvas)
        const theme = getTheme();
        if (theme?.initAmbient) {
            theme.initAmbient(this.canvas);
        }
    }

    _bindEvents() {
        window.addEventListener('resize', () => this._resize());

        this.canvas.addEventListener('mousedown', e => this._onMouseDown(e));
        this.canvas.addEventListener('mousemove', e => this._onMouseMove(e));
        this.canvas.addEventListener('mouseup', e => this._onMouseUp(e));
        this.canvas.addEventListener('wheel', e => this._onWheel(e), { passive: false });
        this.canvas.addEventListener('dblclick', e => this._onDoubleClick(e));
        this.canvas.addEventListener('contextmenu', e => this._onContextMenu(e));
        this.canvas.setAttribute('tabindex', '0');
        this.canvas.addEventListener('keydown', e => this._onKeyDown(e));
    }

    _hitTestAgent(worldX, worldY) {
        for (const [id, agent] of this.agents) {
            const node = this.sim.getNode(id);
            if (!node) continue;
            const dx = worldX - node.x;
            const dy = worldY - node.y;
            if (dx * dx + dy * dy < node.radius * node.radius) {
                return id;
            }
        }
        return null;
    }

    _hitTestToolCard(worldX, worldY) {
        for (const [id, card] of this.toolCards) {
            if (!card._bounds) continue;
            const b = card._bounds;
            if (worldX >= b.x && worldX <= b.x + b.w && worldY >= b.y && worldY <= b.y + b.h) {
                return id;
            }
        }
        return null;
    }

    _hitTestSession(worldX, worldY) {
        for (const [id, session] of this.sessions) {
            const sb = session._smoothBounds;
            if (!sb) continue;
            if (worldX >= sb.x && worldX <= sb.x + sb.width &&
                worldY >= sb.y && worldY <= sb.y + sb.height) {
                return id;
            }
        }
        return null;
    }

    _hitTestDismissBtn(worldX, worldY) {
        for (const [id, session] of this.sessions) {
            const btn = session._dismissBtn;
            if (!btn) continue;
            if (worldX >= btn.x && worldX <= btn.x + btn.w &&
                worldY >= btn.y && worldY <= btn.y + btn.h) {
                return id;
            }
        }
        return null;
    }

    _onMouseDown(e) {
        const world = this._screenToWorld(e.clientX, e.clientY);

        // Dismiss button click (× on session boundary)
        if (this.multiMode) {
            const dismissHit = this._hitTestDismissBtn(world.x, world.y);
            if (dismissHit) {
                this.removeSession(dismissHit);
                return;
            }
        }

        // Check tool cards first (they're on top visually)
        const toolHit = this._hitTestToolCard(world.x, world.y);
        if (toolHit) {
            this._clickedToolId = toolHit;
            return; // Handle in mouseUp
        }

        const hitId = this._hitTestAgent(world.x, world.y);

        if (hitId) {
            this._dragNode = this.sim.getNode(hitId);
            this._dragOffset = { x: world.x - this._dragNode.x, y: world.y - this._dragNode.y };
            this._dragging = false; // Will be set true on mousemove
            this.autoFit = false;
        } else {
            this._panStart = { x: e.clientX, y: e.clientY };
            this._panCameraStart = { x: this.camera.targetX, y: this.camera.targetY };
            this.autoFit = false;
        }
    }

    _onMouseMove(e) {
        const world = this._screenToWorld(e.clientX, e.clientY);

        if (this._dragNode) {
            this._dragging = true;
            this._dragNode.x = world.x - this._dragOffset.x;
            this._dragNode.y = world.y - this._dragOffset.y;
            this._dragNode.pinned = true;
            this._dragNode.fx = this._dragNode.x;
            this._dragNode.fy = this._dragNode.y;
            this.sim.reheat();
        } else if (this._panStart) {
            const dx = (e.clientX - this._panStart.x) / this.camera.zoom;
            const dy = (e.clientY - this._panStart.y) / this.camera.zoom;
            this.camera.targetX = this._panCameraStart.x - dx;
            this.camera.targetY = this._panCameraStart.y - dy;
            this.camera.x = this.camera.targetX;
            this.camera.y = this.camera.targetY;
        } else {
            // Hover detection
            const hitId = this._hitTestAgent(world.x, world.y);
            const toolHit = !hitId ? this._hitTestToolCard(world.x, world.y) : null;
            this.hoveredAgentId = hitId;
            // Dismiss button hover
            const dismissHit = this.multiMode ? this._hitTestDismissBtn(world.x, world.y) : null;
            this._hoveredDismissSession = dismissHit;
            this.canvas.style.cursor = (hitId || toolHit || dismissHit) ? 'pointer' : 'grab';
        }
    }

    _onMouseUp(e) {
        // Tool card click
        if (this._clickedToolId) {
            const toolId = this._clickedToolId;
            this._clickedToolId = null;
            this.selectedToolId = toolId;
            this.selectedAgentId = null;
            hideAgentDetail();
            showToolDetail(this, toolId);
            return;
        }

        if (this._dragNode && !this._dragging) {
            // Click (not drag) — select agent
            const world = this._screenToWorld(e.clientX, e.clientY);
            const hitId = this._hitTestAgent(world.x, world.y);
            if (hitId) {
                this.selectedAgentId = hitId;
                this.selectedToolId = null;
                hideToolDetail();
                showAgentDetail(this, hitId);
            }
        } else if (this._panStart && !this._dragging) {
            // Click on empty space — deselect all
            this.selectedAgentId = null;
            this.selectedToolId = null;
            hideAgentDetail();
            hideToolDetail();
        }

        if (this._dragNode) {
            // Unpin after 3 seconds
            const node = this._dragNode;
            setTimeout(() => {
                node.pinned = false;
                node.fx = null;
                node.fy = null;
                this.sim.reheat();
            }, 3000);
        }

        this._dragNode = null;
        this._dragging = false;
        this._panStart = null;
        this._panCameraStart = null;
    }

    _onWheel(e) {
        e.preventDefault();
        const factor = e.deltaY > 0 ? 1 / 1.08 : 1.08;
        const newZoom = Math.max(0.2, Math.min(4.0, this.camera.targetZoom * factor));

        // Zoom toward cursor
        const world = this._screenToWorld(e.clientX, e.clientY);
        const zoomRatio = newZoom / this.camera.zoom;
        this.camera.targetX = world.x - (world.x - this.camera.x) / zoomRatio;
        this.camera.targetY = world.y - (world.y - this.camera.y) / zoomRatio;
        this.camera.targetZoom = newZoom;
        this.autoFit = false;
    }

    _onDoubleClick(e) {
        this._fitToView(true);
        this.autoFit = true;
    }

    _onContextMenu(e) {
        e.preventDefault();
        if (!this.multiMode) return;

        const world = this._screenToWorld(e.clientX, e.clientY);
        // Check agents first, then sessions
        const agentHit = this._hitTestAgent(world.x, world.y);
        const sessionId = agentHit
            ? this.agents.get(agentHit)?.sessionId
            : this._hitTestSession(world.x, world.y);
        if (!sessionId || !this.sessions.has(sessionId)) return;

        this._showSessionContextMenu(e.clientX, e.clientY, sessionId);
    }

    _onKeyDown(e) {
        if (e.key === 'Delete' && this.selectedAgentId && this.multiMode) {
            const agent = this.agents.get(this.selectedAgentId);
            if (agent?.sessionId) {
                this.removeSession(agent.sessionId);
            }
        }
    }

    _showSessionContextMenu(screenX, screenY, sessionId) {
        // Remove existing menu
        this._dismissContextMenu();

        const session = this.sessions.get(sessionId);
        if (!session) return;

        const menu = document.createElement('div');
        menu.className = 'spark-context-menu glass-panel';
        menu.style.left = screenX + 'px';
        menu.style.top = screenY + 'px';

        const removeItem = document.createElement('div');
        removeItem.className = 'spark-context-item';
        removeItem.textContent = `Remove "${session.name}"`;
        removeItem.addEventListener('click', () => {
            this.removeSession(sessionId);
            this._dismissContextMenu();
        });
        menu.appendChild(removeItem);

        document.body.appendChild(menu);
        this._contextMenu = menu;

        // Close on next click anywhere
        const dismiss = (e) => {
            if (!menu.contains(e.target)) {
                this._dismissContextMenu();
            }
        };
        setTimeout(() => document.addEventListener('pointerdown', dismiss, { once: true }), 0);
        this._contextMenuDismiss = dismiss;
    }

    _dismissContextMenu() {
        if (this._contextMenu) {
            this._contextMenu.remove();
            this._contextMenu = null;
        }
    }

    // ─── Public Controls ────────────────────────────────────

    zoomIn() {
        this.camera.targetZoom = Math.min(4.0, this.camera.targetZoom * 1.3);
        this.autoFit = false;
    }

    zoomOut() {
        this.camera.targetZoom = Math.max(0.2, this.camera.targetZoom / 1.3);
        this.autoFit = false;
    }

    fitView() {
        this._fitToView(true);
        this.autoFit = true;
    }

    getSessionDuration() {
        if (this.multiMode) {
            // Show duration of longest active session
            let earliest = null;
            for (const s of this.sessions.values()) {
                if (s.isActive && s.startTime && (!earliest || s.startTime < earliest)) {
                    earliest = s.startTime;
                }
            }
            if (!earliest) return '--:--';
            const elapsed = Math.floor((Date.now() - earliest.getTime()) / 1000);
            const m = Math.floor(elapsed / 60);
            const s = elapsed % 60;
            return `${m}:${s.toString().padStart(2, '0')}`;
        }
        if (!this.sessionStart) return '--:--';
        const elapsed = Math.floor((Date.now() - this.sessionStart.getTime()) / 1000);
        const m = Math.floor(elapsed / 60);
        const s = elapsed % 60;
        return `${m}:${s.toString().padStart(2, '0')}`;
    }
}

// ─── Utilities ──────────────────────────────────────────

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.arcTo(x + w, y, x + w, y + r, r);
    ctx.lineTo(x + w, y + h - r);
    ctx.arcTo(x + w, y + h, x + w - r, y + h, r);
    ctx.lineTo(x + r, y + h);
    ctx.arcTo(x, y + h, x, y + h - r, r);
    ctx.lineTo(x, y + r);
    ctx.arcTo(x, y, x + r, y, r);
    ctx.closePath();
}

function truncate(str, maxLen) {
    if (!str) return '';
    return str.length > maxLen ? str.substring(0, maxLen - 1) + '\u2026' : str;
}

function getModelMaxTokens(model) {
    if (!model) return 200000;
    if (model.includes('opus')) return 1000000;
    if (model.includes('sonnet')) return 1000000;
    if (model.includes('haiku')) return 200000;
    return 200000;
}

/** Internal/meta tools that are filtered from cards and dimmed in feed. */
const META_TOOLS = new Set([
    'taskcreate', 'taskupdate', 'tasklist', 'taskget', 'taskstop', 'taskoutput',
    'sendmessage', 'todoread', 'todowrite',
    'toolsearch', 'exitplanmode', 'enterplanmode',
    'exitworktree', 'enterworktree',
    'askuserquestion',
]);

/** Categorize a tool name for feed display and card coloring. */
function getToolRole(toolName) {
    if (!toolName) return { label: 'TOOL', css: 'tool' };
    const name = toolName.toLowerCase();
    if (META_TOOLS.has(name)) return { label: 'META', css: 'tool-meta', isMeta: true };
    if (name === 'bash') return { label: 'EXEC', css: 'tool-exec' };
    if (name === 'agent' || name === 'task') return { label: 'AGENT', css: 'tool-agent' };
    if (name === 'edit' || name === 'write' || name === 'notebookedit') return { label: 'WRITE', css: 'tool-write' };
    if (name === 'read' || name === 'glob' || name === 'grep') return { label: 'READ', css: 'tool-read' };
    if (name === 'webfetch' || name === 'websearch') return { label: 'WEB', css: 'tool-web' };
    if (name === 'skill') return { label: 'SKILL', css: 'tool-exec' };
    return { label: 'TOOL', css: 'tool' };
}

/** Extract a file path from an inputSummary string (for file attention panel). */
function extractFilePath(summary) {
    if (!summary) return null;
    const parts = summary.split(/[\s\u2192]+/);
    for (const part of parts) {
        // Must contain path separators and look like an actual file path
        if (!part.includes('/') && !part.includes('\\')) continue;
        if (part.length < 5) continue;

        // Reject API/URL paths
        if (part.startsWith('/api/') || part.startsWith('http')) continue;

        // Reject regex/glob patterns with wildcards at root
        if (part.includes('*') && !part.includes('.')) continue;

        // Reject paths ending with / (directories, not files)
        if (part.endsWith('/') || part.endsWith('\\')) continue;

        // Require a file extension (dot in the last segment)
        const lastSeg = part.split(/[/\\]/).pop();
        if (!lastSeg || !lastSeg.includes('.')) continue;

        // Reject short junk (fragments with commas, pipes, quotes)
        if (/[,|"'`{}()]/.test(part)) continue;

        return part;
    }
    return null;
}

/** Get a short display string for tool cards (filename only, not full path). */
function getToolCardLabel(toolName, inputSummary) {
    if (!inputSummary) return toolName || 'tool';
    const summary = inputSummary.trim();

    // For file-oriented tools, extract just the filename from the path
    const name = (toolName || '').toLowerCase();
    if (name === 'read' || name === 'edit' || name === 'write' || name === 'glob' || name === 'grep' || name === 'notebookedit') {
        // inputSummary is like "file_path → description" or just a path
        // Try to find a file path segment and extract the filename
        const parts = summary.split(/[\s\u2192]/); // split on whitespace or →
        for (const part of parts) {
            if (part.includes('/') || part.includes('\\')) {
                const segments = part.split(/[/\\]/);
                const filename = segments[segments.length - 1];
                if (filename) {
                    // Show "..parent/filename" for context
                    const parent = segments.length > 1 ? segments[segments.length - 2] : null;
                    const short = parent ? `..${parent}/${filename}` : filename;
                    // Append any description after the path
                    const afterPath = summary.substring(summary.indexOf(part) + part.length).trim();
                    const desc = afterPath.replace(/^\u2192\s*/, '').trim();
                    return desc ? `${short} ${truncate(desc, 18)}` : short;
                }
            }
        }
    }

    // For Bash, inputSummary usually starts with the description
    if (name === 'bash') {
        return truncate(summary, 28);
    }

    // For Agent/Task, show the prompt/description
    if (name === 'agent' || name === 'task') {
        return truncate(summary, 28);
    }

    return truncate(summary, 28);
}

/** Convert hex color to "r, g, b" string for use in rgba(). */
function hexToRgb(hex) {
    const c = hex.replace('#', '');
    const r = parseInt(c.substring(0, 2), 16);
    const g = parseInt(c.substring(2, 4), 16);
    const b = parseInt(c.substring(4, 6), 16);
    return `${r}, ${g}, ${b}`;
}

/** Get tool card accent color based on tool category. */
function getToolAccentColor(toolName) {
    const colors = tc();
    if (!toolName) return colors.amber;
    const name = toolName.toLowerCase();
    if (name === 'bash') return colors.amber;
    if (name === 'agent' || name === 'task') return colors.purple;
    if (name === 'edit' || name === 'write' || name === 'notebookedit') return colors.green;
    if (name === 'read' || name === 'glob' || name === 'grep') return colors.cyan;
    if (name === 'webfetch' || name === 'websearch') return colors.brightCyan;
    return colors.amber;
}
