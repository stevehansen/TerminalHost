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

class SparkCanvas {
    constructor(canvasEl) {
        this.canvas = canvasEl;
        this.ctx = canvasEl.getContext('2d');
        this.sim = new ForceSimulation();
        this.dpr = window.devicePixelRatio || 1;

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
        this._lastFrame = 0;

        // Session info (single-session mode)
        this.sessionId = null;
        this.sessionName = '';
        this.sessionStart = null;

        // Multi-session observatory (Phase 3d)
        this.multiMode = false;
        this.sessions = new Map();     // sessionId -> { id, name, projectPath, startTime, isActive, color, agentCount, toolCount }
        this._sessionColorIdx = 0;
        this.collabEdges = [];         // { sourceSessionId, targetSessionId, topic, lastMessageTime, opacity }

        // Search/filter (Phase 3c)
        this.searchTerm = '';
        this.highlightedAgentId = null;  // Agent filter — highlight one agent

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
        this._animate(0);
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
    _registerSession(sessionId, name, projectPath, startTime, isActive) {
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
        };
        this.sessions.set(sessionId, session);
        this.sim.setGroup(sessionId, 0, 0);
        this.sim.arrangeGroups();
        return session;
    }

    /** Load initial state from a SessionActivityState object */
    loadState(state) {
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
        const sessionId = state.sessionId;
        const name = state.workingDirectory
            ? state.workingDirectory.split(/[/\\]/).filter(Boolean).pop() || 'Session'
            : 'Session';

        this._registerSession(sessionId, name, state.workingDirectory, state.startTime, true);

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
        this._sessionColorIdx = 0;
        this.sessionId = null;
        this.sessionName = '';
        this.sessionStart = null;
    }

