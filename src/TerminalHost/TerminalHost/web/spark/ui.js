/**
 * Spark Canvas — UI panels and controls.
 * Glass-morphism panels for agent detail, message feed, and control bar.
 */

// ─── Agent Detail Panel ────────────────────────────────

function showAgentDetail(canvas, agentId) {
    const agent = canvas.agents.get(agentId);
    if (!agent) return;

    const panel = document.getElementById('agentDetail');
    panel.style.display = 'flex';
    // Reset focus button label
    document.getElementById('btnAgentFocus').textContent =
        canvas.highlightedAgentId === agentId ? 'Unfocus' : 'Focus';
    updateAgentDetail(canvas, agentId);
}

function updateAgentDetail(canvas, agentId) {
    const agent = canvas.agents.get(agentId);
    if (!agent) return;

    const stateColor = STATE_COLORS[agent.state] || COLORS.cyan;

    document.getElementById('detailDot').style.background = stateColor;
    document.getElementById('detailDot').style.boxShadow = `0 0 6px ${stateColor}`;
    document.getElementById('detailName').textContent = agent.name;
    document.getElementById('detailState').textContent = agent.state;
    document.getElementById('detailModel').textContent = agent.model || '--';
    document.getElementById('detailTools').textContent = agent.toolCallCount || 0;

    // Time alive
    const start = agent.spawnTime;
    const end = agent.completeTime || new Date();
    const secs = Math.floor((end - start) / 1000);
    const m = Math.floor(secs / 60);
    const s = secs % 60;
    document.getElementById('detailTime').textContent = `${m}:${s.toString().padStart(2, '0')}`;

    // Context bar
    const ctx = agent.context || {};
    const total = (ctx.systemPrompt || 0) + (ctx.userMessages || 0) + (ctx.toolResults || 0) +
                  (ctx.reasoning || 0) + (ctx.subagentResults || 0);
    const max = agent.tokensMax || 200000;

    if (total > 0) {
        document.getElementById('detailContextSection').style.display = 'block';
        const pct = (v) => `${Math.max(0, (v / max) * 100)}%`;
        document.getElementById('ctxSystem').style.width = pct(ctx.systemPrompt || 0);
        document.getElementById('ctxUser').style.width = pct(ctx.userMessages || 0);
        document.getElementById('ctxTools').style.width = pct(ctx.toolResults || 0);
        document.getElementById('ctxReasoning').style.width = pct(ctx.reasoning || 0);
        document.getElementById('ctxSubagent').style.width = pct(ctx.subagentResults || 0);
        document.getElementById('detailContextPct').textContent = `${Math.round((total / max) * 100)}% of ${formatTokens(max)}`;
    } else {
        document.getElementById('detailContextSection').style.display = 'none';
    }

    // Current tool
    const currentToolSection = document.getElementById('detailCurrentTool');
    if (agent.currentToolUseId) {
        const card = canvas.toolCards.get(agent.currentToolUseId);
        if (card) {
            currentToolSection.style.display = 'block';
            document.getElementById('detailCurrentToolName').textContent =
                `${card.toolName} ${truncate(card.inputSummary, 30)}`;
        }
    } else {
        currentToolSection.style.display = 'none';
    }
}

function hideAgentDetail() {
    document.getElementById('agentDetail').style.display = 'none';
}

// ─── Tool Detail Panel ─────────────────────────────────

