/**
 * Spark Canvas — Phase 3c Panels.
 * Timeline/Gantt, File Attention, Transcript, and visibility filter controls.
 */

// ─── Visibility Filters ─────────────────────────────

const filters = {
    showCards: true,
    showEdges: true,
    showBubbles: true,
};

function getFilters() { return filters; }

// ─── Timeline / Gantt Panel ─────────────────────────

const timelineState = {
    visible: false,
    /** @type {{ agentId: string, toolUseId: string, toolName: string, start: Date, end: Date|null, state: string }[]} */
    events: [],
    scrollX: 0,
    pixelsPerSecond: 8,
    canvas: null,
    ctx: null,
};

function toggleTimelinePanel() {
    timelineState.visible = !timelineState.visible;
    const panel = document.getElementById('timelinePanel');
    panel.style.display = timelineState.visible ? 'flex' : 'none';
    document.getElementById('btnToggleTimeline').classList.toggle('active', timelineState.visible);
    if (timelineState.visible) {
        initTimelineCanvas();
        renderTimeline();
    }
}

function initTimelineCanvas() {
    if (timelineState.canvas) return;
    timelineState.canvas = document.getElementById('timelineCanvas');
    timelineState.ctx = timelineState.canvas.getContext('2d');

    // Resize observer
    const body = document.getElementById('timelineBody');
    new ResizeObserver(() => {
        const dpr = window.devicePixelRatio || 1;
        timelineState.canvas.width = body.clientWidth * dpr;
        timelineState.canvas.height = body.clientHeight * dpr;
        timelineState.canvas.style.width = body.clientWidth + 'px';
        timelineState.canvas.style.height = body.clientHeight + 'px';
        renderTimeline();
    }).observe(body);

    // Horizontal scroll
    body.addEventListener('wheel', (e) => {
        if (e.shiftKey || Math.abs(e.deltaX) > Math.abs(e.deltaY)) {
            timelineState.scrollX -= (e.deltaX || e.deltaY);
        } else {
            // Zoom timeline
            const factor = e.deltaY > 0 ? 1 / 1.1 : 1.1;
            timelineState.pixelsPerSecond = Math.max(1, Math.min(60, timelineState.pixelsPerSecond * factor));
        }
        timelineState.scrollX = Math.min(0, timelineState.scrollX);
        renderTimeline();
        e.preventDefault();
    }, { passive: false });
}

/** Record a tool call event for the timeline */
function recordTimelineEvent(agentId, toolUseId, toolName, state, startTime, endTime) {
    // Find existing or create new
    let existing = timelineState.events.find(e => e.toolUseId === toolUseId);
    if (existing) {
        existing.state = state;
        existing.end = endTime || existing.end;
    } else {
        timelineState.events.push({
            agentId, toolUseId, toolName,
            start: startTime || new Date(),
            end: endTime || null,
            state: state || 'Running',
        });
    }
    if (timelineState.visible) renderTimeline();
}