    /** Process a single ActivityEvent */
    processEvent(evt) {
        const type = evt.type || evt.Type;
        const data = evt.data || evt.Data || {};
        const sessionId = evt.sessionId || evt.SessionId;

        // Multi-mode: ensure session is registered
        if (this.multiMode && sessionId) {
            if (!this.sessions.has(sessionId)) {
                const name = (data.cwd || '').split(/[/\\]/).filter(Boolean).pop() || 'Session';
                this._registerSession(sessionId, name, data.cwd, null, true);
            }
        }

        switch (type) {
            case 'SessionStart':
                if (this.multiMode) {
                    const name = (data.cwd || '').split(/[/\\]/).filter(Boolean).pop() || 'Session';
                    this._registerSession(sessionId, name, data.cwd, new Date(), true);
                } else {
                    this.sessionId = sessionId;
                    this.sessionStart = evt.timestamp ? new Date(evt.timestamp || evt.Timestamp) : new Date();
                    this.sessionName = (data.cwd || '').split(/[/\\]/).filter(Boolean).pop() || 'Session';
                }
                break;

            case 'AgentSpawn': {
                const agentId = data.agentId || sessionId;
                this._addAgent(agentId, {
                    id: agentId,
                    name: data.name || (data.isMain ? 'main' : `agent-${this.agents.size}`),
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
                    agent.fadeStart = this._time;
                    const node = this.sim.getNode(agentId);
                    if (node) {
                        node.state = 'Complete';
                        // Complete FX
                        this.fx.triggerComplete(node.x, node.y, tc().green, node.radius);
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

                // Update agent state
                const agentId = evt.agentId || data.agentId || sessionId;
                this._ensureAgent(agentId, sessionId);
                const agent = this.agents.get(agentId);
                if (agent && agent.currentToolUseId === data.toolUseId) {
                    agent.state = 'Active';
                    agent.currentToolUseId = null;
                    if (data.tokenCost) {
                        agent.tokensUsed = (agent.tokensUsed || 0) + data.tokenCost;
                        agent.context.toolResults += data.tokenCost;
                    }
                }

                // Panel data: update timeline
                recordTimelineEvent(agentId, data.toolUseId, data.toolName, data.error ? 'Error' : 'Complete', null, new Date());
                break;
            }

            case 'UserMessage':
                addFeedEntry('USER', truncate(data.content, 120), 'user');
                // Show bubble on main agent
                if (this.sessionId) {
                    this.bubbles.add(this.sessionId, data.content || '', 'user');
                }
                recordTranscriptEntry(this.sessionId || 'main', 'user', data.content || '');
                break;

            case 'AssistantMessage':
                addFeedEntry('CLAUDE', truncate(data.content, 120), 'assistant');
                if (this.sessionId) {
                    this.bubbles.add(this.sessionId, data.content || '', 'assistant');
                }
                recordTranscriptEntry(this.sessionId || 'main', 'assistant', data.content || '');
                break;

            case 'ThinkingBlock':
                addFeedEntry('THINKING', truncate(data.content, 80), 'thinking');
                if (this.sessionId) {
                    this.bubbles.add(this.sessionId, data.content || '', 'thinking');
                }
                recordTranscriptEntry(this.sessionId || 'main', 'thinking', data.content || '');
                break;

            case 'SessionEnd':
            case 'SessionTimeout':
                for (const [id, agent] of this.agents) {
                    // In multi-mode, only affect agents belonging to this session
                    if (this.multiMode && agent.sessionId !== sessionId) continue;
                    if (agent.state !== 'Complete' && agent.state !== 'Error') {
                        agent.state = type === 'SessionTimeout' ? 'TimedOut' : 'Complete';
                        agent.completeTime = new Date();
                        agent.fadeStart = this._time;
                        const node = this.sim.getNode(id);
                        if (node) {
                            this.fx.triggerComplete(node.x, node.y,
                                type === 'SessionTimeout' ? tc().gray : tc().green,
                                node.radius);
                        }
                    }
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

        this.toolCards.set(tc.toolUseId, {
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
        });

        addFeedEntry(toolRole.label, `${tc.toolName} ${truncate(tc.inputSummary, 60)}`, toolRole.css);
    }

    // ─── Camera ─────────────────────────────────────────────

    _fitToView(smooth = true) {
        const bounds = this.sim.getBounds(120);
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

    _animate(timestamp) {
        const dt = Math.min((timestamp - this._lastFrame) / 1000, 0.05);
        this._lastFrame = timestamp;
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

        requestAnimationFrame(t => this._animate(t));
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
        const activeColor = theme?.edgeActiveColor || 'rgba(102, 204, 255, 0.30)';
        const dimColor = theme?.edgeColor || 'rgba(102, 204, 255, 0.12)';

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

            // Bezier control point
            const dx = targetNode.x - sourceNode.x;
            const dy = targetNode.y - sourceNode.y;
            const midX = (sourceNode.x + targetNode.x) / 2;
            const midY = (sourceNode.y + targetNode.y) / 2;
            const perpX = -dy * 0.15;
            const perpY = dx * 0.15;
            const ctrlX = midX + perpX;
            const ctrlY = midY + perpY;

            ctx.save();
            ctx.globalAlpha = edgeAlpha;

            if (isActive && isParentChild) {
                // Tapered edge: wider at source, narrower at target
                this._drawTaperedEdge(ctx, sourceNode, targetNode, ctrlX, ctrlY,
                    isParentChild ? 4 : 2,  // source width
                    isParentChild ? 1 : 0.5, // target width
                    activeColor);
            } else {
                // Dashed animation for inactive/waiting edges
                const color = isActive ? activeColor : dimColor;
                ctx.strokeStyle = color;
                ctx.lineWidth = isParentChild ? 1.5 : 0.8;

                if (!isActive) {
                    // Animated dashes
                    const dashOffset = this._time * 20;
                    ctx.setLineDash([6, 8]);
                    ctx.lineDashOffset = -dashOffset;
                }

                ctx.beginPath();
                ctx.moveTo(sourceNode.x, sourceNode.y);
                ctx.quadraticCurveTo(ctrlX, ctrlY, targetNode.x, targetNode.y);
                ctx.stroke();
                ctx.setLineDash([]);
            }

            ctx.restore();
        }
    }

    /** Draw a tapered bezier edge as a filled polygon */
    _drawTaperedEdge(ctx, source, target, ctrlX, ctrlY, startWidth, endWidth, color) {
        const steps = 16;
        const leftPoints = [];
        const rightPoints = [];

        for (let i = 0; i <= steps; i++) {
            const t = i / steps;
            const inv = 1 - t;

            // Bezier point
            const bx = inv * inv * source.x + 2 * inv * t * ctrlX + t * t * target.x;
            const by = inv * inv * source.y + 2 * inv * t * ctrlY + t * t * target.y;

            // Tangent
            const tx = 2 * inv * (ctrlX - source.x) + 2 * t * (target.x - ctrlX);
            const ty = 2 * inv * (ctrlY - source.y) + 2 * t * (target.y - ctrlY);
            const len = Math.sqrt(tx * tx + ty * ty) || 1;

            // Normal (perpendicular)
            const nx = -ty / len;
            const ny = tx / len;

            // Width at this point (linear taper)
            const w = startWidth * (1 - t) + endWidth * t;

            leftPoints.push({ x: bx + nx * w, y: by + ny * w });
            rightPoints.push({ x: bx - nx * w, y: by - ny * w });
        }

        // Draw filled polygon
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(leftPoints[0].x, leftPoints[0].y);
        for (let i = 1; i < leftPoints.length; i++) {
            ctx.lineTo(leftPoints[i].x, leftPoints[i].y);
        }
        for (let i = rightPoints.length - 1; i >= 0; i--) {
            ctx.lineTo(rightPoints[i].x, rightPoints[i].y);
        }
        ctx.closePath();
        ctx.fill();

        // Bright core line
        ctx.strokeStyle = color;
        ctx.lineWidth = 0.5;
        ctx.globalAlpha = (ctx.globalAlpha || 1) * 0.5;
        ctx.beginPath();
        ctx.moveTo(source.x, source.y);
        ctx.quadraticCurveTo(ctrlX, ctrlY, target.x, target.y);
        ctx.stroke();
    }

    // ─── Session Boundaries (Multi-Mode) ──────────────────────

    _drawSessionBoundaries(ctx) {
        for (const [sessionId, session] of this.sessions) {
            const bounds = this.sim.getGroupBounds(sessionId, 60);
            if (!bounds) continue;

            const { x, y, width: w, height: h } = bounds;
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

            // Stats below label
            ctx.font = '9px monospace';
            ctx.fillStyle = color + '88';
            const agentCount = [...this.agents.values()].filter(a => a.sessionId === sessionId).length;
            const toolCount = [...this.toolCards.values()].filter(t => {
                const a = this.agents.get(t.agentId);
                return a && a.sessionId === sessionId;
            }).length;
            ctx.fillText(`${agentCount} agent${agentCount !== 1 ? 's' : ''} · ${toolCount} tools`, x + 10, textY + 13);

            // Status indicator dot
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

            ctx.restore();
        }
    }

    // ─── Collab Edges (Inter-Session) ────────────────────────

    _drawCollabEdges(ctx) {
        for (const edge of this.collabEdges) {
            const sourceSession = this.sessions.get(edge.sourceSessionId);
            const targetSession = this.sessions.get(edge.targetSessionId);
            if (!sourceSession || !targetSession) continue;

            const sourceBounds = this.sim.getGroupBounds(edge.sourceSessionId);
            const targetBounds = this.sim.getGroupBounds(edge.targetSessionId);
            if (!sourceBounds || !targetBounds) continue;

            // Center points of each session
            const sx = sourceBounds.x + sourceBounds.width / 2;
            const sy = sourceBounds.y + sourceBounds.height / 2;
            const tx = targetBounds.x + targetBounds.width / 2;
            const ty = targetBounds.y + targetBounds.height / 2;

            // Bezier control
            const dx = tx - sx;
            const dy = ty - sy;
            const ctrlX = (sx + tx) / 2 + (-dy * 0.2);
            const ctrlY = (sy + ty) / 2 + (dx * 0.2);

            // Gradient edge from source color to target color
            const gradient = ctx.createLinearGradient(sx, sy, tx, ty);
            gradient.addColorStop(0, sourceSession.color + '44');
            gradient.addColorStop(1, targetSession.color + '44');

            ctx.save();
            ctx.strokeStyle = gradient;
            ctx.lineWidth = 2;
            ctx.setLineDash([10, 6]);
            ctx.lineDashOffset = -this._time * 30;
            ctx.beginPath();
            ctx.moveTo(sx, sy);
            ctx.quadraticCurveTo(ctrlX, ctrlY, tx, ty);
            ctx.stroke();
            ctx.setLineDash([]);

            // Topic label at midpoint
            if (edge.topic) {
                const mx = (sx + tx) / 2;
                const my = (sy + ty) / 2;
                ctx.fillStyle = 'rgba(255,255,255,0.5)';
                ctx.font = '8px monospace';
                ctx.textAlign = 'center';
                ctx.fillText(edge.topic, mx, my - 6);
            }

            // Animated flow particles along collab edge
            const numParticles = 3;
            for (let i = 0; i < numParticles; i++) {
                const t = ((this._time * 0.3 + i / numParticles) % 1);
                const inv = 1 - t;
                const px = inv * inv * sx + 2 * inv * t * ctrlX + t * t * tx;
                const py = inv * inv * sy + 2 * inv * t * ctrlY + t * t * ty;
                ctx.fillStyle = sourceSession.color;
                ctx.globalAlpha = 0.6;
                ctx.beginPath();
                ctx.arc(px, py, 3, 0, Math.PI * 2);
                ctx.fill();
            }

            ctx.restore();
        }
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

            // Bezier control point
            const dx = targetNode.x - sourceNode.x;
            const dy = targetNode.y - sourceNode.y;
            const ctrlX = (sourceNode.x + targetNode.x) / 2 + (-dy * 0.15);
            const ctrlY = (sourceNode.y + targetNode.y) / 2 + (dx * 0.15);

            // Particle type based on agent state
            const isCalling = targetAgent.state === 'ToolCalling';
            const label = isCalling ? 'tool' : null;
            const color = isCalling ? colors.amber : colors.cyan;
            const speed = isCalling ? 0.5 : 0.35;

            this.edgeParticles.spawn(
                sourceNode, targetNode,
                { x: ctrlX, y: ctrlY },
                { color, speed, label, size: 2.5, wobble: 5 }
            );

            // Bidirectional: return particle from target to source
            if (Math.random() < 0.3) {
                this.edgeParticles.spawn(
                    targetNode, sourceNode,
                    { x: ctrlX, y: ctrlY },
                    { color: colors.green, speed: 0.3, label: 'return', size: 2, wobble: 4 }
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
            const radius = node.radius;
            const isSelected = id === this.selectedAgentId;
            const isHovered = id === this.hoveredAgentId;

            // Fade out completed subagents (60s visible, then 3s fade)
            // Main agent never fades — it anchors the graph
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

            ctx.save();
            ctx.globalAlpha = alpha;
            ctx.translate(node.x, node.y);

            // Breathing animation
            let breathScale = 1.0;
            if (agent.state === 'Active' || agent.state === 'Idle') {
                breathScale = 1 + Math.sin(this._time * 2) * 0.02;
            } else if (agent.state === 'Thinking') {
                breathScale = 1 + Math.sin(this._time * 4) * 0.04;
            } else if (agent.state === 'ToolCalling') {
                breathScale = 1 + Math.sin(this._time * 6) * 0.015;
            }
            ctx.scale(breathScale, breathScale);

            // Bloom glow (multi-layer for richer effect)
            const glowSize = isSelected ? 35 : (isHovered ? 28 : 20);
            const isActive = agent.state === 'Active' || agent.state === 'ToolCalling' || agent.state === 'Thinking';

            // Outermost bloom (very faint, large)
            if (isActive || isSelected) {
                const bloom = ctx.createRadialGradient(0, 0, radius * 0.2, 0, 0, radius + glowSize + 10);
                bloom.addColorStop(0, stateColor + '18');
                bloom.addColorStop(0.5, stateColor + '08');
                bloom.addColorStop(1, stateColor + '00');
                ctx.fillStyle = bloom;
                ctx.beginPath();
                ctx.arc(0, 0, radius + glowSize + 10, 0, Math.PI * 2);
                ctx.fill();
            }

            // Inner glow
            const gradient = ctx.createRadialGradient(0, 0, radius * 0.3, 0, 0, radius + glowSize);
            gradient.addColorStop(0, stateColor + '50');
            gradient.addColorStop(0.6, stateColor + '20');
            gradient.addColorStop(1, stateColor + '00');
            ctx.fillStyle = gradient;
            ctx.beginPath();
            ctx.arc(0, 0, radius + glowSize, 0, Math.PI * 2);
            ctx.fill();

            // Node fill with subtle inner gradient
            const fillGrad = ctx.createRadialGradient(0, -radius * 0.3, radius * 0.1, 0, 0, radius);
            fillGrad.addColorStop(0, stateColor + '12');
            fillGrad.addColorStop(1, colors.nodeFill);
            ctx.fillStyle = fillGrad;
            ctx.beginPath();
            ctx.arc(0, 0, radius, 0, Math.PI * 2);
            ctx.fill();

            // State ring (double ring for active)
            ctx.strokeStyle = stateColor;
            ctx.lineWidth = isSelected ? 2.5 : 1.5;
            ctx.beginPath();
            ctx.arc(0, 0, radius, 0, Math.PI * 2);
            ctx.stroke();

            // Secondary ring (active agents pulse)
            if (isActive) {
                const pulseRadius = radius + 4 + Math.sin(this._time * 3) * 2;
                ctx.strokeStyle = stateColor;
                ctx.lineWidth = 0.5;
                ctx.globalAlpha = alpha * (0.3 + Math.sin(this._time * 3) * 0.15);
                ctx.beginPath();
                ctx.arc(0, 0, pulseRadius, 0, Math.PI * 2);
                ctx.stroke();
                ctx.globalAlpha = alpha;
            }

            // Orbiting dots for ToolCalling state
            if (agent.state === 'ToolCalling') {
                const orbitR = radius + 8;
                for (let d = 0; d < 3; d++) {
                    const angle = this._time * 4 + d * (Math.PI * 2 / 3);
                    const dx = Math.cos(angle) * orbitR;
                    const dy = Math.sin(angle) * orbitR;
                    ctx.fillStyle = stateColor;
                    ctx.globalAlpha = alpha * 0.6;
                    ctx.beginPath();
                    ctx.arc(dx, dy, 2, 0, Math.PI * 2);
                    ctx.fill();
                }
                ctx.globalAlpha = alpha;
            }

            // Center icon
            ctx.save();
            const iconScale = radius * 0.035;
            ctx.scale(iconScale, iconScale);
            ctx.fillStyle = '#ffffff';
            if (agent.isMain) {
                // Rotating spark for active main agent
                if (isActive) {
                    ctx.rotate(this._time * 0.3);
                }
                ctx.fill(SPARK_PATH);
            } else {
                // Diamond for subagent
                ctx.beginPath();
                ctx.moveTo(0, -7);
                ctx.lineTo(7, 0);
                ctx.lineTo(0, 7);
                ctx.lineTo(-7, 0);
                ctx.closePath();
                ctx.fill();
            }
            ctx.restore();

            // Context usage bar (stacked, below agent)
            this._drawContextBar(ctx, agent, radius, alpha);

            // State label (above agent name)
            if (agent.state !== 'Active' && agent.state !== 'Idle') {
                ctx.fillStyle = stateColor;
                ctx.globalAlpha = alpha * 0.6;
                ctx.font = '7px monospace';
                ctx.textAlign = 'center';
                ctx.fillText(agent.state.toUpperCase(), 0, -(radius + 8));
                ctx.globalAlpha = alpha;
            }

            // Label below
            const labelY = radius + (agent.isMain ? 30 : 20);
            ctx.fillStyle = theme?.labelColor || colors.brightCyan;
            ctx.font = theme?.labelFont || '10px monospace';
            ctx.textAlign = 'center';
            ctx.fillText(agent.name, 0, labelY);

            ctx.restore();
        }
    }

    // ─── Context Usage Bar ──────────────────────────────────

    _drawContextBar(ctx, agent, radius, alpha) {
        if (!agent.tokensMax || agent.tokensMax <= 0) return;
        const total = agent.tokensUsed || 0;
        if (total <= 0) return;

        const pct = Math.min(1, total / agent.tokensMax);
        const barW = radius * 2.4;
        const barH = 4;
        const barX = -barW / 2;
        const barY = radius + 10;
        const colors = tc();

        // Background track
        ctx.fillStyle = 'rgba(255,255,255,0.06)';
        roundRect(ctx, barX, barY, barW, barH, 2);
        ctx.fill();

        // Threshold glow
        if (pct > 0.8) {
            const glowColor = pct > 0.9 ? colors.red : colors.amber;
            const pulse = 0.5 + Math.sin(this._time * (pct > 0.9 ? 6 : 3)) * 0.3;
            ctx.save();
            ctx.globalAlpha = alpha * pulse * 0.4;
            ctx.shadowColor = glowColor;
            ctx.shadowBlur = 8;
            ctx.fillStyle = glowColor;
            roundRect(ctx, barX, barY, barW * pct, barH, 2);
            ctx.fill();
            ctx.restore();
            ctx.globalAlpha = alpha;
        }

        // Stacked segments
        const ctxData = agent.context || {};
        const segments = [
            { key: 'systemPrompt', color: '#6666aa' },
            { key: 'userMessages', color: '#4488cc' },
            { key: 'toolResults', color: colors.amber },
            { key: 'reasoning', color: colors.cyan },
            { key: 'subagentResults', color: colors.purple },
        ];

        let xOff = 0;
        for (const seg of segments) {
            const val = ctxData[seg.key] || 0;
            if (val <= 0) continue;
            const segW = (val / agent.tokensMax) * barW;
            ctx.fillStyle = seg.color;
            ctx.globalAlpha = alpha * 0.7;
            roundRect(ctx, barX + xOff, barY, Math.max(1, segW), barH, xOff === 0 ? 2 : 0);
            ctx.fill();
            xOff += segW;
        }
        ctx.globalAlpha = alpha;

        // Percentage label when above 70%
        if (pct > 0.7) {
            const labelColor = pct > 0.9 ? colors.red : pct > 0.8 ? colors.amber : colors.cyan;
            ctx.fillStyle = labelColor;
            ctx.font = '7px monospace';
            ctx.textAlign = 'center';
            ctx.fillText(`${Math.round(pct * 100)}%`, 0, barY + barH + 9);
        }
    }

    // ─── Tool Card Rendering ────────────────────────────────

    _drawToolCards(ctx) {
        const theme = getTheme();
        const colors = tc();

        for (const [id, card] of this.toolCards) {
            const agent = this.agents.get(card.agentId);
            const agentNode = agent ? this.sim.getNode(card.agentId) : null;
            if (!agentNode) continue;

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

            // Position: offset from agent node (max 6 visible, compact if more)
            const visibleCards = [...this.toolCards.values()]
                .filter(c => c.agentId === card.agentId && (c.state === 'Running' || (c.fadeStart && this._time - c.fadeStart < 5.5)));
            const cardIdx = visibleCards.indexOf(card);
            const maxVisible = 6;
            if (cardIdx >= maxVisible) continue;

            const isCompleted = card.state !== 'Running';
            const isError = card.state === 'Error';
            const hasTwoLines = isCompleted && (card.resultSummary || card.error || card.tokenCost);

            const spacing = visibleCards.length > 4 ? 26 : 34;
            const offsetX = agentNode.radius + 20;
            const offsetY = -10 + cardIdx * spacing;

            const x = agentNode.x + offsetX;
            const y = agentNode.y + offsetY;
            const w = 220;
            const h = hasTwoLines ? 34 : 24;

            // Store world-space bounds for hit testing
            card._bounds = { x, y, w, h };

            ctx.save();
            ctx.globalAlpha = alpha;

            // Error pulsing glow
            if (isError) {
                const pulse = 0.4 + Math.sin(this._time * 5) * 0.3;
                ctx.save();
                ctx.globalAlpha = alpha * pulse;
                ctx.shadowColor = colors.red;
                ctx.shadowBlur = 12;
                ctx.fillStyle = 'rgba(255, 85, 102, 0.15)';
                roundRect(ctx, x - 2, y - 2, w + 4, h + 4, 6);
                ctx.fill();
                ctx.restore();
                ctx.globalAlpha = alpha;

                // Crack lines for errors
                if (card.fadeStart == null || this._time - card.fadeStart < 2) {
                    ctx.save();
                    ctx.strokeStyle = colors.red;
                    ctx.lineWidth = 0.8;
                    ctx.globalAlpha = alpha * 0.5;
                    const cx = x + w / 2;
                    const cy = y + h / 2;
                    for (let i = 0; i < 3; i++) {
                        const angle = (i / 3) * Math.PI * 2 + 0.5;
                        const len = 8 + Math.random() * 12;
                        ctx.beginPath();
                        ctx.moveTo(cx, cy);
                        ctx.lineTo(
                            cx + Math.cos(angle) * len + Math.sin(i * 7.3) * 3,
                            cy + Math.sin(angle) * len + Math.cos(i * 5.7) * 3
                        );
                        ctx.stroke();
                    }
                    ctx.restore();
                    ctx.globalAlpha = alpha;
                }
            }

            // Highlight if selected
            const isSelectedCard = this.selectedToolId === card.toolUseId;
            if (isSelectedCard) {
                ctx.strokeStyle = tc().brightCyan;
                ctx.lineWidth = 2;
                roundRect(ctx, x - 2, y - 2, w + 4, h + 4, 6);
                ctx.stroke();
            }

            // Background
            ctx.fillStyle = isError ? 'rgba(255, 85, 102, 0.12)' : 'rgba(10, 15, 30, 0.7)';
            roundRect(ctx, x, y, w, h, 4);
            ctx.fill();

            // Border — color by tool type when running, by state when done
            const toolAccent = getToolAccentColor(card.toolName);
            const borderColor = theme?.toolCardBorder
                ? theme.toolCardBorder(card.state)
                : (card.state === 'Running' ? toolAccent
                    : isError ? colors.red
                    : colors.green);
            ctx.strokeStyle = borderColor;
            ctx.lineWidth = isError ? 2 : 1;
            roundRect(ctx, x, y, w, h, 4);
            ctx.stroke();

            // Spinning ring indicator for running tools
            if (card.state === 'Running') {
                const cx = x + 10;
                const cy = y + h / 2;
                const angle = this._time * 3;
                ctx.strokeStyle = toolAccent;
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.arc(cx, cy, 5, angle, angle + Math.PI * 1.2);
                ctx.stroke();
            }

            // State icon for completed/error
            if (isCompleted) {
                ctx.fillStyle = isError ? colors.red : colors.green;
                ctx.font = '10px monospace';
                ctx.fillText(isError ? '\u2716' : '\u2714', x + 6, y + 14);
            }

            // Tool name + short label (first line)
            ctx.fillStyle = '#aaeeff';
            ctx.font = '8px monospace';
            const textX = card.state === 'Running' ? x + 20 : x + 18;
            const cardLabel = getToolCardLabel(card.toolName, card.inputSummary);
            ctx.fillText(truncate(`${card.toolName} ${cardLabel}`, 30), textX, y + 14);

            // Second line: result/error summary or token cost
            if (hasTwoLines) {
                ctx.font = '7px monospace';
                if (isError && card.error) {
                    ctx.fillStyle = 'rgba(255, 85, 102, 0.7)';
                    ctx.fillText(truncate(card.error, 35), x + 18, y + 26);
                } else if (card.resultSummary) {
                    ctx.fillStyle = 'rgba(102, 255, 170, 0.5)';
                    ctx.fillText(truncate(card.resultSummary, 35), x + 18, y + 26);
                } else if (card.tokenCost) {
                    ctx.fillStyle = 'rgba(102, 204, 255, 0.5)';
                    ctx.fillText(`${formatTokens(card.tokenCost)} tokens`, x + 18, y + 26);
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
    }

    _bindEvents() {
        window.addEventListener('resize', () => this._resize());

        this.canvas.addEventListener('mousedown', e => this._onMouseDown(e));
        this.canvas.addEventListener('mousemove', e => this._onMouseMove(e));
        this.canvas.addEventListener('mouseup', e => this._onMouseUp(e));
        this.canvas.addEventListener('wheel', e => this._onWheel(e), { passive: false });
        this.canvas.addEventListener('dblclick', e => this._onDoubleClick(e));
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

    _onMouseDown(e) {
        const world = this._screenToWorld(e.clientX, e.clientY);

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
            this.canvas.style.cursor = (hitId || toolHit) ? 'pointer' : 'grab';
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
