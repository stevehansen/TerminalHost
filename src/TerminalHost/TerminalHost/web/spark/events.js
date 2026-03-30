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

    // Restore persisted state (panels, filters — NOT multi-mode, that's handled below)
    restoreSparkState();

    // Detect if hosted inside WebView2 (Windows) or Avalonia NativeWebView (macOS)
    const isHosted = !!(window.chrome && window.chrome.webview) || typeof invokeCSharpAction === 'function';

    if (mode === 'replay') {
        setConnectionStatus('offline');
    } else if (isHosted) {
        // Hosted in WebView2/Avalonia — C# host will push state via postMessage.
        // Don't try HTTP discovery (HTTPS→HTTP mixed-content can fail).
        setConnectionStatus('connecting');
        // Restore saved multi-mode: defer until after 'ready' so C# OnCanvasReady runs first
        if (getSavedMultiMode()) {
            setTimeout(() => notifyHost('requestMultiMode'), 200);
        }
    } else if (multi || getSavedMultiMode()) {
        // Start in multi-session mode (from URL param or saved state)
        // API may not be ready yet on startup — retry with backoff
        enterMultiModeWithRetry();
    } else if (sessionId) {
        // Direct session ID — load immediately
        loadAndConnect(sessionId);
    } else {
        // Standalone browser — discover sessions via REST API
        setConnectionStatus('connecting');
        discoverSessions();
    }
}

// ─── Session Discovery (direct API) ────────────────────

async function discoverSessions(maxRetries = 5) {
    for (let attempt = 0; attempt <= maxRetries; attempt++) {
        try {
            const resp = await fetch(`${apiBase}/api/sessions`);
            if (!resp.ok) {
                if (attempt === maxRetries) {
                    setConnectionStatus('offline');
                    showEmptyState(`API returned ${resp.status}. Is the API server enabled?`);
                    return;
                }
            } else {
                const data = await resp.json();
                const sessions = (data.sessions || []).map(s => ({
                    sessionId: s.sessionId,
                    displayName: s.workingDirectory ? s.workingDirectory.split(/[/\\]/).filter(Boolean).pop() : 'Session',
                    projectPath: s.workingDirectory || '',
                    isLive: s.lifecycle === 'Active',
                    startTime: s.startTime
                }));

                updateSessionList(sessions);

                const active = sessions.find(s => s.isLive) || sessions[0];
                if (active) {
                    document.getElementById('sessionSelect').value = active.sessionId;
                    loadAndConnect(active.sessionId);
                } else {
                    setConnectionStatus('offline');
                    showEmptyState('No active sessions. Start a Claude Code session to visualize.');
                }
                return;
            }
        } catch (err) {
            if (attempt === maxRetries) {
                console.warn('Session discovery failed:', err);
                setConnectionStatus('offline');
                showEmptyState(`Cannot reach API at ${apiBase}. Is the API server enabled in Settings?`);
                return;
            }
        }
        // Backoff: 500ms, 1s, 1.5s, 2s, 2.5s
        await new Promise(r => setTimeout(r, Math.min(500 + attempt * 500, 3000)));
    }
}

function showEmptyState(message) {
    addFeedEntry('SPARK', message, 'assistant');
}

async function loadAndConnect(sessionId) {
    sparkCanvas.clearAll();
    replayPause();
    showReplayControls(false);
    setConnectionStatus('connecting');
    await loadInitialState(apiBase, sessionId);
    connectSSE(apiBase, sessionId);
}

// ─── JSONL Replay Mode ─────────────────────────────────

const replay = {
    events: [],
    index: 0,
    speed: 10,          // multiplier: 1=realtime, 0=instant
    playing: false,
    timer: null,
    startWallTime: null, // real clock when play started
    startEventTime: null, // event-time when play started (from current index)
};

function loadReplay(state, events) {
    // Disconnect any live SSE
    if (sseConnection) {
        sseConnection.close();
        sseConnection = null;
    }

    // Stop any existing replay
    replayPause();

    // Load state for agent structure + tool timeline + file panel
    sparkCanvas.loadState(state);

    // Sort events by timestamp and store
    replay.events = (events || []).map(e => ({
        ...e,
        _ts: e.timestamp ? new Date(e.timestamp).getTime() : 0
    })).sort((a, b) => a._ts - b._ts);
    replay.index = 0;

    // Stabilize layout from loaded state
    sparkCanvas.sim.stabilize(100);
    sparkCanvas.fitView();

    setConnectionStatus('replay');
    showReplayControls(true);
    updateReplayProgress();

    const name = state.workingDirectory
        ? state.workingDirectory.split(/[/\\]/).filter(Boolean).pop() || 'Replay'
        : 'Replay';
    const toolCount = Object.keys(state.toolCalls || {}).length;
    const agentCount = Object.keys(state.agents || {}).length;
    addFeedEntry('SPARK', `Loaded: ${name} (${toolCount} tools, ${agentCount} agents, ${replay.events.length} events)`, 'assistant');

    // Auto-start playback
    replayPlay();
}