function renderTimeline() {
    const canvas = timelineState.canvas;
    const ctx = timelineState.ctx;
    if (!canvas || !ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const w = canvas.width / dpr;
    const h = canvas.height / dpr;
    const pps = timelineState.pixelsPerSecond;

    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);

    if (!sparkCanvas || !sparkCanvas.sessionStart) {
        ctx.fillStyle = '#556677';
        ctx.font = '11px monospace';
        ctx.fillText('No session data', 20, h / 2);
        return;
    }

    const sessionStart = sparkCanvas.sessionStart.getTime();
    const now = Date.now();
    const rowH = 24;
    const labelW = 80;
    const scrollX = timelineState.scrollX;

    // Build agent rows
    const agentIds = [...sparkCanvas.agents.keys()];
    const colors = tc();
    const stateColors = tsc();

    // Time axis
    ctx.fillStyle = 'rgba(255,255,255,0.04)';
    ctx.fillRect(labelW, 0, w - labelW, h);

    // Grid lines (every 10s, 30s, 60s depending on zoom)
    const interval = pps > 15 ? 10 : pps > 5 ? 30 : 60;
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.lineWidth = 0.5;
    ctx.font = '8px monospace';
    ctx.fillStyle = 'rgba(255,255,255,0.3)';

    const startOffset = Math.floor(-scrollX / (pps * interval)) * interval;
    for (let t = startOffset; t * pps + scrollX < w - labelW; t += interval) {
        const x = labelW + t * pps + scrollX;
        if (x < labelW) continue;
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, h);
        ctx.stroke();
        const mins = Math.floor(t / 60);
        const secs = t % 60;
        ctx.fillText(`${mins}:${secs.toString().padStart(2, '0')}`, x + 2, 10);
    }

    // "Now" line
    const nowX = labelW + ((now - sessionStart) / 1000) * pps + scrollX;
    if (nowX >= labelW && nowX <= w) {
        ctx.strokeStyle = colors.cyan;
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        ctx.moveTo(nowX, 0);
        ctx.lineTo(nowX, h);
        ctx.stroke();
        ctx.setLineDash([]);
    }

    // Agent rows
    for (let i = 0; i < agentIds.length; i++) {
        const agentId = agentIds[i];
        const agent = sparkCanvas.agents.get(agentId);
        if (!agent) continue;

        const y = 14 + i * rowH;

        // Row label
        ctx.fillStyle = stateColors[agent.state] || colors.cyan;
        ctx.font = '9px monospace';
        ctx.fillText(truncate(agent.name, 10), 4, y + rowH / 2 + 3);

        // Row separator
        ctx.strokeStyle = 'rgba(255,255,255,0.05)';
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        ctx.moveTo(labelW, y + rowH);
        ctx.lineTo(w, y + rowH);
        ctx.stroke();

        // Agent lifespan bar
        const aStart = (agent.spawnTime.getTime() - sessionStart) / 1000;
        const aEnd = agent.completeTime ? (agent.completeTime.getTime() - sessionStart) / 1000 : (now - sessionStart) / 1000;
        const barX = labelW + aStart * pps + scrollX;
        const barW = Math.max(2, (aEnd - aStart) * pps);

        if (barX + barW > labelW && barX < w) {
            ctx.fillStyle = (stateColors[agent.state] || colors.cyan) + '18';
            ctx.fillRect(Math.max(labelW, barX), y + 2, Math.min(barW, w - Math.max(labelW, barX)), rowH - 4);
        }

        // Tool call blocks within this agent
        const agentEvents = timelineState.events.filter(e => e.agentId === agentId);
        for (const evt of agentEvents) {
            const tStart = (evt.start.getTime() - sessionStart) / 1000;
            const tEnd = evt.end ? (evt.end.getTime() - sessionStart) / 1000 : (now - sessionStart) / 1000;
            const bx = labelW + tStart * pps + scrollX;
            const bw = Math.max(2, (tEnd - tStart) * pps);

            if (bx + bw < labelW || bx > w) continue;

            const toolColor = getToolAccentColor(evt.toolName);
            const blockColor = evt.state === 'Error' ? colors.red
                : evt.state === 'Running' ? toolColor
                : toolColor;

            ctx.fillStyle = blockColor + '88';
            const clampX = Math.max(labelW, bx);
            const clampW = Math.min(bw, w - clampX);
            ctx.fillRect(clampX, y + 5, clampW, rowH - 10);

            // Tool name label if block is wide enough
            if (clampW > 30) {
                ctx.fillStyle = '#ffffff';
                ctx.font = '7px monospace';
                ctx.fillText(truncate(evt.toolName, Math.floor(clampW / 5)), clampX + 2, y + rowH / 2 + 2);
            }
        }
    }
}

// ─── File Attention Panel ───────────────────────────

const fileState = {
    visible: false,
    /** @type {Map<string, {path:string, reads:number, writes:number, agents:Set<string>}>} */
    files: new Map(),
};