function showToolDetail(canvas, toolUseId) {
    const card = canvas.toolCards.get(toolUseId);
    if (!card) return;

    const panel = document.getElementById('toolDetail');
    panel.style.display = 'flex';

    const stateColor = card.state === 'Error' ? '#ff5566' : card.state === 'Running' ? '#ffbb44' : '#66ffaa';
    document.getElementById('toolDetailDot').style.background = stateColor;
    document.getElementById('toolDetailDot').style.boxShadow = `0 0 6px ${stateColor}`;
    document.getElementById('toolDetailName').textContent = card.toolName;
    document.getElementById('toolDetailState').textContent = card.state;

    // Agent name
    const agent = canvas.agents.get(card.agentId);
    document.getElementById('toolDetailAgent').textContent = agent?.name || card.agentId?.substring(0, 8) || '--';

    // Input — rich rendering
    const inputEl = document.getElementById('toolDetailInput');
    inputEl.innerHTML = renderToolContent(card.toolName, card.inputSummary, 'input');
    document.getElementById('toolDetailInputSection').style.display = 'block';

    // Result — rich rendering
    const resultSection = document.getElementById('toolDetailResultSection');
    if (card.resultSummary) {
        document.getElementById('toolDetailResult').innerHTML = renderToolContent(card.toolName, card.resultSummary, 'result');
        resultSection.style.display = 'block';
    } else {
        resultSection.style.display = 'none';
    }

    // Error
    const errorSection = document.getElementById('toolDetailErrorSection');
    if (card.error) {
        document.getElementById('toolDetailError').innerHTML = renderToolContent(card.toolName, card.error, 'error');
        errorSection.style.display = 'block';
    } else {
        errorSection.style.display = 'none';
    }

    // Duration
    const start = card.startTime;
    const end = card.endTime || new Date();
    const ms = end - start;
    const durText = ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`;
    document.getElementById('toolDetailDuration').textContent = durText;

    // Token cost
    const tokenRow = document.getElementById('toolDetailTokenRow');
    if (card.tokenCost) {
        document.getElementById('toolDetailTokens').textContent = formatTokens(card.tokenCost);
        tokenRow.style.display = 'flex';
    } else {
        tokenRow.style.display = 'none';
    }
}

// ─── Rich Tool Content Renderer ───────────────────────

/**
 * Render tool content with syntax coloring based on tool type and context.
 * @param {string} toolName
 * @param {string} text
 * @param {'input'|'result'|'error'} context
 * @returns {string} HTML
 */
function renderToolContent(toolName, text, context) {
    if (!text) return '<span class="tc-dim">(no details)</span>';

    const name = (toolName || '').toLowerCase();
    const escaped = escapeHtml(text);

    // Error context: always red monospace
    if (context === 'error') {
        return `<pre class="tc-error">${escaped}</pre>`;
    }

    // File-oriented tools: highlight file path
    if ((name === 'read' || name === 'write' || name === 'edit' || name === 'multiedit' || name === 'notebookedit') && context === 'input') {
        return renderFilePath(escaped);
    }

    // Bash: command styling
    if (name === 'bash') {
        if (context === 'input') {
            return `<pre class="tc-command">${escaped}</pre>`;
        }
        // Result: could be command output — render with line coloring
        return renderCommandOutput(escaped);
    }

    // Grep: pattern + path
    if (name === 'grep' && context === 'input') {
        return renderGrepInput(escaped);
    }

    // Glob: pattern
    if (name === 'glob' && context === 'input') {
        return `<span class="tc-pattern">${escaped}</span>`;
    }

    // Agent/Task: prompt/description
    if ((name === 'agent' || name === 'task') && context === 'input') {
        return `<span class="tc-prompt">${escaped}</span>`;
    }

    // Result: try to detect JSON or diff-like content
    if (context === 'result') {
        if (looksLikeJson(text)) {
            return renderJson(text);
        }
        if (looksLikeDiff(text)) {
            return renderDiff(escaped);
        }
    }

    // Default: plain monospace
    return `<pre class="tc-plain">${escaped}</pre>`;
}

function renderFilePath(escaped) {
    // Split path into directory and filename
    const lastSlash = Math.max(escaped.lastIndexOf('/'), escaped.lastIndexOf('\\'));
    if (lastSlash >= 0) {
        const dir = escaped.substring(0, lastSlash + 1);
        const file = escaped.substring(lastSlash + 1);
        return `<span class="tc-dir">${dir}</span><span class="tc-file">${file}</span>`;
    }
    return `<span class="tc-file">${escaped}</span>`;
}

function renderGrepInput(escaped) {
    // Format: "pattern" in path, or just pattern
    const inIdx = escaped.indexOf(' in ');
    if (inIdx > 0) {
        const pattern = escaped.substring(0, inIdx);
        const path = escaped.substring(inIdx + 4);
        return `<span class="tc-pattern">${pattern}</span><span class="tc-dim"> in </span>${renderFilePath(path)}`;
    }
    return `<span class="tc-pattern">${escaped}</span>`;
}

function renderCommandOutput(escaped) {
    const lines = escaped.split('\n');
    return '<pre class="tc-output">' + lines.map(line => {
        if (line.startsWith('error') || line.startsWith('Error') || line.startsWith('ERR')) {
            return `<span class="tc-line-error">${line}</span>`;
        }
        if (line.startsWith('warning') || line.startsWith('Warning') || line.startsWith('WARN')) {
            return `<span class="tc-line-warn">${line}</span>`;
        }
        return line;
    }).join('\n') + '</pre>';
}

function looksLikeJson(text) {
    const trimmed = text.trim();
    return (trimmed.startsWith('{') || trimmed.startsWith('[')) && (trimmed.includes('"') || trimmed.includes(':'));
}

function looksLikeDiff(text) {
    const lines = text.split('\n');
    let diffLines = 0;
    for (const line of lines.slice(0, 10)) {
        if (line.startsWith('+') || line.startsWith('-') || line.startsWith('@@')) diffLines++;
    }
    return diffLines >= 2;
}

function renderJson(text) {
    try {
        const obj = JSON.parse(text);
        const pretty = JSON.stringify(obj, null, 2);
        return '<pre class="tc-json">' + syntaxColorJson(escapeHtml(pretty)) + '</pre>';
    } catch {
        // Not valid JSON, just show as-is with basic coloring
        return '<pre class="tc-json">' + syntaxColorJson(escapeHtml(text)) + '</pre>';
    }
}

function syntaxColorJson(escaped) {
    return escaped
        .replace(/"([^"]+)"(?=\s*:)/g, '<span class="tc-json-key">"$1"</span>') // keys
        .replace(/:\s*"([^"]*)"/g, ': <span class="tc-json-string">"$1"</span>') // string values
        .replace(/:\s*(\d+\.?\d*)/g, ': <span class="tc-json-number">$1</span>') // numbers
        .replace(/:\s*(true|false|null)/g, ': <span class="tc-json-bool">$1</span>'); // booleans
}

function renderDiff(escaped) {
    const lines = escaped.split('\n');
    return '<pre class="tc-diff">' + lines.map(line => {
        if (line.startsWith('+++') || line.startsWith('---')) {
            return `<span class="tc-diff-header">${line}</span>`;
        }
        if (line.startsWith('+')) {
            return `<span class="tc-diff-add">${line}</span>`;
        }
        if (line.startsWith('-')) {
            return `<span class="tc-diff-del">${line}</span>`;
        }
        if (line.startsWith('@@')) {
            return `<span class="tc-diff-hunk">${line}</span>`;
        }
        return line;
    }).join('\n') + '</pre>';
}

function hideToolDetail() {
    document.getElementById('toolDetail').style.display = 'none';
}

// ─── Message Feed ──────────────────────────────────────

const MAX_FEED_ENTRIES = 30;

/**
 * @param {string} role - Display label (USER, CLAUDE, THINKING, etc.)
 * @param {string} text - Truncated display text
 * @param {string} roleClass - CSS class suffix (user, assistant, thinking, tool, etc.)
 * @param {string} [fullText] - Full untruncated text for hover popup
 * @param {string|number|Date} [timestamp] - Event timestamp for chronological ordering
 */
function addFeedEntry(role, text, roleClass, fullText, timestamp) {
    const feed = document.getElementById('feedContent');

    // Remove empty message
    const empty = feed.querySelector('.feed-empty');
    if (empty) empty.remove();

    const ts = timestamp ? new Date(timestamp).getTime() : Date.now();

    const entry = document.createElement('div');
    entry.className = 'feed-entry';
    entry.dataset.ts = ts;
    entry.innerHTML = `<span class="feed-role feed-role-${roleClass}">${role}</span><span class="feed-text">${escapeHtml(text)}</span>`;

    // Show hover popup with full content if text was truncated
    const full = fullText || text;
    if (full.length > text.length || full.length > 80) {
        entry.addEventListener('mouseenter', (e) => showFeedTooltip(e, role, full, roleClass));
        entry.addEventListener('mouseleave', hideFeedTooltip);
        entry.style.cursor = 'pointer';
    }

    // Insert in chronological order (newest at top).
    // Walk down from top to find the first entry with an older timestamp.
    let inserted = false;
    for (const child of feed.children) {
        const childTs = Number(child.dataset?.ts || 0);
        if (childTs && childTs < ts) {
            feed.insertBefore(entry, child);
            inserted = true;
            break;
        }
    }
    if (!inserted) {
        feed.appendChild(entry);
    }

    // Trim old entries (from bottom = oldest)
    while (feed.children.length > MAX_FEED_ENTRIES) {
        feed.removeChild(feed.lastChild);
    }
}

// ─── Feed / Transcript Hover Tooltip ─────────────────

let _feedTooltip = null;

function _ensureFeedTooltip() {
    if (_feedTooltip) return _feedTooltip;
    _feedTooltip = document.createElement('div');
    _feedTooltip.className = 'feed-tooltip';
    _feedTooltip.style.display = 'none';
    document.body.appendChild(_feedTooltip);
    // Hide on scroll or mouse leaving the tooltip itself
    _feedTooltip.addEventListener('mouseleave', hideFeedTooltip);
    return _feedTooltip;
}

function showFeedTooltip(e, role, fullText, roleClass) {
    const tip = _ensureFeedTooltip();
    tip.innerHTML = `<div class="feed-tooltip-role feed-role-${roleClass}">${role}</div><div class="feed-tooltip-text">${escapeHtml(fullText)}</div>`;
    tip.style.display = 'block';

    // Position near the entry, but keep on screen
    const rect = e.currentTarget.getBoundingClientRect();
    const tipW = Math.min(400, window.innerWidth - 32);
    tip.style.maxWidth = tipW + 'px';

    // Place to the left of the feed panel (or right-aligned to entry)
    let left = rect.left - tipW - 8;
    if (left < 8) left = rect.left;
    let top = rect.top;
    tip.style.left = left + 'px';
    tip.style.top = top + 'px';

    // Adjust if overflowing bottom
    requestAnimationFrame(() => {
        const tipRect = tip.getBoundingClientRect();
        if (tipRect.bottom > window.innerHeight - 8) {
            tip.style.top = Math.max(8, window.innerHeight - tipRect.height - 8) + 'px';
        }
    });
}

function hideFeedTooltip() {
    if (_feedTooltip) _feedTooltip.style.display = 'none';
}

/** Show tooltip for transcript entries (called from rendered HTML) */
function showTranscriptTooltip(e, idx) {
    const entry = transcriptState?.entries?.[idx];
    if (!entry) return;
    const roleClass = entry.type === 'user' ? 'user'
        : entry.type === 'assistant' ? 'assistant'
        : entry.type === 'thinking' ? 'thinking' : 'tool';
    showFeedTooltip(e, entry.type.toUpperCase(), entry.text, roleClass);
}

// ─── Control Bar ───────────────────────────────────────

function updateControlBar(canvas) {
    if (canvas.multiMode) {
        // Multi-session: show observatory stats
        const activeCount = [...canvas.sessions.values()].filter(s => s.isActive).length;
        document.getElementById('sessionName').textContent = `Observatory`;
        document.getElementById('agentCount').textContent = `${canvas.agents.size} agent${canvas.agents.size !== 1 ? 's' : ''}`;
        // Multi-badge
        const badge = document.getElementById('multiBadge');
        if (badge) {
            badge.style.display = '';
            document.getElementById('multiSessionCount').textContent =
                `${canvas.sessions.size} session${canvas.sessions.size !== 1 ? 's' : ''} (${activeCount} live)`;
        }
    } else {
        document.getElementById('sessionName').textContent = canvas.sessionName || 'No session';
        document.getElementById('agentCount').textContent = `${canvas.agents.size} agent${canvas.agents.size !== 1 ? 's' : ''}`;
        const badge = document.getElementById('multiBadge');
        if (badge) badge.style.display = 'none';
    }

    let toolCount = 0;
    for (const agent of canvas.agents.values()) {
        toolCount += agent.toolCallCount || 0;
    }
    document.getElementById('toolCount').textContent = `${toolCount} tools`;
}

function updateDurationDisplay(canvas) {
    document.getElementById('sessionDuration').textContent = canvas.getSessionDuration();
    document.getElementById('zoomLevel').textContent = `${Math.round(canvas.camera.zoom * 100)}%`;
}

function setConnectionStatus(status) {
    const el = document.getElementById('connectionStatus');
    el.textContent = status.toUpperCase();
    el.className = status === 'live' ? 'status-live'
        : status === 'connecting' ? 'status-connecting'
        : status === 'replay' ? 'status-replay'
        : 'status-offline';
}

// ─── Session Picker ────────────────────────────────────

function updateSessionList(sessions) {
    const select = document.getElementById('sessionSelect');
    const currentValue = select.value;

    // Clear all but the placeholder
    while (select.options.length > 1) select.remove(1);

    for (const s of sessions) {
        const opt = document.createElement('option');
        opt.value = s.sessionId;
        const live = s.isLive ? ' \u25CF' : ''; // green dot for live
        const time = s.startTime ? new Date(s.startTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
        opt.textContent = `${s.displayName}${live}  ${time}`;
        opt.title = s.projectPath || s.sessionId;
        select.add(opt);
    }

    // Restore selection if still in list
    if (currentValue && [...select.options].some(o => o.value === currentValue)) {
        select.value = currentValue;
    }
}

function onSessionSelected(sessionId) {
    if (!sessionId) return;
    // Clear previous session before loading new one
    sparkCanvas.clearAll();
    // Notify C# host (if available) and also load directly via API
    notifyHost('selectSession', { sessionId });
    loadAndConnect(sessionId);
}

// ─── Bind Control Buttons ──────────────────────────────

function initControls(canvas) {
    document.getElementById('btnFitView').addEventListener('click', () => canvas.fitView());
    document.getElementById('btnZoomIn').addEventListener('click', () => canvas.zoomIn());
    document.getElementById('btnZoomOut').addEventListener('click', () => canvas.zoomOut());
    document.getElementById('detailClose').addEventListener('click', () => {
        canvas.selectedAgentId = null;
        canvas.highlightedAgentId = null;
        hideAgentDetail();
    });
    document.getElementById('btnAgentFocus').addEventListener('click', () => {
        if (canvas.highlightedAgentId === canvas.selectedAgentId) {
            // Toggle off
            canvas.highlightedAgentId = null;
            document.getElementById('btnAgentFocus').textContent = 'Focus';
        } else {
            canvas.highlightedAgentId = canvas.selectedAgentId;
            document.getElementById('btnAgentFocus').textContent = 'Unfocus';
        }
    });
    document.getElementById('toolDetailClose').addEventListener('click', () => {
        canvas.selectedToolId = null;
        hideToolDetail();
    });

    // Session picker
    document.getElementById('sessionSelect').addEventListener('change', (e) => {
        onSessionSelected(e.target.value);
    });
    document.getElementById('btnRefreshSessions').addEventListener('click', () => {
        notifyHost('refreshSessions');
        discoverSessions(); // Also fetch directly from API
    });

    // Multi-session observatory toggle
    document.getElementById('btnMultiMode').addEventListener('click', () => {
        toggleMultiMode();
    });

    // Canvas search
    document.getElementById('canvasSearch').addEventListener('input', (e) => {
        canvas.setSearch(e.target.value);
    });
    document.getElementById('canvasSearch').addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            e.target.value = '';
            canvas.setSearch('');
            e.target.blur();
        }
    });

    // Theme selector
    document.getElementById('themeSelect').addEventListener('change', (e) => {
        setTheme(e.target.value);
    });

    // Default theme (host will push saved theme via setTheme message on ready)
    setTheme('holographic', true);

    // Initialize Phase 3c panels
    initPanels(canvas);

    // Duration update timer
    setInterval(() => updateDurationDisplay(canvas), 1000);

    // Replay controls
    document.getElementById('btnReplayToggle')?.addEventListener('click', replayToggle);
    document.getElementById('btnReplayRestart')?.addEventListener('click', () => {
        replayRestart();
        replayPlay();
    });
    document.getElementById('replayProgressTrack')?.addEventListener('click', (e) => {
        const rect = e.currentTarget.getBoundingClientRect();
        const fraction = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
        replaySeek(fraction);
    });
    for (const btn of document.querySelectorAll('.replay-speed-btn')) {
        btn.addEventListener('click', (e) => {
            const speed = parseInt(e.target.dataset.speed, 10);
            replaySetSpeed(speed);
            // Update active state
            for (const b of document.querySelectorAll('.replay-speed-btn')) b.classList.remove('active');
            e.target.classList.add('active');
        });
    }
}

// ─── Utilities ─────────────────────────────────────────

function formatTokens(n) {
    if (n >= 1000000) return `${(n / 1000000).toFixed(1)}M`;
    if (n >= 1000) return `${Math.round(n / 1000)}K`;
    return n.toString();
}

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}
