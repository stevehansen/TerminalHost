/**
 * Spark Canvas — Event source and data pipeline.
 * Connects to TerminalHost API via SSE or WebView2 postMessage,
 * loads initial state, and dispatches events to the canvas.
 *
 * Phase 3d: Multi-session observatory mode — load all active sessions,
 * unfiltered SSE, collab polling for inter-session edges.
 */

let sparkCanvas = null;
let apiBase = 'http://localhost:19280';

// ─── Initialization ────────────────────────────────────

function initSpark() {
    const canvasEl = document.getElementById('mainCanvas');
    sparkCanvas = new SparkCanvas(canvasEl);
    initControls(sparkCanvas);

    // Determine data source
    const params = new URLSearchParams(window.location.search);
    const sessionId = params.get('session');
    apiBase = params.get('api') || 'http://localhost:19280';
    const mode = params.get('mode') || 'live';
    const multi = params.get('multi') === '1';

    // Always listen for WebView2 messages
    listenForWebViewMessages();

    if (mode === 'replay') {
        setConnectionStatus('offline');
    } else if (multi) {
        // Start in multi-session mode
        enterMultiMode();
    } else if (sessionId) {
        // Direct session ID — load immediately
        loadAndConnect(sessionId);
    } else {
        // No session specified — try to discover via API, then auto-connect
        setConnectionStatus('connecting');
        discoverSessions();
    }
}

// ─── Session Discovery (direct API) ────────────────────

async function discoverSessions() {
    try {
        const resp = await fetch(`${apiBase}/api/sessions`);
        if (!resp.ok) {
            setConnectionStatus('offline');
            showEmptyState(`API returned ${resp.status}. Is the API server enabled?`);
            return;
        }
        const data = await resp.json();
        const sessions = (data.sessions || []).map(s => ({
            sessionId: s.sessionId,
            displayName: s.workingDirectory ? s.workingDirectory.split(/[/\\]/).filter(Boolean).pop() : 'Session',
            projectPath: s.workingDirectory || '',
            isLive: s.lifecycle === 'Active',
            startTime: s.startTime
        }));

        updateSessionList(sessions);

        // Auto-connect to first active session
        const active = sessions.find(s => s.isLive) || sessions[0];
        if (active) {
            document.getElementById('sessionSelect').value = active.sessionId;
            loadAndConnect(active.sessionId);
        } else {
            setConnectionStatus('offline');
            showEmptyState('No active sessions. Start a Claude Code session to visualize.');
        }
    } catch (err) {
        console.warn('Session discovery failed:', err);
        setConnectionStatus('offline');
        showEmptyState(`Cannot reach API at ${apiBase}. Is the API server enabled in Settings?`);
    }
}

function showEmptyState(message) {
    addFeedEntry('SPARK', message, 'assistant');
}

async function loadAndConnect(sessionId) {
    setConnectionStatus('connecting');
    await loadInitialState(apiBase, sessionId);
    connectSSE(apiBase, sessionId);
}

// ─── REST: Initial State Load ──────────────────────────

async function loadInitialState(apiBase, sessionId) {
    try {
        const resp = await fetch(`${apiBase}/api/sessions/${sessionId}/state`);
        if (!resp.ok) {
            console.warn(`Failed to load state: ${resp.status}`);
            // Session might exist in timeline but not in activity service yet — not fatal
            return;
        }
        const state = await resp.json();
        sparkCanvas.loadState(state);
        setConnectionStatus('live');
    } catch (err) {
        console.error('Failed to load initial state:', err);
    }
}

// ─── SSE: Live Event Stream ────────────────────────────

let sseConnection = null;

function connectSSE(apiBase, sessionId) {
    if (sseConnection) {
        sseConnection.close();
    }

    const url = `${apiBase}/api/events?events=activity.event`;
    sseConnection = new EventSource(url);

    sseConnection.addEventListener('activity.event', (e) => {
        try {
            const envelope = JSON.parse(e.data);
            // Unwrap: ApiEvent { id, type, data: { type, sessionId, ... } }
            const evt = envelope.data || envelope;

            if (sparkCanvas.multiMode) {
                // Multi-mode: accept all session events
                sparkCanvas.processEvent(evt);
            } else {
                // Single-mode: filter to our session
                if (evt.sessionId === sessionId || evt.SessionId === sessionId) {
                    sparkCanvas.processEvent(evt);
                }
            }
        } catch (err) {
            console.warn('Failed to parse SSE event:', err);
        }
    });

    let sseFailCount = 0;

    sseConnection.onopen = () => {
        sseFailCount = 0;
        setConnectionStatus('live');
    };

    sseConnection.onerror = () => {
        sseFailCount++;
        // EventSource auto-reconnects — don't flash status on brief interruptions.
        // Only show offline after multiple consecutive failures (truly disconnected).
        if (sseFailCount >= 3) {
            setConnectionStatus('offline');
        }
        // Don't show 'connecting' flicker at all — just quietly reconnect
    };
}