function toggleFilePanel() {
    fileState.visible = !fileState.visible;
    // Close transcript if opening files (mutually exclusive on left side)
    if (fileState.visible && transcriptState.visible) {
        transcriptState.visible = false;
        document.getElementById('transcriptPanel').style.display = 'none';
        document.getElementById('btnToggleTranscript').classList.remove('active');
    }
    // Close agent/tool detail panels
    if (fileState.visible) {
        hideAgentDetail();
        hideToolDetail();
    }
    const panel = document.getElementById('filePanel');
    panel.style.display = fileState.visible ? 'flex' : 'none';
    document.getElementById('btnToggleFiles').classList.toggle('active', fileState.visible);
    if (fileState.visible) renderFilePanel();
}

/** Record a file access */
function recordFileAccess(filePath, toolName, agentId) {
    if (!filePath) return;
    let entry = fileState.files.get(filePath);
    if (!entry) {
        entry = { path: filePath, reads: 0, writes: 0, agents: new Set() };
        fileState.files.set(filePath, entry);
    }
    const name = (toolName || '').toLowerCase();
    if (name === 'edit' || name === 'write' || name === 'notebookedit') {
        entry.writes++;
    } else {
        entry.reads++;
    }
    if (agentId) entry.agents.add(agentId);

    if (fileState.visible) renderFilePanel();
}

function renderFilePanel() {
    const body = document.getElementById('filePanelBody');
    if (!body) return;

    // Sort by total access count descending
    const sorted = [...fileState.files.values()].sort((a, b) => (b.reads + b.writes) - (a.reads + a.writes));

    if (sorted.length === 0) {
        body.innerHTML = '<div class="file-empty">No file activity yet</div>';
        return;
    }

    const maxCount = Math.max(1, sorted[0].reads + sorted[0].writes);
    const colors = tc();

    let html = '';
    for (const f of sorted.slice(0, 50)) {
        const total = f.reads + f.writes;
        const pct = (total / maxCount) * 100;
        // Heat color: cyan (low) -> amber (medium) -> red (high)
        const heat = pct > 70 ? colors.red : pct > 40 ? colors.amber : colors.cyan;
        const fileName = f.path.split(/[/\\]/).pop() || f.path;
        const dirPath = f.path.substring(0, f.path.length - fileName.length);

        const statsLabel = f.writes > 0
            ? `${f.reads} read · ${f.writes} write · ${f.agents.size} agent${f.agents.size !== 1 ? 's' : ''}`
            : `${f.reads} read · ${f.agents.size} agent${f.agents.size !== 1 ? 's' : ''}`;

        html += `<div class="file-row" title="${escapeHtml(f.path)}">
            <div class="file-bar" style="width:${pct}%; background:${heat}30;"></div>
            <span class="file-name" style="color:${heat}">${escapeHtml(fileName)}</span>
            <span class="file-dir">${escapeHtml(dirPath)}</span>
            <span class="file-stats">${statsLabel}</span>
        </div>`;
    }
    body.innerHTML = html;
}

// ─── Transcript Panel ───────────────────────────────

const transcriptState = {
    visible: false,
    /** @type {{ time: Date, agentId: string, type: string, text: string }[]} */
    entries: [],
    searchTerm: '',
};

function toggleTranscriptPanel() {
    transcriptState.visible = !transcriptState.visible;
    // Close files if opening transcript (mutually exclusive on left side)
    if (transcriptState.visible && fileState.visible) {
        fileState.visible = false;
        document.getElementById('filePanel').style.display = 'none';
        document.getElementById('btnToggleFiles').classList.remove('active');
    }
    // Close agent/tool detail panels
    if (transcriptState.visible) {
        hideAgentDetail();
        hideToolDetail();
    }
    const panel = document.getElementById('transcriptPanel');
    panel.style.display = transcriptState.visible ? 'flex' : 'none';
    document.getElementById('btnToggleTranscript').classList.toggle('active', transcriptState.visible);
    if (transcriptState.visible) renderTranscriptPanel();
}

/** Record a transcript entry */
function recordTranscriptEntry(agentId, type, text) {
    transcriptState.entries.push({
        time: new Date(),
        agentId: agentId || 'main',
        type, // 'user', 'assistant', 'thinking', 'tool'
        text,
    });
    // Cap at 500 entries
    if (transcriptState.entries.length > 500) {
        transcriptState.entries.shift();
    }
    if (transcriptState.visible) renderTranscriptPanel();
}