function replayPlay() {
    if (replay.events.length === 0) return;
    if (replay.index >= replay.events.length) {
        // Restart from beginning
        replayRestart();
    }
    replay.playing = true;
    replay.startWallTime = performance.now();
    replay.startEventTime = replay.events[replay.index]._ts;
    updateReplayButton();
    replayTick();
}

function replayPause() {
    replay.playing = false;
    if (replay.timer) {
        cancelAnimationFrame(replay.timer);
        replay.timer = null;
    }
    updateReplayButton();
}

function replayToggle() {
    if (replay.playing) replayPause();
    else replayPlay();
}

function replayRestart() {
    replayPause();
    replay.index = 0;
    // Clear canvas and reload just the base state (agents from loadState)
    // We keep agent nodes but clear dynamic feed/transcript state
    sparkCanvas.clearFeedAndTranscript?.();
    updateReplayProgress();
}

function replaySetSpeed(speed) {
    const wasPlaying = replay.playing;
    if (wasPlaying) replayPause();
    replay.speed = speed;
    document.getElementById('replaySpeed').textContent = speed === 0 ? 'Max' : `${speed}x`;
    if (wasPlaying) replayPlay();
}

function replayTick() {
    if (!replay.playing || replay.index >= replay.events.length) {
        if (replay.index >= replay.events.length) {
            replay.playing = false;
            updateReplayButton();
            addFeedEntry('SPARK', 'Replay complete', 'assistant');
        }
        return;
    }

    const now = performance.now();

    if (replay.speed === 0) {
        // Instant: dump all remaining events
        while (replay.index < replay.events.length) {
            sparkCanvas.processEvent(replay.events[replay.index]);
            replay.index++;
        }
        updateReplayProgress();
        replay.playing = false;
        updateReplayButton();
        addFeedEntry('SPARK', 'Replay complete', 'assistant');
        return;
    }

    // Calculate how far ahead in event-time we should be
    const wallElapsed = now - replay.startWallTime;
    const eventTimeTarget = replay.startEventTime + (wallElapsed * replay.speed);

    // Process all events up to the target time
    let processed = 0;
    while (replay.index < replay.events.length) {
        const evt = replay.events[replay.index];
        if (evt._ts > eventTimeTarget) break;
        sparkCanvas.processEvent(evt);
        replay.index++;
        processed++;
        // Batch limit: yield to renderer every 50 events
        if (processed >= 50) break;
    }

    updateReplayProgress();

    if (replay.index >= replay.events.length) {
        replay.playing = false;
        updateReplayButton();
        addFeedEntry('SPARK', 'Replay complete', 'assistant');
        return;
    }

    replay.timer = requestAnimationFrame(replayTick);
}

function replaySeek(fraction) {
    const wasPlaying = replay.playing;
    replayPause();

    // Clear feed/transcript for clean replay
    sparkCanvas.clearFeedAndTranscript?.();

    const targetIndex = Math.min(Math.floor(fraction * replay.events.length), replay.events.length);

    // Reset and replay up to target
    replay.index = 0;
    while (replay.index < targetIndex) {
        sparkCanvas.processEvent(replay.events[replay.index]);
        replay.index++;
    }
    updateReplayProgress();

    if (wasPlaying && replay.index < replay.events.length) replayPlay();
}

function updateReplayProgress() {
    const total = replay.events.length;
    const current = replay.index;
    const pct = total > 0 ? (current / total) * 100 : 0;
    const bar = document.getElementById('replayProgressFill');
    const label = document.getElementById('replayProgressLabel');
    if (bar) bar.style.width = `${pct}%`;
    if (label) label.textContent = `${current}/${total}`;
}

function updateReplayButton() {
    const btn = document.getElementById('btnReplayToggle');
    if (btn) btn.textContent = replay.playing ? '\u23F8' : '\u25B6'; // ⏸ or ▶
}

function showReplayControls(show) {
    const el = document.getElementById('replayBar');
    if (el) el.style.display = show ? '' : 'none';
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
        // But don't downgrade if host is pushing events via postMessage.
        const isHosted = !!(window.chrome && window.chrome.webview) || typeof invokeCSharpAction === 'function';
        if (sseFailCount >= 3 && !isHosted) {
            setConnectionStatus('offline');
        } else if (sseFailCount >= 3 && isHosted) {
            // SSE failed but host pushes events — close SSE silently, stay live
            sseConnection?.close();
            sseConnection = null;
        }
    };
}

// ─── Multi-Session Observatory (Phase 3d) ───────────────

let collabPollTimer = null;