// ─── Multi-Session Observatory (Phase 3d) ───────────────

let collabPollTimer = null;

async function enterMultiMode() {
    sparkCanvas.clearAll();
    sparkCanvas.setMultiMode(true);
    setConnectionStatus('connecting');

    // Hide session picker in multi-mode (we show all sessions)
    document.getElementById('sessionPicker').style.display = 'none';

    // Update toggle button state
    const btn = document.getElementById('btnMultiMode');
    if (btn) btn.classList.add('active');

    try {
        // Discover all sessions
        const resp = await fetch(`${apiBase}/api/sessions`);
        if (!resp.ok) {
            setConnectionStatus('offline');
            showEmptyState('Cannot reach API for multi-session discovery.');
            return;
        }
        const data = await resp.json();
        const sessions = data.sessions || [];

        if (sessions.length === 0) {
            setConnectionStatus('offline');
            showEmptyState('No sessions found. Start Claude Code sessions to visualize.');
            return;
        }

        addFeedEntry('SPARK', `Loading ${sessions.length} session${sessions.length !== 1 ? 's' : ''}...`, 'assistant');
        console.log('[Multi] Found sessions:', sessions.map(s => `${s.sessionId} (${s.lifecycle || '?'})`));

        // Load state for each session
        let loadedCount = 0;
        const loadPromises = sessions.map(async (s) => {
            // Best display name from the sessions list (always has workingDirectory)
            const listName = s.displayName || s.workingDirectory?.split(/[/\\]/).filter(Boolean).pop() || null;
            try {
                const stateResp = await fetch(`${apiBase}/api/sessions/${s.sessionId}/state`);
                if (stateResp.ok) {
                    const state = await stateResp.json();
                    // Use sessions-list name as fallback if state has no workingDirectory
                    if (!state.workingDirectory && s.workingDirectory) {
                        state.workingDirectory = s.workingDirectory;
                    }
                    console.log(`[Multi] Session ${s.sessionId}: ${Object.keys(state.agents || {}).length} agents, ${Object.keys(state.toolCalls || {}).length} tools`);
                    sparkCanvas.loadState(state);
                    loadedCount++;
                } else {
                    console.log(`[Multi] Session ${s.sessionId}: state returned ${stateResp.status}, creating placeholder`);
                    sparkCanvas._loadSessionState({
                        sessionId: s.sessionId,
                        workingDirectory: s.workingDirectory,
                        startTime: s.startTime,
                        agents: {
                            [s.sessionId]: {
                                id: s.sessionId,
                                name: 'main',
                                isMain: true,
                                state: s.lifecycle === 'Active' ? 'Active' : 'Complete',
                                spawnTime: s.startTime,
                                completeTime: s.lifecycle !== 'Active' ? s.lastActivityTime : null,
                                toolCallCount: s.totalToolCalls || 0,
                            }
                        },
                        toolCalls: {},
                    });
                    loadedCount++;
                }
                // Override name from list if session still has generic "Session" name
                if (listName) {
                    const session = sparkCanvas.sessions.get(s.sessionId);
                    if (session && session.name === 'Session') {
                        session.name = listName;
                    }
                }
            } catch (err) {
                console.warn(`Failed to load session ${s.sessionId}:`, err);
            }
        });

        await Promise.all(loadPromises);
        console.log(`[Multi] Loaded ${loadedCount}/${sessions.length} sessions, canvas has ${sparkCanvas.agents.size} agents`);

        // Deduplicate sessions from the same workspace — keep newest
        deduplicateSessions();

        // Final stabilize and fit all sessions into view
        sparkCanvas.sim.arrangeGroups();
        sparkCanvas.sim.stabilize(120);
        sparkCanvas.fitView();

        // Connect SSE without session filter (null sessionId = accept all)
        connectSSE(apiBase, null);
        setConnectionStatus('live');

        // Periodically dedup sessions (handles new sessions replacing old from same workspace)
        if (!window._dedupTimer) {
            window._dedupTimer = setInterval(() => {
                if (sparkCanvas.multiMode) deduplicateSessions();
            }, 30000);
        }

        addFeedEntry('SPARK', `Observatory: ${sparkCanvas.sessions.size} sessions loaded`, 'assistant');

        // Start collab polling
        startCollabPolling();

    } catch (err) {
        console.warn('Multi-session discovery failed:', err);
        setConnectionStatus('offline');
        showEmptyState(`Cannot reach API at ${apiBase}.`);
    }
}