function renderTranscriptPanel() {
    const body = document.getElementById('transcriptBody');
    if (!body) return;

    const search = transcriptState.searchTerm.toLowerCase();
    let entries = transcriptState.entries;
    if (search) {
        entries = entries.filter(e => e.text.toLowerCase().includes(search));
    }

    if (entries.length === 0) {
        body.innerHTML = '<div class="transcript-empty">No messages yet</div>';
        return;
    }

    const colors = tc();
    let html = '';
    // Show most recent first, limit to 200
    const visible = entries.slice(-200).reverse();
    for (const e of visible) {
        const time = e.time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
        const typeColor = e.type === 'user' ? colors.amber
            : e.type === 'assistant' ? colors.cyan
            : e.type === 'thinking' ? colors.purple
            : colors.green;
        const typeLabel = e.type.toUpperCase();
        const agentName = sparkCanvas?.agents.get(e.agentId)?.name || e.agentId.substring(0, 8);

        let textHtml = escapeHtml(e.text);
        // Highlight search matches
        if (search) {
            textHtml = textHtml.replace(new RegExp(`(${search.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi'),
                '<mark class="transcript-highlight">$1</mark>');
        }

        html += `<div class="transcript-entry">
            <span class="transcript-time">${time}</span>
            <span class="transcript-type" style="color:${typeColor}">${typeLabel}</span>
            <span class="transcript-agent">${escapeHtml(agentName)}</span>
            <span class="transcript-text">${textHtml}</span>
        </div>`;
    }
    body.innerHTML = html;
}

// ─── Panel Initialization ───────────────────────────

function initPanels(canvas) {
    // Toggle buttons
    document.getElementById('btnToggleTimeline').addEventListener('click', toggleTimelinePanel);
    document.getElementById('btnToggleFiles').addEventListener('click', toggleFilePanel);
    document.getElementById('btnToggleTranscript').addEventListener('click', toggleTranscriptPanel);

    // Timeline controls
    document.getElementById('btnTimelineClose').addEventListener('click', toggleTimelinePanel);
    document.getElementById('btnTimelineZoomIn').addEventListener('click', () => {
        timelineState.pixelsPerSecond = Math.min(60, timelineState.pixelsPerSecond * 1.3);
        renderTimeline();
    });
    document.getElementById('btnTimelineZoomOut').addEventListener('click', () => {
        timelineState.pixelsPerSecond = Math.max(1, timelineState.pixelsPerSecond / 1.3);
        renderTimeline();
    });
    document.getElementById('btnTimelineFit').addEventListener('click', () => {
        timelineState.scrollX = 0;
        // Auto-fit pps to show full session
        if (sparkCanvas?.sessionStart) {
            const dur = (Date.now() - sparkCanvas.sessionStart.getTime()) / 1000;
            const body = document.getElementById('timelineBody');
            if (body && dur > 0) {
                timelineState.pixelsPerSecond = Math.max(1, (body.clientWidth - 80) / dur);
            }
        }
        renderTimeline();
    });

    // File panel close
    document.getElementById('btnFileClose').addEventListener('click', toggleFilePanel);

    // Transcript
    document.getElementById('btnTranscriptClose').addEventListener('click', toggleTranscriptPanel);
    document.getElementById('transcriptSearch').addEventListener('input', (e) => {
        transcriptState.searchTerm = e.target.value;
        renderTranscriptPanel();
    });

    // Visibility filter toggles
    const toggleFilter = (btnId, key) => {
        const btn = document.getElementById(btnId);
        btn.addEventListener('click', () => {
            filters[key] = !filters[key];
            btn.classList.toggle('active', filters[key]);
        });
    };
    toggleFilter('btnToggleCards', 'showCards');
    toggleFilter('btnToggleEdges', 'showEdges');
    toggleFilter('btnToggleBubbles', 'showBubbles');

    // Periodic timeline refresh when visible
    setInterval(() => {
        if (timelineState.visible) renderTimeline();
    }, 1000);
}