/** Enter multi-mode with retries — used on startup when API may not be ready yet */
async function enterMultiModeWithRetry(maxRetries = 8) {
    setConnectionStatus('connecting');
    for (let attempt = 0; attempt < maxRetries; attempt++) {
        try {
            const resp = await fetch(`${apiBase}/api/sessions`);
            if (resp.ok) {
                // API is ready — enter multi-mode normally
                await enterMultiMode();
                return;
            }
        } catch { /* API not ready yet */ }
        // Backoff: 500ms, 1s, 1.5s, 2s, 2.5s, 3s, 3s, 3s
        await new Promise(r => setTimeout(r, Math.min(500 + attempt * 500, 3000)));
    }
    // All retries exhausted — fall back to single-session discovery
    console.warn('[Spark] Multi-mode API not reachable after retries, falling back to single-session');
    discoverSessions();
}

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
                        source: s.source,
                        containerName: s.containerName,
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
                const session = sparkCanvas.sessions.get(s.sessionId);
                if (session) {
                    if (listName && session.name === 'Session') session.name = listName;
                    // Propagate source metadata from session list
                    if (s.source) session.source = s.source;
                    if (s.containerName) session.containerName = s.containerName;
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

/** Enter multi-mode from host-pushed session states (no HTTP needed) */
function enterMultiModeFromHost(sessions) {
    sparkCanvas.clearAll();
    sparkCanvas.setMultiMode(true);

    document.getElementById('sessionPicker').style.display = 'none';
    const btn = document.getElementById('btnMultiMode');
    if (btn) btn.classList.add('active');

    if (sessions.length === 0) {
        setConnectionStatus('connecting');
        addFeedEntry('SPARK', 'No sessions found. Start Claude Code sessions to visualize.', 'assistant');
        saveSparkState();
        return;
    }

    addFeedEntry('SPARK', `Loading ${sessions.length} session${sessions.length !== 1 ? 's' : ''}...`, 'assistant');

    for (const state of sessions) {
        sparkCanvas.loadState(state);
        // Override name from workingDirectory if still generic
        const session = sparkCanvas.sessions.get(state.sessionId);
        if (session && session.name === 'Session' && state.workingDirectory) {
            const name = state.workingDirectory.split(/[/\\]/).filter(Boolean).pop();
            if (name) session.name = name;
        }
    }

    deduplicateSessions();
    sparkCanvas.sim.arrangeGroups();
    sparkCanvas.sim.stabilize(120);
    sparkCanvas.fitView();

    setConnectionStatus('live');
    addFeedEntry('SPARK', `Observatory: ${sparkCanvas.sessions.size} sessions loaded`, 'assistant');
    saveSparkState();
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
    const isHosted = !!(window.chrome && window.chrome.webview) || typeof invokeCSharpAction === 'function';

    if (sparkCanvas.multiMode) {
        if (isHosted) {
            // Tell host to exit multi-mode and push single-session state
            notifyHost('exitMultiMode');
            sparkCanvas.clearAll();
            sparkCanvas.setMultiMode(false);
            document.getElementById('sessionPicker').style.display = '';
            const btn = document.getElementById('btnMultiMode');
            if (btn) btn.classList.remove('active');
            setConnectionStatus('connecting');
            // Host will push single-session state via OnCanvasReady flow
            notifyHost('ready');
        } else {
            exitMultiMode();
        }
    } else {
        if (isHosted) {
            notifyHost('requestMultiMode');
        } else {
            enterMultiMode();
        }
    }
    saveSparkState();
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

    // Group sessions by name + source (devcontainer and local sessions with same name coexist)
    const byName = new Map();
    for (const [sid, session] of sparkCanvas.sessions) {
        const key = session.name.toLowerCase() + '|' + (session.source || 'Local');
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

// ─── Host Message Bridge (WebView2 + Avalonia NativeWebView) ─

function listenForWebViewMessages() {
    // WebView2 (Windows): chrome.webview.postMessage/addEventListener
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (e) => {
            handleHostMessage(e.data);
        });
    }

    // Generic postMessage fallback
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
            setConnectionStatus(msg.sessionId ? 'live' : 'connecting');
            break;

        case 'connectSSE':
            // Host already pushed state via loadState — just start SSE stream
            // (don't clearAll/re-fetch, which would wipe the state and fail on mixed-content)
            if (msg.apiBase) apiBase = msg.apiBase;
            sparkCanvas.sessionId = msg.sessionId;
            connectSSE(apiBase, msg.sessionId);
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

        case 'loadReplay':
            loadReplay(msg.state, msg.events);
            break;

        case 'multiMode':
            if (msg.enabled) enterMultiMode();
            else exitMultiMode();
            break;

        case 'loadMultiState':
            // Host-pushed multi-session observatory (no HTTP needed)
            enterMultiModeFromHost(msg.sessions || []);
            break;

        case 'clear':
            sparkCanvas.clearAll();
            setConnectionStatus('connecting');
            break;
    }
}

// ─── Notify host of readiness ──────────────────────────

function notifyHost(action, data) {
    const msg = JSON.stringify({ action, ...data });
    // WebView2 (Windows)
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(msg);
    }
    // Avalonia NativeWebView (macOS) — invokeCSharpAction is injected by the control
    else if (typeof invokeCSharpAction === 'function') {
        invokeCSharpAction(msg);
    }
}

// ─── Bootstrap ─────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    initSpark();
    notifyHost('ready');
});