function exitMultiMode() {
    stopCollabPolling();
    sparkCanvas.clearAll();
    sparkCanvas.setMultiMode(false);

    // Show session picker again
    document.getElementById('sessionPicker').style.display = '';

    const btn = document.getElementById('btnMultiMode');
    if (btn) btn.classList.remove('active');

    setConnectionStatus('connecting');
    discoverSessions();
}

function toggleMultiMode() {
    if (sparkCanvas.multiMode) {
        exitMultiMode();
    } else {
        enterMultiMode();
    }
}

// ─── Collab Polling (Inter-Session Communication) ───────

async function startCollabPolling() {
    stopCollabPolling();
    await pollCollab();
    collabPollTimer = setInterval(pollCollab, 5000);
}

function stopCollabPolling() {
    if (collabPollTimer) {
        clearInterval(collabPollTimer);
        collabPollTimer = null;
    }
}

let collabFailCount = 0;

async function pollCollab() {
    if (!sparkCanvas.multiMode) return;
    // Back off after repeated failures, but retry every ~60s
    if (collabFailCount > 3 && collabFailCount % 12 !== 0) {
        collabFailCount++;
        return;
    }

    try {
        const topicsResp = await fetch(`${apiBase}/api/collab/topics`);
        if (!topicsResp.ok) {
            collabFailCount++;
            return;
        }
        collabFailCount = 0;
        const topicsData = await topicsResp.json();

        // Also fetch collab sessions to map collab names → working directories
        let collabSessions = [];
        try {
            const csResp = await fetch(`${apiBase}/api/collab/sessions`);
            if (csResp.ok) {
                const csData = await csResp.json();
                collabSessions = csData.sessions || [];
            }
        } catch { /* ignore */ }

        const topics = topicsData.topics || topicsData || [];
        const newEdges = [];

        for (const topic of topics) {
            const details = topic.subscriberDetails || [];
            if (details.length < 2) continue;

            // Match each subscriber to a canvas session using enriched identity
            const matchedSessions = [];
            for (const sub of details) {
                let matched = false;

                // 1. Direct match by claudeSessionId (most reliable)
                if (sub.claudeSessionId) {
                    for (const [sid, session] of sparkCanvas.sessions) {
                        // Canvas session IDs are Claude session IDs
                        if (sid === sub.claudeSessionId) {
                            matchedSessions.push(sid);
                            matched = true;
                            break;
                        }
                    }
                }
                if (matched) continue;

                // 2. Match by projectName (case-insensitive)
                const projName = (sub.projectName || sub.name || '').toLowerCase();
                for (const [sid, session] of sparkCanvas.sessions) {
                    if (session.name.toLowerCase() === projName) {
                        matchedSessions.push(sid);
                        matched = true;
                        break;
                    }
                }
                if (matched) continue;

                // 3. Match by workingDir folder name
                if (sub.workingDir) {
                    const folder = sub.workingDir.split(/[/\\]/).filter(Boolean).pop()?.toLowerCase();
                    for (const [sid, session] of sparkCanvas.sessions) {
                        if (session.name.toLowerCase() === folder) {
                            matchedSessions.push(sid);
                            break;
                        }
                    }
                }
            }

            // Deduplicate matched sessions
            const unique = [...new Set(matchedSessions)];

            // Create edges between all pairs of matched sessions
            for (let i = 0; i < unique.length; i++) {
                for (let j = i + 1; j < unique.length; j++) {
                    newEdges.push({
                        sourceSessionId: unique[i],
                        targetSessionId: unique[j],
                        topic: topic.name,
                        lastMessageTime: topic.lastMessageTime ? new Date(topic.lastMessageTime) : null,
                        opacity: 1.0,
                    });
                }
            }
        }

        // Merge REST-discovered edges with event-based edges (don't overwrite)
        sparkCanvas._rebuildCollabEdges(); // rebuild from event-based subscriptions
        // Add REST edges that aren't already covered by event-based ones
        const existingKeys = new Set(sparkCanvas.collabEdges.map(e =>
            `${e.sourceSessionId}|${e.targetSessionId}|${e.topic}`));
        for (const edge of newEdges) {
            const key = `${edge.sourceSessionId}|${edge.targetSessionId}|${edge.topic}`;
            const keyRev = `${edge.targetSessionId}|${edge.sourceSessionId}|${edge.topic}`;
            if (!existingKeys.has(key) && !existingKeys.has(keyRev)) {
                sparkCanvas.collabEdges.push(edge);
            }
        }
        const hadEdges = sparkCanvas.collabEdges.length > 0;
        if (newEdges.length > 0 && !hadEdges) {
            addFeedEntry('COLLAB', `${newEdges.length} connection${newEdges.length !== 1 ? 's' : ''} via ${topics.length} topic${topics.length !== 1 ? 's' : ''}`, 'assistant');
        }

    } catch {
        collabFailCount++;
    }
}

// ─── Session Deduplication ───────────────────────────────

/** Remove duplicate sessions from the same workspace — keep the one with the most activity */
function deduplicateSessions() {
    if (!sparkCanvas.multiMode || sparkCanvas.sessions.size < 2) return;

    // Group sessions by name (workspace folder)
    const byName = new Map();
    for (const [sid, session] of sparkCanvas.sessions) {
        const key = session.name.toLowerCase();
        if (!byName.has(key)) byName.set(key, []);
        byName.get(key).push(sid);
    }

    for (const [name, sids] of byName) {
        if (sids.length < 2) continue;

        // Pick the best session: prefer active, then most agents, then newest
        let bestId = sids[0];
        let bestScore = -1;
        for (const sid of sids) {
            const session = sparkCanvas.sessions.get(sid);
            const agentCount = [...sparkCanvas.agents.values()].filter(a => a.sessionId === sid).length;
            const isActive = session.isActive ? 1000 : 0;
            const score = isActive + agentCount;
            if (score > bestScore) {
                bestScore = score;
                bestId = sid;
            }
        }

        // Remove all but the best
        for (const sid of sids) {
            if (sid === bestId) continue;
            // Remove agents belonging to this session
            for (const [aid, agent] of sparkCanvas.agents) {
                if (agent.sessionId === sid) {
                    sparkCanvas.agents.delete(aid);
                    sparkCanvas.sim.removeNode(aid);
                }
            }
            // Remove tool cards
            for (const [tid, card] of sparkCanvas.toolCards) {
                const agent = sparkCanvas.agents.get(card.agentId);
                if (!agent) sparkCanvas.toolCards.delete(tid);
            }
            sparkCanvas.sessions.delete(sid);
            sparkCanvas.sim.removeGroup(sid);
        }
    }

    sparkCanvas.sim.arrangeGroups();
}

// ─── WebView2 Message Bridge ───────────────────────────

function listenForWebViewMessages() {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (e) => {
            handleHostMessage(e.data);
        });
    }

    window.addEventListener('message', (e) => {
        if (e.data && e.data.type === 'spark') {
            handleHostMessage(e.data.payload);
        }
    });
}

function handleHostMessage(msg) {
    if (!msg) return;

    // PostWebMessageAsString sends a raw string — parse it
    if (typeof msg === 'string') {
        try { msg = JSON.parse(msg); } catch { return; }
    }

    switch (msg.action) {
        case 'loadState':
            sparkCanvas.loadState(msg.state);
            setConnectionStatus('live');
            break;

        case 'event':
            sparkCanvas.processEvent(msg.event);
            break;

        case 'setSession':
            sparkCanvas.sessionId = msg.sessionId;
            sparkCanvas.sessionName = msg.sessionName || '';
            setConnectionStatus('live');
            break;

        case 'connectSSE':
            loadAndConnect(msg.sessionId);
            break;

        case 'setTheme':
            if (msg.theme) setTheme(msg.theme, true);
            break;

        case 'sessionList':
            updateSessionList(msg.sessions || []);
            if (sparkCanvas.sessionId) {
                document.getElementById('sessionSelect').value = sparkCanvas.sessionId;
            }
            break;

        case 'multiMode':
            if (msg.enabled) enterMultiMode();
            else exitMultiMode();
            break;

        case 'clear':
            sparkCanvas.clearAll();
            setConnectionStatus('connecting');
            break;
    }
}

// ─── Notify host of readiness ──────────────────────────

function notifyHost(action, data) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ action, ...data }));
    }
}

// ─── Bootstrap ─────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    initSpark();
    notifyHost('ready');
});
