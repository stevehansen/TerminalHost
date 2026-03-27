/**
 * Spark Canvas — Theme system.
 * Each theme provides colors, ambient rendering, and node/edge/card overrides.
 */

// ─── Theme Registry ────────────────────────────────────

const themes = {};
let activeTheme = null;

function registerTheme(id, theme) {
    themes[id] = theme;
}

function setTheme(id, fromHost) {
    if (!themes[id]) return;
    activeTheme = themes[id];
    document.body.className = `theme-${id}`;
    // Update selector if it exists
    const sel = document.getElementById('themeSelect');
    if (sel) sel.value = id;
    // Reinit ambient layer if present
    if (activeTheme.initAmbient && sparkCanvas) {
        activeTheme.initAmbient(sparkCanvas.canvas);
    }
    // Notify host to persist (unless this was initiated by the host)
    if (!fromHost) {
        notifyHost('themeChanged', { theme: id });
    }
}

function getTheme() {
    return activeTheme;
}

function getThemeIds() {
    return Object.keys(themes);
}

// ─── Theme: Holographic (Default) ──────────────────────

registerTheme('holographic', {
    name: 'Holographic',
    colors: {
        void: '#050510',
        cyan: '#66ccff',
        brightCyan: '#aaeeff',
        amber: '#ffbb44',
        green: '#66ffaa',
        red: '#ff5566',
        purple: '#cc88ff',
        gray: '#888899',
        nodeFill: 'rgba(10, 15, 40, 0.5)',
    },
    stateColors: {
        Active: '#66ccff', Idle: '#66ccff', Thinking: '#66ccff',
        ToolCalling: '#ffbb44', WaitingPermission: '#ffbb44',
        Complete: '#66ffaa', Error: '#ff5566', Failed: '#ff5566', TimedOut: '#888899',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#050510';
        ctx.fillRect(0, 0, w, h);
    },
    renderAmbient() { /* no ambient layer */ },
    initAmbient() {},
    // Holographic uses default FX (hexagon ring spawn, radial flash complete, shatter error)
});

// ─── Theme: Matrix Rain ────────────────────────────────

const matrixState = {
    columns: [],
    charSize: 14,
    chars: '',
    initialized: false,
    ambientCanvas: null,
    ambientCtx: null,
};

// Build character set: half-width katakana + digits + Latin
(function buildMatrixChars() {
    let chars = '';
    // Half-width katakana (U+FF66 to U+FF9D)
    for (let i = 0xFF66; i <= 0xFF9D; i++) chars += String.fromCharCode(i);
    // Digits and symbols
    chars += '0123456789@#$%&=+<>{}[]|/\\';
    matrixState.chars = chars;
})();

function matrixInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.width / dpr;

    // Create offscreen canvas for rain (avoids clearing main canvas)
    if (!matrixState.ambientCanvas) {
        matrixState.ambientCanvas = document.createElement('canvas');
        matrixState.ambientCtx = matrixState.ambientCanvas.getContext('2d');
    }

    const ac = matrixState.ambientCanvas;
    ac.width = canvas.width;
    ac.height = canvas.height;

    const cols = Math.ceil(w / matrixState.charSize);
    matrixState.columns = [];
    for (let i = 0; i < cols; i++) {
        matrixState.columns.push({
            y: Math.random() * -100,             // start position (staggered)
            speed: 0.3 + Math.random() * 0.7,    // fall speed multiplier
            chars: [],                             // recent characters for trail
            trailLen: 8 + Math.floor(Math.random() * 16), // trail length
            nextChar: 0,                           // time until next char change
        });
    }
    matrixState.initialized = true;
}

function matrixRenderAmbient(ctx, w, h, dpr, dt) {
    if (!matrixState.initialized) return;

    const cs = matrixState.charSize;
    const cols = matrixState.columns;
    const chars = matrixState.chars;

    // Draw directly on main canvas (behind scene, after clear)
    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.font = `${cs}px monospace`;

    const viewH = h / dpr;
    const viewW = w / dpr;

    for (let i = 0; i < cols.length; i++) {
        const col = cols[i];
        const x = i * cs;

        // Advance position
        col.y += col.speed * dt * 60;
        col.nextChar -= dt;

        // Add new character periodically
        if (col.nextChar <= 0) {
            col.nextChar = 0.05 + Math.random() * 0.08;
            const ch = chars[Math.floor(Math.random() * chars.length)];
            col.chars.unshift(ch);
            if (col.chars.length > col.trailLen) col.chars.pop();
        }

        // Draw trail
        for (let j = 0; j < col.chars.length; j++) {
            const cy = col.y - j * cs;
            if (cy < -cs || cy > viewH + cs) continue;

            if (j === 0) {
                // Head: bright white-green
                ctx.fillStyle = '#ccffcc';
                ctx.globalAlpha = 0.95;
            } else {
                // Trail: fading green
                const fade = 1 - (j / col.trailLen);
                ctx.fillStyle = '#00ff41';
                ctx.globalAlpha = fade * 0.6;
            }
            ctx.fillText(col.chars[j], x, cy);
        }

        // Reset when fully off screen
        if (col.y - col.trailLen * cs > viewH) {
            col.y = Math.random() * -200 - 50;
            col.speed = 0.3 + Math.random() * 0.7;
            col.chars = [];
        }
    }

    ctx.globalAlpha = 1;
    ctx.restore();
}

registerTheme('matrix', {
    name: 'Matrix Rain',
    colors: {
        void: '#000000',
        cyan: '#00ff41',
        brightCyan: '#ccffcc',
        amber: '#00cc33',
        green: '#00ff41',
        red: '#ff2222',
        purple: '#00ff41',
        gray: '#336633',
        nodeFill: 'rgba(0, 10, 0, 0.6)',
    },
    stateColors: {
        Active: '#00ff41', Idle: '#00cc33', Thinking: '#00ff41',
        ToolCalling: '#00ff41', WaitingPermission: '#ffaa00',
        Complete: '#00ff41', Error: '#ff2222', Failed: '#ff2222', TimedOut: '#336633',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: matrixInitAmbient,
    renderAmbient: matrixRenderAmbient,
    nodeGlow(ctx, x, y, radius, stateColor, isMain) {
        const gradient = ctx.createRadialGradient(x, y, radius * 0.3, x, y, radius + 12);
        gradient.addColorStop(0, 'rgba(0, 255, 65, 0.15)');
        gradient.addColorStop(1, 'rgba(0, 255, 65, 0)');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(x, y, radius + 12, 0, Math.PI * 2);
        ctx.fill();
    },
    toolCardBorder(state) {
        if (state === 'Error') return '#ff2222';
        if (state === 'Complete') return '#00ff41';
        return '#00cc33';
    },
    edgeColor: 'rgba(0, 255, 65, 0.25)',
    edgeActiveColor: 'rgba(0, 255, 65, 0.5)',
    labelColor: '#00ff41',
    labelFont: '9px monospace',

    // Matrix FX: binary rain spawn, dissolve complete, glitch error
    fxSpawn(fx, x, y, color, radius) {
        // Green character rain falling downward from spawn point
        const chars = matrixState.chars;
        for (let i = 0; i < 24; i++) {
            const col = (i % 6) - 3;
            const delay = Math.floor(i / 6) * 0.15;
            fx._emit(x + col * 12, y, {
                vx: col * 8 + (Math.random() - 0.5) * 10,
                vy: 80 + Math.random() * 120,
                color: i < 6 ? '#ccffcc' : '#00ff41',
                size: 2,
                life: 0.8 + Math.random() * 0.5 + delay,
                drag: 0.99,
                glow: true,
                trail: true,
            });
        }
        // Bright flash
        fx.effects.push({
            type: 'flash', x, y, color: '#00ff41',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: (radius || 28) * 2.5,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        // Dissolve: characters rise and scatter like derezzed code
        const r = radius || 28;
        for (let i = 0; i < 20; i++) {
            const angle = Math.random() * Math.PI * 2;
            const dist = Math.random() * r;
            fx._emit(x + Math.cos(angle) * dist, y + Math.sin(angle) * dist, {
                vx: (Math.random() - 0.5) * 30,
                vy: -40 - Math.random() * 60,
                color: '#00ff41',
                size: 1.5 + Math.random(),
                life: 0.6 + Math.random() * 0.8,
                drag: 0.97,
                glow: true,
            });
        }
        // Soft green ring
        fx.effects.push({
            type: 'ring', x, y, color: '#00ff41',
            born: performance.now() / 1000, duration: 0.8,
            startRadius: r, maxRadius: r * 3,
        });
    },

    fxError(fx, x, y, color, radius) {
        // Glitch: horizontal scan-line burst + red fragments
        const r = radius || 28;
        for (let i = 0; i < 16; i++) {
            const offY = (Math.random() - 0.5) * r * 2;
            fx._emit(x, y + offY, {
                vx: (Math.random() > 0.5 ? 1 : -1) * (60 + Math.random() * 100),
                vy: (Math.random() - 0.5) * 20,
                color: i < 4 ? '#ff2222' : '#00ff41',
                size: 1 + Math.random() * 2,
                life: 0.4 + Math.random() * 0.5,
                drag: 0.94,
                glow: false,
            });
        }
        fx.effects.push({
            type: 'flash', x, y, color: '#ff2222',
            born: performance.now() / 1000, duration: 0.25,
            maxRadius: r * 2,
        });
    },
});

// ─── Theme: War Room / WOPR ──────────────────────────

const warRoomState = {
    sweepAngle: 0,
    blips: [],       // afterglow blips on the radar
    scanlineOffset: 0,
    initialized: false,
};

function warRoomInitAmbient(canvas) {
    warRoomState.sweepAngle = 0;
    warRoomState.blips = [];
    warRoomState.initialized = true;
}

function warRoomRenderAmbient(ctx, w, h, dpr, dt) {
    if (!warRoomState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;
    const cx = vw / 2;
    const cy = vh / 2;
    const radius = Math.min(vw, vh) * 0.42;

    // Scanlines
    ctx.globalAlpha = 0.03;
    warRoomState.scanlineOffset = (warRoomState.scanlineOffset + dt * 30) % 4;
    for (let y = warRoomState.scanlineOffset; y < vh; y += 4) {
        ctx.fillStyle = '#ffaa00';
        ctx.fillRect(0, y, vw, 1);
    }

    // Radar circle rings
    ctx.globalAlpha = 0.08;
    ctx.strokeStyle = '#ffaa00';
    ctx.lineWidth = 1;
    for (let i = 1; i <= 4; i++) {
        ctx.beginPath();
        ctx.arc(cx, cy, radius * (i / 4), 0, Math.PI * 2);
        ctx.stroke();
    }

    // Cross hairs
    ctx.globalAlpha = 0.05;
    ctx.beginPath();
    ctx.moveTo(cx - radius, cy);
    ctx.lineTo(cx + radius, cy);
    ctx.moveTo(cx, cy - radius);
    ctx.lineTo(cx, cy + radius);
    ctx.stroke();

    // Sweep line
    warRoomState.sweepAngle += dt * (Math.PI * 2 / 10); // 10s per revolution
    if (warRoomState.sweepAngle > Math.PI * 2) warRoomState.sweepAngle -= Math.PI * 2;

    const sweepX = cx + Math.cos(warRoomState.sweepAngle) * radius;
    const sweepY = cy + Math.sin(warRoomState.sweepAngle) * radius;

    // Sweep trail (fading arc)
    const trailAngle = 0.5; // radians of trail
    const grad = ctx.createConicGradient(warRoomState.sweepAngle - trailAngle, cx, cy);
    grad.addColorStop(0, 'rgba(255, 170, 0, 0)');
    grad.addColorStop(trailAngle / (Math.PI * 2), 'rgba(255, 170, 0, 0.12)');
    grad.addColorStop(trailAngle / (Math.PI * 2) + 0.001, 'rgba(255, 170, 0, 0)');
    grad.addColorStop(1, 'rgba(255, 170, 0, 0)');
    ctx.globalAlpha = 1;
    ctx.fillStyle = grad;
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.arc(cx, cy, radius, 0, Math.PI * 2);
    ctx.closePath();
    ctx.fill();

    // Sweep line itself
    ctx.globalAlpha = 0.6;
    ctx.strokeStyle = '#ffaa00';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.lineTo(sweepX, sweepY);
    ctx.stroke();

    // CRT vignette
    ctx.globalAlpha = 1;
    const vigGrad = ctx.createRadialGradient(cx, cy, radius * 0.3, cx, cy, Math.max(vw, vh) * 0.7);
    vigGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
    vigGrad.addColorStop(1, 'rgba(0, 0, 0, 0.5)');
    ctx.fillStyle = vigGrad;
    ctx.fillRect(0, 0, vw, vh);

    ctx.restore();
}

registerTheme('warroom', {
    name: 'War Room',
    colors: {
        void: '#0a0800',
        cyan: '#ffaa00',       // Read: amber (radar contacts)
        brightCyan: '#ffcc44',
        amber: '#ff6600',      // Exec: deep orange (threat)
        green: '#33ff33',      // Write: CRT green
        red: '#ff2222',
        purple: '#33ccff',     // Agent: cold blue (allied signal)
        gray: '#665500',
        nodeFill: 'rgba(20, 15, 0, 0.6)',
    },
    stateColors: {
        Active: '#ffaa00', Idle: '#cc8800', Thinking: '#ffcc44',
        ToolCalling: '#ff6600', WaitingPermission: '#ff6600',
        Complete: '#33ff33', Error: '#ff2222', Failed: '#ff2222', TimedOut: '#665500',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#0a0800';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: warRoomInitAmbient,
    renderAmbient: warRoomRenderAmbient,
    toolCardBorder(state) {
        if (state === 'Error') return '#ff2222';
        if (state === 'Complete') return '#33ff33';
        return '#ffaa00';
    },
    edgeColor: 'rgba(255, 170, 0, 0.2)',
    edgeActiveColor: 'rgba(255, 170, 0, 0.5)',
    labelColor: '#ffaa00',
    labelFont: '9px monospace',

    // War Room FX: radar ping spawn, tactical flash complete, explosion error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Radar ping: expanding concentric rings
        for (let i = 0; i < 3; i++) {
            fx.effects.push({
                type: 'ring', x, y, color: '#ffaa00',
                born: performance.now() / 1000 + i * 0.2, duration: 1.0,
                startRadius: r, maxRadius: r * (3 + i),
            });
        }
        // Contact blip particles
        for (let i = 0; i < 10; i++) {
            const angle = Math.random() * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * (30 + Math.random() * 40),
                vy: Math.sin(angle) * (30 + Math.random() * 40),
                color: '#ffaa00',
                size: 2 + Math.random(),
                life: 0.5 + Math.random() * 0.5,
                drag: 0.95,
                glow: true,
            });
        }
    },

    fxComplete(fx, x, y, color, radius) {
        // Tactical flash — CRT green confirmation burst
        fx.effects.push({
            type: 'flash', x, y, color: '#33ff33',
            born: performance.now() / 1000, duration: 0.4,
            maxRadius: (radius || 28) * 2,
        });
        // Confirmation ring
        fx.effects.push({
            type: 'ring', x, y, color: '#33ff33',
            born: performance.now() / 1000, duration: 0.8,
            startRadius: radius || 28, maxRadius: (radius || 28) * 4,
        });
        // Upward streak particles (signal acquired)
        for (let i = 0; i < 8; i++) {
            fx._emit(x + (Math.random() - 0.5) * 20, y, {
                vx: (Math.random() - 0.5) * 15,
                vy: -60 - Math.random() * 80,
                color: '#33ff33',
                size: 1.5,
                life: 0.6 + Math.random() * 0.4,
                drag: 0.97,
                glow: true,
                trail: true,
            });
        }
    },

    fxError(fx, x, y, color, radius) {
        // Explosion: radial burst with orange/red fire particles
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#ff6600',
            born: performance.now() / 1000, duration: 0.3,
            maxRadius: r * 3,
        });
        for (let i = 0; i < 20; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 50 + Math.random() * 100;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i < 8 ? '#ff2222' : i < 14 ? '#ff6600' : '#ffaa00',
                size: 2 + Math.random() * 2,
                life: 0.5 + Math.random() * 0.6,
                drag: 0.93,
                glow: true,
            });
        }
        // Crack lines
        for (let i = 0; i < 5; i++) {
            fx.effects.push({
                type: 'crack', x, y, color: '#ff2222',
                angle: (i / 5) * Math.PI * 2 + Math.random() * 0.5,
                born: performance.now() / 1000, duration: 1.2,
                length: r * 2 + Math.random() * 30,
                width: 1.5,
            });
        }
    },
});

// ─── Theme: Tron Circuit ─────────────────────────────

const tronState = {
    gridOffset: 0,
    pulses: [],  // light pulses traveling along grid lines
    initialized: false,
};

function tronInitAmbient(canvas) {
    tronState.gridOffset = 0;
    tronState.pulses = [];
    tronState.initialized = true;
}

function tronRenderAmbient(ctx, w, h, dpr, dt) {
    if (!tronState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;
    const gridSize = 60;

    // Grid lines
    ctx.strokeStyle = 'rgba(0, 223, 255, 0.06)';
    ctx.lineWidth = 1;

    for (let x = 0; x < vw; x += gridSize) {
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, vh);
        ctx.stroke();
    }
    for (let y = 0; y < vh; y += gridSize) {
        ctx.beginPath();
        ctx.moveTo(0, y);
        ctx.lineTo(vw, y);
        ctx.stroke();
    }

    // Occasional brighter grid line intersections
    ctx.fillStyle = 'rgba(0, 223, 255, 0.12)';
    for (let x = 0; x < vw; x += gridSize) {
        for (let y = 0; y < vh; y += gridSize) {
            ctx.fillRect(x - 1.5, y - 1.5, 3, 3);
        }
    }

    // Traveling light pulses along grid lines
    if (Math.random() < dt * 2 && tronState.pulses.length < 8) {
        const horizontal = Math.random() > 0.5;
        tronState.pulses.push({
            horizontal,
            pos: horizontal ? 0 : 0,
            lane: Math.floor(Math.random() * (horizontal ? vh / gridSize : vw / gridSize)) * gridSize,
            speed: 150 + Math.random() * 250,
            length: 40 + Math.random() * 80,
        });
    }

    for (let i = tronState.pulses.length - 1; i >= 0; i--) {
        const p = tronState.pulses[i];
        p.pos += p.speed * dt;

        const limit = p.horizontal ? vw : vh;
        if (p.pos - p.length > limit) {
            tronState.pulses.splice(i, 1);
            continue;
        }

        const grad = p.horizontal
            ? ctx.createLinearGradient(p.pos - p.length, 0, p.pos, 0)
            : ctx.createLinearGradient(0, p.pos - p.length, 0, p.pos);
        grad.addColorStop(0, 'rgba(0, 223, 255, 0)');
        grad.addColorStop(1, 'rgba(0, 223, 255, 0.5)');

        ctx.strokeStyle = grad;
        ctx.lineWidth = 2;
        ctx.beginPath();
        if (p.horizontal) {
            ctx.moveTo(Math.max(0, p.pos - p.length), p.lane);
            ctx.lineTo(p.pos, p.lane);
        } else {
            ctx.moveTo(p.lane, Math.max(0, p.pos - p.length));
            ctx.lineTo(p.lane, p.pos);
        }
        ctx.stroke();
    }

    ctx.restore();
}

registerTheme('tron', {
    name: 'Tron Circuit',
    colors: {
        void: '#050515',
        cyan: '#00dfff',       // Read: classic Tron cyan
        brightCyan: '#88eeff',
        amber: '#ff6600',      // Exec: Rinzler orange
        green: '#44ffaa',      // Write: identity disc green
        red: '#ff2222',
        purple: '#cc66ff',     // Agent: program purple
        gray: '#334455',
        nodeFill: 'rgba(5, 5, 25, 0.7)',
    },
    stateColors: {
        Active: '#00dfff', Idle: '#0099bb', Thinking: '#00dfff',
        ToolCalling: '#ff6600', WaitingPermission: '#ff6600',
        Complete: '#00dfff', Error: '#ff2222', Failed: '#ff2222', TimedOut: '#334455',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#050515';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: tronInitAmbient,
    renderAmbient: tronRenderAmbient,
    toolCardBorder(state) {
        if (state === 'Error') return '#ff2222';
        if (state === 'Complete') return '#00dfff';
        return '#ff6600';
    },
    edgeColor: 'rgba(0, 223, 255, 0.2)',
    edgeActiveColor: 'rgba(0, 223, 255, 0.5)',
    labelColor: '#00dfff',
    labelFont: '9px monospace',

    // Tron FX: circuit trace spawn, identity disc complete, derez error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Circuit trace: 4 perpendicular light trails extending outward (grid-aligned)
        const dirs = [[1,0],[-1,0],[0,1],[0,-1]];
        for (const [dx, dy] of dirs) {
            const len = r * 3 + Math.random() * 30;
            fx.effects.push({
                type: 'crack', x, y, color: '#00dfff',
                angle: Math.atan2(dy, dx),
                born: performance.now() / 1000, duration: 0.8,
                length: len, width: 2,
            });
            // Spark at end of trace
            fx._emit(x + dx * len, y + dy * len, {
                vx: dx * 20, vy: dy * 20,
                color: '#88eeff',
                size: 3, life: 0.4, drag: 0.9, glow: true,
            });
        }
        // Central flash
        fx.effects.push({
            type: 'flash', x, y, color: '#00dfff',
            born: performance.now() / 1000, duration: 0.35,
            maxRadius: r * 2,
        });
        // Ring particles
        for (let i = 0; i < 12; i++) {
            const angle = (i / 12) * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * 50,
                vy: Math.sin(angle) * 50,
                color: '#00dfff',
                size: 1.5, life: 0.6, drag: 0.96, glow: true,
            });
        }
    },

    fxComplete(fx, x, y, color, radius) {
        // Identity disc: bright spinning ring that expands then fades
        const r = radius || 28;
        for (let i = 0; i < 2; i++) {
            fx.effects.push({
                type: 'ring', x, y, color: '#00dfff',
                born: performance.now() / 1000 + i * 0.15, duration: 0.9,
                startRadius: r * 0.5, maxRadius: r * (3 + i * 1.5),
            });
        }
        // Disc fragment particles flying outward
        for (let i = 0; i < 8; i++) {
            const angle = (i / 8) * Math.PI * 2;
            fx._emit(x + Math.cos(angle) * r, y + Math.sin(angle) * r, {
                vx: Math.cos(angle) * 60,
                vy: Math.sin(angle) * 60,
                color: '#00dfff',
                size: 2, life: 0.5, drag: 0.95, glow: true, trail: true,
            });
        }
    },

    fxError(fx, x, y, color, radius) {
        // Derez: rectangular fragments scatter with orange glow
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#ff6600',
            born: performance.now() / 1000, duration: 0.25,
            maxRadius: r * 2,
        });
        for (let i = 0; i < 18; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 40 + Math.random() * 80;
            fx._emit(x + (Math.random() - 0.5) * r, y + (Math.random() - 0.5) * r, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i < 6 ? '#ff6600' : '#ff2222',
                size: 1.5 + Math.random() * 2,
                life: 0.4 + Math.random() * 0.5,
                drag: 0.92,
                glow: false,
                angular: (Math.random() - 0.5) * 8,
            });
        }
    },
});

// ─── Theme: LCARS ────────────────────────────────────

function lcarsInitAmbient() {}

function lcarsRenderAmbient(ctx, w, h, dpr, dt) {
    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;

    const colors = ['#ff9900', '#cc6699', '#9999cc', '#6688cc'];
    const barH = 12;
    const gap = 3;

    // Top bezel: horizontal bars with rounded left ends
    for (let i = 0; i < 3; i++) {
        const y = 4 + i * (barH + gap);
        const barW = 60 + (i * 40);
        ctx.fillStyle = colors[i % colors.length];
        ctx.globalAlpha = 0.35;
        ctx.beginPath();
        ctx.moveTo(barH / 2, y);
        ctx.lineTo(barW, y);
        ctx.lineTo(barW, y + barH);
        ctx.lineTo(barH / 2, y + barH);
        ctx.arc(barH / 2, y + barH / 2, barH / 2, Math.PI / 2, -Math.PI / 2, true);
        ctx.closePath();
        ctx.fill();
    }

    // Right side vertical bar
    ctx.fillStyle = '#ff9900';
    ctx.globalAlpha = 0.2;
    ctx.fillRect(vw - 16, 50, 12, vh - 100);

    // Bottom bezel
    for (let i = 0; i < 2; i++) {
        const y = vh - 4 - (i + 1) * (barH + gap);
        const barW = 80 + (i * 50);
        ctx.fillStyle = colors[(i + 2) % colors.length];
        ctx.globalAlpha = 0.3;
        ctx.beginPath();
        ctx.moveTo(vw - barH / 2, y);
        ctx.lineTo(vw - barW, y);
        ctx.lineTo(vw - barW, y + barH);
        ctx.lineTo(vw - barH / 2, y + barH);
        ctx.arc(vw - barH / 2, y + barH / 2, barH / 2, -Math.PI / 2, Math.PI / 2, true);
        ctx.closePath();
        ctx.fill();
    }

    ctx.globalAlpha = 1;
    ctx.restore();
}

registerTheme('lcars', {
    name: 'LCARS',
    colors: {
        void: '#000000',
        cyan: '#6688cc',
        brightCyan: '#9999cc',
        amber: '#ff9900',
        green: '#66cc66',
        red: '#cc3333',
        purple: '#cc6699',
        gray: '#666677',
        nodeFill: 'rgba(0, 0, 10, 0.6)',
    },
    stateColors: {
        Active: '#6688cc', Idle: '#9999cc', Thinking: '#cc6699',
        ToolCalling: '#ff9900', WaitingPermission: '#ff9900',
        Complete: '#66cc66', Error: '#cc3333', Failed: '#cc3333', TimedOut: '#666677',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: lcarsInitAmbient,
    renderAmbient: lcarsRenderAmbient,
    toolCardBorder(state) {
        if (state === 'Error') return '#cc3333';
        if (state === 'Complete') return '#66cc66';
        return '#ff9900';
    },
    edgeColor: 'rgba(153, 153, 204, 0.25)',
    edgeActiveColor: 'rgba(153, 153, 204, 0.5)',
    labelColor: '#ff9900',
    labelFont: '10px sans-serif',

    // LCARS FX: transporter beam spawn, panel sweep complete, alert error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Transporter beam: vertical column of sparkling particles
        for (let i = 0; i < 24; i++) {
            fx._emit(x + (Math.random() - 0.5) * r * 1.2, y + r * 2, {
                vx: (Math.random() - 0.5) * 8,
                vy: -80 - Math.random() * 100,
                color: ['#ff9900', '#cc6699', '#9999cc', '#6688cc'][i % 4],
                size: 1.5 + Math.random(),
                life: 0.6 + Math.random() * 0.4,
                drag: 0.98,
                glow: true,
            });
        }
        // Horizontal LCARS-style sweep line
        fx.effects.push({
            type: 'crack', x: x - r * 3, y, color: '#ff9900',
            angle: 0, born: performance.now() / 1000, duration: 0.6,
            length: r * 6, width: 3,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        // Panel sweep: wide horizontal bar flash
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#66cc66',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: r * 2.5,
        });
        // LCARS color particles radiating outward
        const lcColors = ['#ff9900', '#cc6699', '#9999cc', '#66cc66'];
        for (let i = 0; i < 10; i++) {
            const angle = (i / 10) * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * 40,
                vy: Math.sin(angle) * 40,
                color: lcColors[i % 4],
                size: 2, life: 0.5, drag: 0.96, glow: true,
            });
        }
    },

    fxError(fx, x, y, color, radius) {
        // Red alert: pulsing red flash + horizontal alert bars
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#cc3333',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: r * 3,
        });
        // Alert bar cracks (horizontal)
        for (let i = -1; i <= 1; i += 2) {
            fx.effects.push({
                type: 'crack', x, y: y + i * 10, color: '#cc3333',
                angle: i > 0 ? 0 : Math.PI,
                born: performance.now() / 1000, duration: 1.0,
                length: r * 4, width: 2.5,
            });
        }
        for (let i = 0; i < 8; i++) {
            fx._emit(x, y, {
                vx: (Math.random() - 0.5) * 60,
                vy: (Math.random() - 0.5) * 60,
                color: '#cc3333',
                size: 2 + Math.random(), life: 0.4, drag: 0.94, glow: true,
            });
        }
    },
});

// ─── Theme: Blade Runner Noir ────────────────────────

const bladeRunnerState = {
    raindrops: [],
    grainSeed: 0,
    initialized: false,
};

function bladeRunnerInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const vw = canvas.width / dpr;
    const vh = canvas.height / dpr;

    bladeRunnerState.raindrops = [];
    for (let i = 0; i < 120; i++) {
        bladeRunnerState.raindrops.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            speed: 200 + Math.random() * 300,
            length: 8 + Math.random() * 16,
            opacity: 0.1 + Math.random() * 0.2,
        });
    }
    bladeRunnerState.initialized = true;
}

function bladeRunnerRenderAmbient(ctx, w, h, dpr, dt) {
    if (!bladeRunnerState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;

    // Rain
    ctx.strokeStyle = '#44aacc';
    ctx.lineWidth = 1;
    for (const drop of bladeRunnerState.raindrops) {
        drop.y += drop.speed * dt;
        drop.x -= drop.speed * dt * 0.1; // slight wind

        if (drop.y > vh) {
            drop.y = -drop.length;
            drop.x = Math.random() * vw;
        }
        if (drop.x < -20) drop.x = vw + 10;

        ctx.globalAlpha = drop.opacity;
        ctx.beginPath();
        ctx.moveTo(drop.x, drop.y);
        ctx.lineTo(drop.x - drop.length * 0.1, drop.y + drop.length);
        ctx.stroke();
    }

    // Film grain overlay (sparse, performance-friendly)
    ctx.globalAlpha = 0.04;
    bladeRunnerState.grainSeed += dt;
    const grainStep = 8;
    for (let gx = 0; gx < vw; gx += grainStep) {
        for (let gy = 0; gy < vh; gy += grainStep) {
            if (Math.random() > 0.5) {
                ctx.fillStyle = Math.random() > 0.5 ? '#ffffff' : '#000000';
                ctx.fillRect(gx, gy, grainStep, grainStep);
            }
        }
    }

    // Heavy vignette
    ctx.globalAlpha = 1;
    const vigGrad = ctx.createRadialGradient(vw / 2, vh / 2, Math.min(vw, vh) * 0.25, vw / 2, vh / 2, Math.max(vw, vh) * 0.65);
    vigGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
    vigGrad.addColorStop(1, 'rgba(0, 0, 0, 0.6)');
    ctx.fillStyle = vigGrad;
    ctx.fillRect(0, 0, vw, vh);

    ctx.restore();
}

registerTheme('bladerunner', {
    name: 'Blade Runner',
    colors: {
        void: '#0a0a0e',
        cyan: '#44aacc',
        brightCyan: '#77ccdd',
        amber: '#ff8844',
        green: '#44aa66',
        red: '#cc3333',
        purple: '#886699',
        gray: '#555566',
        nodeFill: 'rgba(10, 10, 14, 0.6)',
    },
    stateColors: {
        Active: '#ff8844', Idle: '#44aacc', Thinking: '#886699',
        ToolCalling: '#ff8844', WaitingPermission: '#ff8844',
        Complete: '#44aa66', Error: '#cc3333', Failed: '#cc3333', TimedOut: '#555566',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#0a0a0e';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: bladeRunnerInitAmbient,
    renderAmbient: bladeRunnerRenderAmbient,
    toolCardBorder(state) {
        if (state === 'Error') return '#cc3333';
        if (state === 'Complete') return '#44aa66';
        return '#ff8844';
    },
    edgeColor: 'rgba(68, 170, 204, 0.2)',
    edgeActiveColor: 'rgba(255, 136, 68, 0.4)',
    labelColor: '#ff8844',
    labelFont: '9px monospace',

    // Blade Runner FX: rain splatter spawn, neon fade complete, neon flicker error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Rain splatter: particles falling from above with neon glow
        for (let i = 0; i < 16; i++) {
            fx._emit(x + (Math.random() - 0.5) * r * 3, y - r * 2 - Math.random() * 40, {
                vx: -10 + (Math.random() - 0.5) * 20,
                vy: 60 + Math.random() * 100,
                color: i < 5 ? '#ff8844' : '#44aacc',
                size: 1 + Math.random() * 1.5,
                life: 0.5 + Math.random() * 0.4,
                drag: 0.97,
                glow: true,
                trail: true,
            });
        }
        // Warm neon flash
        fx.effects.push({
            type: 'flash', x, y, color: '#ff8844',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: r * 2,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        // Neon sign turning off: shrinking glow then particles drift up like embers
        const r = radius || 28;
        fx.effects.push({
            type: 'ring', x, y, color: '#44aa66',
            born: performance.now() / 1000, duration: 1.0,
            startRadius: r, maxRadius: r * 3,
        });
        for (let i = 0; i < 10; i++) {
            fx._emit(x + (Math.random() - 0.5) * r, y + (Math.random() - 0.5) * r, {
                vx: (Math.random() - 0.5) * 15,
                vy: -20 - Math.random() * 40,
                color: i < 3 ? '#ff8844' : '#44aa66',
                size: 1 + Math.random(),
                life: 1.0 + Math.random() * 0.5,
                drag: 0.99,
                glow: true,
            });
        }
    },

    fxError(fx, x, y, color, radius) {
        // Neon flicker: rapid flashes then shatter
        const r = radius || 28;
        for (let i = 0; i < 3; i++) {
            fx.effects.push({
                type: 'flash', x, y, color: '#cc3333',
                born: performance.now() / 1000 + i * 0.08, duration: 0.15,
                maxRadius: r * (1.5 + i * 0.5),
            });
        }
        // Glass shatter fragments
        for (let i = 0; i < 14; i++) {
            const angle = Math.random() * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * (30 + Math.random() * 60),
                vy: Math.sin(angle) * (30 + Math.random() * 60) + 20,
                color: i < 4 ? '#cc3333' : '#886699',
                size: 1.5 + Math.random() * 2,
                life: 0.5 + Math.random() * 0.4,
                drag: 0.93,
                glow: false,
                angular: (Math.random() - 0.5) * 6,
            });
        }
    },
});

// ─── Theme: Swordfish Terminal ───────────────────────

const swordfishState = {
    cubes: [],
    hexScroll: 0,
    initialized: false,
};

function swordfishInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const vw = canvas.width / dpr;
    const vh = canvas.height / dpr;

    swordfishState.cubes = [];
    for (let i = 0; i < 4; i++) {
        swordfishState.cubes.push({
            x: vw * (0.2 + Math.random() * 0.6),
            y: vh * (0.2 + Math.random() * 0.6),
            size: 40 + Math.random() * 60,
            rotSpeed: 0.2 + Math.random() * 0.4,
            angle: Math.random() * Math.PI * 2,
        });
    }
    swordfishState.hexScroll = 0;
    swordfishState.initialized = true;
}

function swordfishRenderAmbient(ctx, w, h, dpr, dt) {
    if (!swordfishState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;

    // Wireframe cubes
    for (const cube of swordfishState.cubes) {
        cube.angle += cube.rotSpeed * dt;
        const s = cube.size;
        const a = cube.angle;
        const cos = Math.cos(a);
        const sin = Math.sin(a);

        // Simple isometric cube projection
        const front = [
            [-1,-1], [1,-1], [1,1], [-1,1]
        ].map(([px, py]) => [
            cube.x + (px * cos - py * sin * 0.5) * s * 0.5,
            cube.y + (px * sin + py * cos * 0.5) * s * 0.5
        ]);
        const depth = s * 0.3;
        const back = front.map(([x, y]) => [x + depth * cos, y - depth * 0.7]);

        ctx.strokeStyle = 'rgba(0, 255, 255, 0.06)';
        ctx.lineWidth = 1;

        // Front face
        ctx.beginPath();
        front.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
        ctx.closePath();
        ctx.stroke();

        // Back face
        ctx.beginPath();
        back.forEach(([x, y], i) => i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y));
        ctx.closePath();
        ctx.stroke();

        // Connecting lines
        for (let i = 0; i < 4; i++) {
            ctx.beginPath();
            ctx.moveTo(front[i][0], front[i][1]);
            ctx.lineTo(back[i][0], back[i][1]);
            ctx.stroke();
        }
    }

    // Hex stream columns (sparse, left/right edges)
    swordfishState.hexScroll += dt * 40;
    ctx.font = '10px monospace';
    ctx.globalAlpha = 0.08;
    const hexChars = '0123456789ABCDEF';
    for (let col = 0; col < 3; col++) {
        const x = 8 + col * 18;
        for (let row = 0; row < vh / 12; row++) {
            const ch = hexChars[Math.floor(Math.random() * 16)];
            ctx.fillStyle = col === 0 ? '#00ffff' : col === 1 ? '#ff00ff' : '#0066ff';
            ctx.fillText(ch, x, (row * 12 + swordfishState.hexScroll) % vh);
        }
        // Right side too
        for (let row = 0; row < vh / 12; row++) {
            const ch = hexChars[Math.floor(Math.random() * 16)];
            ctx.fillStyle = col === 0 ? '#ff00ff' : col === 1 ? '#00ffff' : '#0066ff';
            ctx.fillText(ch, vw - 60 + col * 18, (row * 12 + swordfishState.hexScroll * 1.3) % vh);
        }
    }

    ctx.globalAlpha = 1;
    ctx.restore();
}

registerTheme('swordfish', {
    name: 'Swordfish',
    colors: {
        void: '#000000',
        cyan: '#00ffff',       // Read: electric cyan
        brightCyan: '#aaffff',
        amber: '#ff00ff',      // Exec: magenta
        green: '#00ff66',      // Write: neon green
        red: '#ff2222',
        purple: '#6644ff',     // Agent: electric blue-violet
        gray: '#334455',
        nodeFill: 'rgba(0, 0, 10, 0.6)',
    },
    stateColors: {
        Active: '#00ffff', Idle: '#0088aa', Thinking: '#ff00ff',
        ToolCalling: '#ff00ff', WaitingPermission: '#ff00ff',
        Complete: '#00ffff', Error: '#ff2222', Failed: '#ff2222', TimedOut: '#334455',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#000005';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: swordfishInitAmbient,
    renderAmbient: swordfishRenderAmbient,
    toolCardBorder(state) {
        if (state === 'Error') return '#ff2222';
        if (state === 'Complete') return '#00ffff';
        return '#ff00ff';
    },
    edgeColor: 'rgba(0, 255, 255, 0.2)',
    edgeActiveColor: 'rgba(255, 0, 255, 0.4)',
    labelColor: '#00ffff',
    labelFont: '9px monospace',

    // Swordfish FX: wireframe expand spawn, hex dissolve complete, digital shatter error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Expanding wireframe cube outline
        fx.effects.push({
            type: 'hexRing', x, y, color: '#00ffff', radius: r,
            born: performance.now() / 1000, duration: 1.0,
            maxRadius: r * 4,
        });
        // Magenta + cyan dual flash
        fx.effects.push({
            type: 'flash', x, y, color: '#ff00ff',
            born: performance.now() / 1000, duration: 0.3,
            maxRadius: r * 2,
        });
        // Hex code particles flying out
        for (let i = 0; i < 14; i++) {
            const angle = (i / 14) * Math.PI * 2;
            const speed = 50 + Math.random() * 60;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i % 2 === 0 ? '#00ffff' : '#ff00ff',
                size: 1.5 + Math.random(),
                life: 0.6 + Math.random() * 0.4,
                drag: 0.96,
                glow: true,
                trail: true,
            });
        }
    },

    fxComplete(fx, x, y, color, radius) {
        // Hex dissolve: particles drift outward in a grid pattern
        const r = radius || 28;
        for (let gx = -2; gx <= 2; gx++) {
            for (let gy = -2; gy <= 2; gy++) {
                if (gx === 0 && gy === 0) continue;
                fx._emit(x + gx * 10, y + gy * 10, {
                    vx: gx * 25,
                    vy: gy * 25,
                    color: '#00ffff',
                    size: 1.5, life: 0.6, drag: 0.97, glow: true,
                });
            }
        }
        fx.effects.push({
            type: 'ring', x, y, color: '#00ffff',
            born: performance.now() / 1000, duration: 0.7,
            startRadius: r, maxRadius: r * 3,
        });
    },

    fxError(fx, x, y, color, radius) {
        // Digital shatter: magenta fragments with glitch lines
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#ff2222',
            born: performance.now() / 1000, duration: 0.2,
            maxRadius: r * 2.5,
        });
        // Horizontal glitch lines
        for (let i = 0; i < 4; i++) {
            const offY = (Math.random() - 0.5) * r * 2;
            fx.effects.push({
                type: 'crack', x: x - r * 2, y: y + offY, color: '#ff00ff',
                angle: 0 + (Math.random() - 0.5) * 0.1,
                born: performance.now() / 1000, duration: 0.8,
                length: r * 4, width: 1.5 + Math.random(),
            });
        }
        for (let i = 0; i < 12; i++) {
            const angle = Math.random() * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * (40 + Math.random() * 80),
                vy: Math.sin(angle) * (40 + Math.random() * 80),
                color: i < 4 ? '#ff2222' : '#ff00ff',
                size: 1.5 + Math.random() * 2,
                life: 0.4 + Math.random() * 0.4,
                drag: 0.92,
                glow: false,
            });
        }
    },
});

// ─── Theme: Minority Report ──────────────────────────

function minorityReportInitAmbient() {}

function minorityReportRenderAmbient(ctx, w, h, dpr, dt) {
    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;

    // Subtle dot grid
    ctx.fillStyle = 'rgba(68, 136, 187, 0.06)';
    const dotSpacing = 40;
    for (let x = dotSpacing; x < vw; x += dotSpacing) {
        for (let y = dotSpacing; y < vh; y += dotSpacing) {
            ctx.beginPath();
            ctx.arc(x, y, 1, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    // Depth layers — subtle horizontal frost bands
    ctx.globalAlpha = 0.02;
    ctx.fillStyle = '#4488bb';
    ctx.fillRect(0, vh * 0.15, vw, 1);
    ctx.fillRect(0, vh * 0.35, vw, 1);
    ctx.fillRect(0, vh * 0.55, vw, 1);
    ctx.fillRect(0, vh * 0.75, vw, 1);

    // Soft center glow
    ctx.globalAlpha = 1;
    const glowGrad = ctx.createRadialGradient(vw / 2, vh / 2, 0, vw / 2, vh / 2, Math.max(vw, vh) * 0.5);
    glowGrad.addColorStop(0, 'rgba(224, 238, 255, 0.03)');
    glowGrad.addColorStop(1, 'rgba(224, 238, 255, 0)');
    ctx.fillStyle = glowGrad;
    ctx.fillRect(0, 0, vw, vh);

    ctx.restore();
}

registerTheme('minority', {
    name: 'Minority Report',
    colors: {
        void: '#0e1520',
        cyan: '#4488bb',       // Read: clinical blue
        brightCyan: '#88bbdd',
        amber: '#bb8844',      // Exec: warm amber (precog alert)
        green: '#44aa88',      // Write: teal-green
        red: '#cc4444',
        purple: '#7766bb',     // Agent: precog purple
        gray: '#556677',
        nodeFill: 'rgba(14, 21, 32, 0.5)',
    },
    stateColors: {
        Active: '#4488bb', Idle: '#6677aa', Thinking: '#88bbdd',
        ToolCalling: '#4488bb', WaitingPermission: '#4488bb',
        Complete: '#44aa88', Error: '#cc4444', Failed: '#cc4444', TimedOut: '#556677',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#0e1520';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: minorityReportInitAmbient,
    renderAmbient: minorityReportRenderAmbient,
    toolCardBorder(state) {
        if (state === 'Error') return '#cc4444';
        if (state === 'Complete') return '#44aa88';
        return '#4488bb';
    },
    edgeColor: 'rgba(68, 136, 187, 0.15)',
    edgeActiveColor: 'rgba(68, 136, 187, 0.35)',
    labelColor: '#88bbdd',
    labelFont: '9px sans-serif',

    // Minority Report FX: glass ripple spawn, precog flash complete, red ball error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Glass ripple: multiple concentric transparent rings
        for (let i = 0; i < 4; i++) {
            fx.effects.push({
                type: 'ring', x, y, color: '#88bbdd',
                born: performance.now() / 1000 + i * 0.12, duration: 1.2,
                startRadius: r * 0.5, maxRadius: r * (2 + i * 1.2),
            });
        }
        // Frosted glass particles drifting outward slowly
        for (let i = 0; i < 12; i++) {
            const angle = (i / 12) * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * 25,
                vy: Math.sin(angle) * 25,
                color: '#88bbdd',
                size: 2 + Math.random(),
                life: 1.0 + Math.random() * 0.5,
                drag: 0.98,
                glow: true,
            });
        }
    },

    fxComplete(fx, x, y, color, radius) {
        // Precog flash: bright white center, blue outward ring
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#ffffff',
            born: performance.now() / 1000, duration: 0.3,
            maxRadius: r * 1.5,
        });
        fx.effects.push({
            type: 'ring', x, y, color: '#44aa88',
            born: performance.now() / 1000 + 0.1, duration: 0.8,
            startRadius: r, maxRadius: r * 4,
        });
        // Gentle upward drift particles
        for (let i = 0; i < 8; i++) {
            fx._emit(x + (Math.random() - 0.5) * r, y, {
                vx: (Math.random() - 0.5) * 10,
                vy: -15 - Math.random() * 30,
                color: '#44aa88',
                size: 1.5, life: 0.8, drag: 0.99, glow: true,
            });
        }
    },

    fxError(fx, x, y, color, radius) {
        // Red ball: warm red sphere flash + crack lines + scattered fragments
        const r = radius || 28;
        fx.effects.push({
            type: 'flash', x, y, color: '#cc4444',
            born: performance.now() / 1000, duration: 0.4,
            maxRadius: r * 2.5,
        });
        // Glass shatter cracks
        for (let i = 0; i < 6; i++) {
            fx.effects.push({
                type: 'crack', x, y, color: '#cc4444',
                angle: (i / 6) * Math.PI * 2 + Math.random() * 0.3,
                born: performance.now() / 1000, duration: 1.2,
                length: r * 2 + Math.random() * 20,
                width: 1 + Math.random(),
            });
        }
        for (let i = 0; i < 10; i++) {
            const angle = Math.random() * Math.PI * 2;
            fx._emit(x, y, {
                vx: Math.cos(angle) * (30 + Math.random() * 50),
                vy: Math.sin(angle) * (30 + Math.random() * 50),
                color: i < 3 ? '#cc4444' : '#7766bb',
                size: 1.5 + Math.random(),
                life: 0.5 + Math.random() * 0.3,
                drag: 0.95,
                glow: true,
            });
        }
    },
});

// ─── Theme: WarGames (WOPR / NORAD 1983) ─────────────

const wargamesState = {
    initialized: false,
    // World map vector points (simplified continental outlines)
    mapLines: [],
    // Missile arcs in flight
    arcs: [],
    arcTimer: 0,
    // CRT flicker
    flickerAlpha: 0,
    scanlineY: 0,
    // Phosphor afterglow grid
    glowGrid: null,
    gridCols: 0,
    gridRows: 0,
};

function wargamesInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const vw = canvas.width / dpr;
    const vh = canvas.height / dpr;

    // Build simplified world map outlines (longitude/latitude → screen coords)
    // Mercator-ish projection centered on North America
    wargamesState.mapLines = buildWargamesMap(vw, vh);

    // Phosphor glow grid (tracks recent draw intensity for afterglow)
    const cellSize = 8;
    wargamesState.gridCols = Math.ceil(vw / cellSize);
    wargamesState.gridRows = Math.ceil(vh / cellSize);
    wargamesState.glowGrid = new Float32Array(wargamesState.gridCols * wargamesState.gridRows);

    wargamesState.arcs = [];
    wargamesState.arcTimer = 0;
    wargamesState.scanlineY = 0;
    wargamesState.initialized = true;
}

function buildWargamesMap(vw, vh) {
    // Simplified continental outlines as polylines
    // Coordinates are fractions [0..1] of the viewport
    const continents = [
        // North America (simplified)
        [[0.08, 0.15], [0.12, 0.12], [0.18, 0.10], [0.25, 0.12], [0.30, 0.18],
         [0.28, 0.25], [0.25, 0.32], [0.22, 0.38], [0.18, 0.42], [0.15, 0.40],
         [0.12, 0.35], [0.10, 0.28], [0.08, 0.22], [0.08, 0.15]],
        // South America
        [[0.22, 0.48], [0.25, 0.45], [0.28, 0.48], [0.30, 0.55], [0.28, 0.65],
         [0.26, 0.72], [0.23, 0.78], [0.20, 0.75], [0.19, 0.65], [0.20, 0.55],
         [0.22, 0.48]],
        // Europe
        [[0.42, 0.12], [0.45, 0.10], [0.50, 0.12], [0.52, 0.15], [0.50, 0.20],
         [0.48, 0.25], [0.45, 0.22], [0.42, 0.18], [0.42, 0.12]],
        // Africa
        [[0.42, 0.30], [0.45, 0.28], [0.50, 0.30], [0.52, 0.38], [0.50, 0.50],
         [0.48, 0.60], [0.45, 0.65], [0.42, 0.58], [0.40, 0.48], [0.40, 0.38],
         [0.42, 0.30]],
        // Asia (simplified)
        [[0.55, 0.10], [0.62, 0.08], [0.70, 0.10], [0.78, 0.15], [0.82, 0.20],
         [0.85, 0.28], [0.80, 0.32], [0.75, 0.35], [0.70, 0.30], [0.65, 0.25],
         [0.58, 0.22], [0.55, 0.18], [0.55, 0.10]],
        // Australia
        [[0.78, 0.58], [0.82, 0.55], [0.88, 0.56], [0.90, 0.62], [0.88, 0.68],
         [0.82, 0.70], [0.78, 0.65], [0.78, 0.58]],
    ];

    return continents.map(poly =>
        poly.map(([fx, fy]) => [fx * vw, fy * vh])
    );
}

function wargamesLaunchArc(vw, vh) {
    // Random source and target points on the map
    const cities = [
        [0.15, 0.25], [0.25, 0.20], [0.20, 0.35], // NA
        [0.45, 0.15], [0.50, 0.18], [0.48, 0.22],  // Europe
        [0.65, 0.20], [0.75, 0.25], [0.80, 0.18],  // Asia
        [0.45, 0.45], [0.85, 0.60],                  // Africa, Aus
    ];
    const src = cities[Math.floor(Math.random() * cities.length)];
    let tgt = cities[Math.floor(Math.random() * cities.length)];
    while (tgt === src) tgt = cities[Math.floor(Math.random() * cities.length)];

    return {
        x0: src[0] * vw, y0: src[1] * vh,
        x1: tgt[0] * vw, y1: tgt[1] * vh,
        progress: 0,
        speed: 0.15 + Math.random() * 0.2,
        peak: 0.25 + Math.random() * 0.15, // arc height as fraction of distance
    };
}

function wargamesRenderAmbient(ctx, w, h, dpr, dt) {
    if (!wargamesState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;

    // ── CRT scanline sweep ──
    wargamesState.scanlineY = (wargamesState.scanlineY + dt * 120) % vh;
    ctx.globalAlpha = 0.04;
    ctx.fillStyle = '#33ff33';
    ctx.fillRect(0, wargamesState.scanlineY, vw, 2);

    // ── Horizontal CRT scanlines ──
    ctx.globalAlpha = 0.025;
    ctx.fillStyle = '#33ff33';
    for (let y = 0; y < vh; y += 3) {
        ctx.fillRect(0, y, vw, 1);
    }

    // ── World map vector outlines ──
    ctx.globalAlpha = 0.07;
    ctx.strokeStyle = '#33ff33';
    ctx.lineWidth = 1;
    for (const poly of wargamesState.mapLines) {
        ctx.beginPath();
        ctx.moveTo(poly[0][0], poly[0][1]);
        for (let i = 1; i < poly.length; i++) {
            ctx.lineTo(poly[i][0], poly[i][1]);
        }
        ctx.stroke();
    }

    // ── Missile trajectory arcs ──
    wargamesState.arcTimer += dt;
    if (wargamesState.arcTimer > 2.5 + Math.random() * 3) {
        wargamesState.arcTimer = 0;
        if (wargamesState.arcs.length < 5) {
            wargamesState.arcs.push(wargamesLaunchArc(vw, vh));
        }
    }

    // Update and draw arcs
    for (let i = wargamesState.arcs.length - 1; i >= 0; i--) {
        const arc = wargamesState.arcs[i];
        arc.progress += arc.speed * dt;
        if (arc.progress >= 1) {
            // Impact flash
            ctx.globalAlpha = 0.15;
            ctx.fillStyle = '#33ff33';
            ctx.beginPath();
            ctx.arc(arc.x1, arc.y1, 6, 0, Math.PI * 2);
            ctx.fill();
            wargamesState.arcs.splice(i, 1);
            continue;
        }

        // Draw parabolic arc (traced portion)
        const dx = arc.x1 - arc.x0;
        const dy = arc.y1 - arc.y0;
        const dist = Math.sqrt(dx * dx + dy * dy);
        const peakH = dist * arc.peak;

        ctx.globalAlpha = 0.3;
        ctx.strokeStyle = '#33ff33';
        ctx.lineWidth = 1;
        ctx.beginPath();
        const steps = Math.floor(arc.progress * 40);
        for (let s = 0; s <= steps; s++) {
            const t = s / 40;
            const px = arc.x0 + dx * t;
            const py = arc.y0 + dy * t - Math.sin(t * Math.PI) * peakH;
            if (s === 0) ctx.moveTo(px, py);
            else ctx.lineTo(px, py);
        }
        ctx.stroke();

        // Warhead dot at tip
        const t = arc.progress;
        const tipX = arc.x0 + dx * t;
        const tipY = arc.y0 + dy * t - Math.sin(t * Math.PI) * peakH;
        ctx.globalAlpha = 0.8;
        ctx.fillStyle = '#33ff33';
        ctx.beginPath();
        ctx.arc(tipX, tipY, 2, 0, Math.PI * 2);
        ctx.fill();

        // Source blip
        ctx.globalAlpha = 0.2;
        ctx.beginPath();
        ctx.arc(arc.x0, arc.y0, 3, 0, Math.PI * 2);
        ctx.fill();

        // Target crosshair
        ctx.globalAlpha = 0.15;
        ctx.strokeStyle = '#33ff33';
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        ctx.moveTo(arc.x1 - 6, arc.y1); ctx.lineTo(arc.x1 + 6, arc.y1);
        ctx.moveTo(arc.x1, arc.y1 - 6); ctx.lineTo(arc.x1, arc.y1 + 6);
        ctx.stroke();
    }

    // ── CRT phosphor vignette ──
    ctx.globalAlpha = 1;
    const vigGrad = ctx.createRadialGradient(vw / 2, vh / 2, Math.min(vw, vh) * 0.25,
        vw / 2, vh / 2, Math.max(vw, vh) * 0.7);
    vigGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
    vigGrad.addColorStop(1, 'rgba(0, 0, 0, 0.6)');
    ctx.fillStyle = vigGrad;
    ctx.fillRect(0, 0, vw, vh);

    // ── Random CRT flicker ──
    if (Math.random() < 0.003) {
        wargamesState.flickerAlpha = 0.06;
    }
    if (wargamesState.flickerAlpha > 0) {
        ctx.globalAlpha = wargamesState.flickerAlpha;
        ctx.fillStyle = '#33ff33';
        ctx.fillRect(0, 0, vw, vh);
        wargamesState.flickerAlpha -= dt * 0.3;
    }

    ctx.restore();
}

registerTheme('wargames', {
    name: 'WarGames',
    colors: {
        void: '#000800',           // Deep dark green-black CRT
        cyan: '#33ff33',           // Phosphor green (primary)
        brightCyan: '#88ff88',     // Bright phosphor
        amber: '#33ff33',          // Keep monochrome — everything green
        green: '#33ff33',          // CRT green
        red: '#ff3333',            // DEFCON red alert
        purple: '#33ccff',         // Rare blue accent (allied)
        gray: '#1a4a1a',           // Dim phosphor
        nodeFill: 'rgba(0, 8, 0, 0.65)',
    },
    stateColors: {
        Active: '#33ff33', Idle: '#22aa22', Thinking: '#44ff44',
        ToolCalling: '#33ff33', WaitingPermission: '#ffcc00',
        Complete: '#33ff33', Error: '#ff3333', Failed: '#ff3333', TimedOut: '#1a4a1a',
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#000800';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: wargamesInitAmbient,
    renderAmbient: wargamesRenderAmbient,
    nodeGlow(ctx, x, y, radius, stateColor, isMain) {
        // Green phosphor bloom — characteristic CRT glow
        const gradient = ctx.createRadialGradient(x, y, radius * 0.2, x, y, radius + 16);
        gradient.addColorStop(0, 'rgba(51, 255, 51, 0.18)');
        gradient.addColorStop(0.5, 'rgba(51, 255, 51, 0.06)');
        gradient.addColorStop(1, 'rgba(51, 255, 51, 0)');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(x, y, radius + 16, 0, Math.PI * 2);
        ctx.fill();
    },
    toolCardBorder(state) {
        if (state === 'Error') return '#ff3333';
        if (state === 'Complete') return '#33ff33';
        return '#22aa22';
    },
    edgeColor: 'rgba(51, 255, 51, 0.15)',
    edgeActiveColor: 'rgba(51, 255, 51, 0.45)',
    labelColor: '#33ff33',
    labelFont: '9px monospace',

    // WarGames FX: missile launch spawn, DEFCON confirmation complete, nuclear error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Missile launch: trajectory lines radiating outward
        for (let i = 0; i < 6; i++) {
            const angle = (i / 6) * Math.PI * 2 + Math.random() * 0.3;
            const speed = 40 + Math.random() * 60;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: '#33ff33',
                size: 1.5,
                life: 0.8 + Math.random() * 0.4,
                drag: 0.96,
                glow: true,
                trail: true,
            });
        }
        // Expanding radar-style detection ring
        fx.effects.push({
            type: 'ring', x, y, color: '#33ff33',
            born: performance.now() / 1000, duration: 1.2,
            startRadius: r * 0.5, maxRadius: r * 4,
        });
        // Second delayed ring (WOPR double-confirm)
        fx.effects.push({
            type: 'ring', x, y, color: '#22aa22',
            born: performance.now() / 1000 + 0.3, duration: 1.0,
            startRadius: r * 0.5, maxRadius: r * 3,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        const r = radius || 28;
        // DEFCON stand-down: green flash + settling particles
        fx.effects.push({
            type: 'flash', x, y, color: '#33ff33',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: r * 2,
        });
        // Confirmation vector lines (like trajectory confirm)
        for (let i = 0; i < 4; i++) {
            const angle = (i / 4) * Math.PI * 2;
            fx.effects.push({
                type: 'crack', x, y, color: '#33ff33',
                angle: angle,
                born: performance.now() / 1000, duration: 0.8,
                length: r * 2.5,
                width: 1,
            });
        }
        // Upward phosphor particles (signal dissipation)
        for (let i = 0; i < 10; i++) {
            fx._emit(x + (Math.random() - 0.5) * r, y + (Math.random() - 0.5) * r, {
                vx: (Math.random() - 0.5) * 20,
                vy: -30 - Math.random() * 50,
                color: '#33ff33',
                size: 1 + Math.random(),
                life: 0.6 + Math.random() * 0.5,
                drag: 0.98,
                glow: true,
            });
        }
    },

    fxError(fx, x, y, color, radius) {
        // NUCLEAR DETONATION: bright red flash + expanding blast ring + fallout particles
        const r = radius || 28;
        // Bright white-red flash (initial detonation)
        fx.effects.push({
            type: 'flash', x, y, color: '#ff6644',
            born: performance.now() / 1000, duration: 0.3,
            maxRadius: r * 3.5,
        });
        // Blast ring
        fx.effects.push({
            type: 'ring', x, y, color: '#ff3333',
            born: performance.now() / 1000, duration: 1.5,
            startRadius: r, maxRadius: r * 6,
        });
        // Secondary ring (shockwave)
        fx.effects.push({
            type: 'ring', x, y, color: '#ff6644',
            born: performance.now() / 1000 + 0.15, duration: 1.2,
            startRadius: r, maxRadius: r * 4,
        });
        // Fallout particles — mixed red/orange/green
        for (let i = 0; i < 24; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 40 + Math.random() * 80;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i < 10 ? '#ff3333' : i < 18 ? '#ff6644' : '#33ff33',
                size: 1.5 + Math.random() * 2,
                life: 0.5 + Math.random() * 0.8,
                drag: 0.92,
                glow: true,
            });
        }
        // Crack lines radiating (ground zero)
        for (let i = 0; i < 8; i++) {
            fx.effects.push({
                type: 'crack', x, y, color: '#ff3333',
                angle: (i / 8) * Math.PI * 2 + Math.random() * 0.2,
                born: performance.now() / 1000, duration: 1.5,
                length: r * 2.5 + Math.random() * 15,
                width: 1 + Math.random(),
            });
        }
    },
});

// ─── Theme: StarCraft (Terran Command) ────────────────

const starcraftState = {
    initialized: false,
    stars: [],
    gridPhase: 0,
    scanAngle: 0,
    minerals: [],      // Ambient mineral shimmer particles
    mineralTimer: 0,
    alerts: [],        // "Nuclear launch detected" style sweep alerts
    alertTimer: 0,
};

function starcraftInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const vw = canvas.width / dpr;
    const vh = canvas.height / dpr;

    // Star field
    starcraftState.stars = [];
    const starCount = Math.floor((vw * vh) / 1200);
    for (let i = 0; i < starCount; i++) {
        starcraftState.stars.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            size: 0.3 + Math.random() * 1.2,
            brightness: 0.2 + Math.random() * 0.6,
            twinkleSpeed: 1 + Math.random() * 3,
            twinklePhase: Math.random() * Math.PI * 2,
        });
    }

    // Mineral shimmer points (scattered across canvas)
    starcraftState.minerals = [];
    for (let i = 0; i < 12; i++) {
        starcraftState.minerals.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            phase: Math.random() * Math.PI * 2,
            size: 1 + Math.random() * 2,
        });
    }

    starcraftState.gridPhase = 0;
    starcraftState.scanAngle = 0;
    starcraftState.alerts = [];
    starcraftState.alertTimer = 0;
    starcraftState.mineralTimer = 0;
    starcraftState.initialized = true;
}

function starcraftRenderAmbient(ctx, w, h, dpr, dt) {
    if (!starcraftState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;
    const now = performance.now() / 1000;

    // ── Star field ──
    for (const star of starcraftState.stars) {
        const twinkle = 0.5 + 0.5 * Math.sin(now * star.twinkleSpeed + star.twinklePhase);
        const alpha = star.brightness * twinkle;
        ctx.globalAlpha = alpha;

        // Slight blue-white tint for larger stars
        if (star.size > 0.8) {
            ctx.fillStyle = '#aaddff';
        } else {
            ctx.fillStyle = '#ffffff';
        }
        ctx.beginPath();
        ctx.arc(star.x, star.y, star.size, 0, Math.PI * 2);
        ctx.fill();
    }

    // ── Hex tactical grid ──
    starcraftState.gridPhase += dt * 0.3;
    ctx.globalAlpha = 0.03;
    ctx.strokeStyle = '#3388cc';
    ctx.lineWidth = 0.5;

    const hexSize = 40;
    const hexH = hexSize * Math.sqrt(3);
    const cols = Math.ceil(vw / (hexSize * 1.5)) + 1;
    const rows = Math.ceil(vh / hexH) + 1;

    for (let row = -1; row < rows; row++) {
        for (let col = -1; col < cols; col++) {
            const cx = col * hexSize * 1.5;
            const cy = row * hexH + (col % 2 ? hexH / 2 : 0);
            drawHex(ctx, cx, cy, hexSize * 0.5);
        }
    }

    // ── Mineral shimmer ──
    starcraftState.mineralTimer += dt;
    for (const m of starcraftState.minerals) {
        m.phase += dt * 2;
        const shimmer = 0.5 + 0.5 * Math.sin(m.phase);
        ctx.globalAlpha = shimmer * 0.15;
        ctx.fillStyle = '#44ccff';
        ctx.beginPath();
        ctx.arc(m.x, m.y, m.size, 0, Math.PI * 2);
        ctx.fill();

        // Glow halo
        ctx.globalAlpha = shimmer * 0.05;
        ctx.beginPath();
        ctx.arc(m.x, m.y, m.size * 4, 0, Math.PI * 2);
        ctx.fill();
    }

    // Respawn minerals that fade (keeps them drifting)
    if (starcraftState.mineralTimer > 4) {
        starcraftState.mineralTimer = 0;
        const idx = Math.floor(Math.random() * starcraftState.minerals.length);
        starcraftState.minerals[idx].x = Math.random() * vw;
        starcraftState.minerals[idx].y = Math.random() * vh;
    }

    // ── Scan sweep (subtle Terran sensor sweep) ──
    starcraftState.scanAngle += dt * (Math.PI * 2 / 15); // 15s per revolution
    if (starcraftState.scanAngle > Math.PI * 2) starcraftState.scanAngle -= Math.PI * 2;

    const scx = vw / 2, scy = vh / 2;
    const scanR = Math.max(vw, vh) * 0.6;
    const sweepEnd = starcraftState.scanAngle;
    const sweepArc = 0.3;

    const scanGrad = ctx.createConicGradient(sweepEnd - sweepArc, scx, scy);
    scanGrad.addColorStop(0, 'rgba(51, 136, 204, 0)');
    scanGrad.addColorStop(sweepArc / (Math.PI * 2), 'rgba(51, 136, 204, 0.04)');
    scanGrad.addColorStop(sweepArc / (Math.PI * 2) + 0.001, 'rgba(51, 136, 204, 0)');
    scanGrad.addColorStop(1, 'rgba(51, 136, 204, 0)');
    ctx.globalAlpha = 1;
    ctx.fillStyle = scanGrad;
    ctx.beginPath();
    ctx.moveTo(scx, scy);
    ctx.arc(scx, scy, scanR, 0, Math.PI * 2);
    ctx.closePath();
    ctx.fill();

    // ── Alert sweeps ("Nuclear launch detected" horizontal scan) ──
    starcraftState.alertTimer += dt;
    if (starcraftState.alertTimer > 12 + Math.random() * 8) {
        starcraftState.alertTimer = 0;
        starcraftState.alerts.push({ y: -10, speed: 100 + Math.random() * 60 });
    }
    for (let i = starcraftState.alerts.length - 1; i >= 0; i--) {
        const a = starcraftState.alerts[i];
        a.y += a.speed * dt;
        if (a.y > vh + 20) {
            starcraftState.alerts.splice(i, 1);
            continue;
        }
        ctx.globalAlpha = 0.06;
        ctx.fillStyle = '#ff4444';
        ctx.fillRect(0, a.y - 1, vw, 2);
        ctx.globalAlpha = 0.02;
        ctx.fillRect(0, a.y - 8, vw, 16);
    }

    // ── Subtle vignette ──
    ctx.globalAlpha = 1;
    const vigGrad = ctx.createRadialGradient(vw / 2, vh / 2, Math.min(vw, vh) * 0.3,
        vw / 2, vh / 2, Math.max(vw, vh) * 0.65);
    vigGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
    vigGrad.addColorStop(1, 'rgba(0, 0, 0, 0.4)');
    ctx.fillStyle = vigGrad;
    ctx.fillRect(0, 0, vw, vh);

    ctx.restore();
}

function drawHex(ctx, cx, cy, r) {
    ctx.beginPath();
    for (let i = 0; i < 6; i++) {
        const angle = (Math.PI / 3) * i - Math.PI / 6;
        const px = cx + r * Math.cos(angle);
        const py = cy + r * Math.sin(angle);
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
    }
    ctx.closePath();
    ctx.stroke();
}

registerTheme('starcraft', {
    name: 'StarCraft',
    colors: {
        void: '#030912',              // Deep space
        cyan: '#44ccff',              // Terran blue (scanner, UI)
        brightCyan: '#88ddff',        // Bright Terran blue
        amber: '#ffaa22',             // Warning orange (Terran alert)
        green: '#44ff66',             // Mineral green / completion
        red: '#ff4444',               // Under attack red
        purple: '#aa66ff',            // Protoss purple
        gray: '#445566',              // Inactive console
        nodeFill: 'rgba(4, 12, 28, 0.6)',
    },
    stateColors: {
        Active: '#44ccff',             // Terran blue — operational
        Idle: '#3388aa',               // Dim blue — standby
        Thinking: '#aa66ff',           // Protoss purple — psionic processing
        ToolCalling: '#ffaa22',        // Orange — SCV working
        WaitingPermission: '#ffaa22',  // Orange — awaiting orders
        Complete: '#44ff66',           // Green — objective complete
        Error: '#ff4444',              // Red — unit lost
        Failed: '#ff4444',
        TimedOut: '#445566',           // Gray — signal lost
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#030912';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: starcraftInitAmbient,
    renderAmbient: starcraftRenderAmbient,
    nodeGlow(ctx, x, y, radius, stateColor, isMain) {
        // Terran shield glow — bright core, blue falloff
        const gradient = ctx.createRadialGradient(x, y, radius * 0.2, x, y, radius + 14);
        gradient.addColorStop(0, 'rgba(68, 204, 255, 0.15)');
        gradient.addColorStop(0.5, 'rgba(68, 204, 255, 0.05)');
        gradient.addColorStop(1, 'rgba(68, 204, 255, 0)');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(x, y, radius + 14, 0, Math.PI * 2);
        ctx.fill();
    },
    toolCardBorder(state) {
        if (state === 'Error') return '#ff4444';
        if (state === 'Complete') return '#44ff66';
        if (state === 'Running') return '#ffaa22';
        return '#3388aa';
    },
    edgeColor: 'rgba(68, 204, 255, 0.15)',
    edgeActiveColor: 'rgba(68, 204, 255, 0.45)',
    labelColor: '#44ccff',
    labelFont: '9px monospace',

    // StarCraft FX: warp-in spawn, objective complete, unit lost error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Protoss warp-in: converging particles + bright flash
        for (let i = 0; i < 16; i++) {
            const angle = Math.random() * Math.PI * 2;
            const dist = r * 3 + Math.random() * r * 2;
            fx._emit(x + Math.cos(angle) * dist, y + Math.sin(angle) * dist, {
                // Particles move INWARD toward spawn point
                vx: -Math.cos(angle) * (60 + Math.random() * 40),
                vy: -Math.sin(angle) * (60 + Math.random() * 40),
                color: i < 8 ? '#88ddff' : '#aa66ff',
                size: 1.5 + Math.random(),
                life: 0.5 + Math.random() * 0.3,
                drag: 0.95,
                glow: true,
                trail: true,
            });
        }
        // Warp flash
        fx.effects.push({
            type: 'flash', x, y, color: '#44ccff',
            born: performance.now() / 1000 + 0.3, duration: 0.4,
            maxRadius: r * 2.5,
        });
        // Psionic ring
        fx.effects.push({
            type: 'ring', x, y, color: '#aa66ff',
            born: performance.now() / 1000 + 0.2, duration: 0.8,
            startRadius: r * 0.3, maxRadius: r * 3,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        const r = radius || 28;
        // Objective complete: green mineral burst + confirmation
        fx.effects.push({
            type: 'flash', x, y, color: '#44ff66',
            born: performance.now() / 1000, duration: 0.4,
            maxRadius: r * 2,
        });
        // Rising mineral-green particles
        for (let i = 0; i < 12; i++) {
            fx._emit(x + (Math.random() - 0.5) * r, y + (Math.random() - 0.5) * r * 0.5, {
                vx: (Math.random() - 0.5) * 30,
                vy: -40 - Math.random() * 60,
                color: i < 6 ? '#44ff66' : '#44ccff',
                size: 1.5 + Math.random(),
                life: 0.6 + Math.random() * 0.5,
                drag: 0.97,
                glow: true,
            });
        }
        // Expanding confirmation ring
        fx.effects.push({
            type: 'ring', x, y, color: '#44ff66',
            born: performance.now() / 1000, duration: 0.8,
            startRadius: r, maxRadius: r * 3.5,
        });
    },

    fxError(fx, x, y, color, radius) {
        // Unit lost: Terran explosion — orange/red fireball + debris
        const r = radius || 28;
        // Initial bright flash
        fx.effects.push({
            type: 'flash', x, y, color: '#ff6622',
            born: performance.now() / 1000, duration: 0.35,
            maxRadius: r * 3,
        });
        // Explosion ring
        fx.effects.push({
            type: 'ring', x, y, color: '#ff4444',
            born: performance.now() / 1000, duration: 1.0,
            startRadius: r, maxRadius: r * 5,
        });
        // Debris and fire particles
        for (let i = 0; i < 20; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 50 + Math.random() * 90;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed - 20, // slight upward bias (explosion plume)
                color: i < 6 ? '#ff4444' : i < 12 ? '#ff8833' : '#ffcc44',
                size: 1.5 + Math.random() * 2,
                life: 0.4 + Math.random() * 0.6,
                drag: 0.93,
                glow: true,
            });
        }
        // Wreckage cracks
        for (let i = 0; i < 5; i++) {
            fx.effects.push({
                type: 'crack', x, y, color: '#ff6622',
                angle: (i / 5) * Math.PI * 2 + Math.random() * 0.4,
                born: performance.now() / 1000, duration: 1.0,
                length: r * 2 + Math.random() * 15,
                width: 1 + Math.random(),
            });
        }
    },
});

// ─── Theme: Zerg Hive ─────────────────────────────────

const zergState = {
    initialized: false,
    creepTendrils: [],   // Animated creep spread lines
    spores: [],          // Floating spore particles
    heartbeat: 0,        // Hive pulsation timer
    veins: [],           // Static organic vein network
    ambientCanvas: null,
    ambientCtx: null,
};

function zergInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const vw = canvas.width / dpr;
    const vh = canvas.height / dpr;

    // Generate organic vein network (static backdrop)
    zergState.veins = [];
    const veinCount = 15 + Math.floor(Math.random() * 10);
    for (let i = 0; i < veinCount; i++) {
        const startX = Math.random() * vw;
        const startY = Math.random() * vh;
        const segments = 6 + Math.floor(Math.random() * 8);
        const points = [{ x: startX, y: startY }];
        let cx = startX, cy = startY;
        let angle = Math.random() * Math.PI * 2;
        for (let s = 0; s < segments; s++) {
            angle += (Math.random() - 0.5) * 1.2;
            const len = 20 + Math.random() * 50;
            cx += Math.cos(angle) * len;
            cy += Math.sin(angle) * len;
            points.push({ x: cx, y: cy });
        }
        zergState.veins.push({
            points,
            width: 0.5 + Math.random() * 1.5,
            alpha: 0.03 + Math.random() * 0.04,
        });
    }

    // Creep tendrils — slowly spreading organic lines
    zergState.creepTendrils = [];
    for (let i = 0; i < 8; i++) {
        zergState.creepTendrils.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            angle: Math.random() * Math.PI * 2,
            length: 0,
            maxLength: 60 + Math.random() * 100,
            speed: 15 + Math.random() * 25,
            segments: [],
            life: 0,
            maxLife: 4 + Math.random() * 4,
        });
    }

    // Floating spore particles
    zergState.spores = [];
    for (let i = 0; i < 20; i++) {
        zergState.spores.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            vx: (Math.random() - 0.5) * 8,
            vy: -3 - Math.random() * 6,
            size: 1 + Math.random() * 2,
            phase: Math.random() * Math.PI * 2,
            life: Math.random() * 6,
        });
    }

    zergState.heartbeat = 0;
    zergState.initialized = true;
}

function zergRenderAmbient(ctx, w, h, dpr, dt) {
    if (!zergState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;
    const now = performance.now() / 1000;

    // ── Hive heartbeat pulse (background throb) ──
    zergState.heartbeat += dt;
    const pulse = Math.sin(zergState.heartbeat * 1.5) * 0.5 + 0.5; // 0..1
    ctx.globalAlpha = pulse * 0.015;
    ctx.fillStyle = '#6622aa';
    ctx.fillRect(0, 0, vw, vh);

    // ── Organic vein network ──
    for (const vein of zergState.veins) {
        ctx.globalAlpha = vein.alpha + pulse * 0.01;
        ctx.strokeStyle = '#8833bb';
        ctx.lineWidth = vein.width;
        ctx.beginPath();
        ctx.moveTo(vein.points[0].x, vein.points[0].y);
        for (let i = 1; i < vein.points.length; i++) {
            // Bezier for organic feel
            const prev = vein.points[i - 1];
            const curr = vein.points[i];
            const cpx = (prev.x + curr.x) / 2 + (Math.random() - 0.5) * 5;
            const cpy = (prev.y + curr.y) / 2 + (Math.random() - 0.5) * 5;
            ctx.quadraticCurveTo(cpx, cpy, curr.x, curr.y);
        }
        ctx.stroke();
    }

    // ── Creep tendrils (animated spread) ──
    for (const t of zergState.creepTendrils) {
        t.life += dt;
        if (t.life > t.maxLife) {
            // Reset tendril
            t.x = Math.random() * vw;
            t.y = Math.random() * vh;
            t.angle = Math.random() * Math.PI * 2;
            t.length = 0;
            t.segments = [{ x: t.x, y: t.y }];
            t.life = 0;
            t.maxLength = 60 + Math.random() * 100;
            continue;
        }

        // Grow tendril
        if (t.length < t.maxLength) {
            t.length += t.speed * dt;
            t.angle += (Math.random() - 0.5) * 1.5 * dt;
            const tip = t.segments[t.segments.length - 1] || { x: t.x, y: t.y };
            const nx = tip.x + Math.cos(t.angle) * t.speed * dt;
            const ny = tip.y + Math.sin(t.angle) * t.speed * dt;
            t.segments.push({ x: nx, y: ny });
        }

        // Draw tendril (fading as it ages)
        const ageFade = Math.max(0, 1 - (t.life / t.maxLife));
        ctx.globalAlpha = ageFade * 0.12;
        ctx.strokeStyle = '#aa44dd';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        if (t.segments.length > 0) {
            ctx.moveTo(t.segments[0].x, t.segments[0].y);
            for (let i = 1; i < t.segments.length; i++) {
                ctx.lineTo(t.segments[i].x, t.segments[i].y);
            }
        }
        ctx.stroke();

        // Glow at tip
        if (t.segments.length > 0 && t.length < t.maxLength) {
            const tip = t.segments[t.segments.length - 1];
            ctx.globalAlpha = ageFade * 0.2;
            ctx.fillStyle = '#cc66ff';
            ctx.beginPath();
            ctx.arc(tip.x, tip.y, 3, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    // ── Floating spores ──
    for (const s of zergState.spores) {
        s.x += s.vx * dt;
        s.y += s.vy * dt;
        s.phase += dt * 2;
        s.life += dt;

        // Wrap around
        if (s.y < -10) { s.y = vh + 10; s.x = Math.random() * vw; }
        if (s.x < -10) s.x = vw + 10;
        if (s.x > vw + 10) s.x = -10;

        const wobble = Math.sin(s.phase) * 0.3;
        ctx.globalAlpha = 0.08 + wobble * 0.04;
        ctx.fillStyle = '#88ee44'; // Acid green spores
        ctx.beginPath();
        ctx.arc(s.x, s.y, s.size, 0, Math.PI * 2);
        ctx.fill();
    }

    // ── Organic vignette (darker, more oppressive) ──
    ctx.globalAlpha = 1;
    const vigGrad = ctx.createRadialGradient(vw / 2, vh / 2, Math.min(vw, vh) * 0.2,
        vw / 2, vh / 2, Math.max(vw, vh) * 0.6);
    vigGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
    vigGrad.addColorStop(1, 'rgba(0, 0, 0, 0.55)');
    ctx.fillStyle = vigGrad;
    ctx.fillRect(0, 0, vw, vh);

    ctx.restore();
}

registerTheme('zerg', {
    name: 'Zerg Hive',
    colors: {
        void: '#08020e',              // Deep hive purple-black
        cyan: '#aa44dd',              // Zerg purple (primary)
        brightCyan: '#cc66ff',        // Bright psionic purple
        amber: '#88ee44',             // Acid green (bile/execute)
        green: '#88ee44',             // Acid green
        red: '#ff4444',               // Tissue damage red
        purple: '#aa44dd',            // Zerg purple
        gray: '#443355',              // Chitinous gray
        nodeFill: 'rgba(12, 4, 20, 0.6)',
    },
    stateColors: {
        Active: '#aa44dd',             // Purple — active bio-process
        Idle: '#774499',               // Dim purple — dormant
        Thinking: '#cc66ff',           // Bright purple — psionic link
        ToolCalling: '#88ee44',        // Acid green — spawning/building
        WaitingPermission: '#ddaa22',  // Amber — awaiting Overmind directive
        Complete: '#88ee44',           // Acid green — evolution complete
        Error: '#ff4444',              // Red — organism lost
        Failed: '#ff4444',
        TimedOut: '#443355',           // Dark — decayed
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#08020e';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: zergInitAmbient,
    renderAmbient: zergRenderAmbient,
    nodeGlow(ctx, x, y, radius, stateColor, isMain) {
        // Organic bioluminescent glow
        const gradient = ctx.createRadialGradient(x, y, radius * 0.2, x, y, radius + 18);
        gradient.addColorStop(0, 'rgba(170, 68, 221, 0.18)');
        gradient.addColorStop(0.4, 'rgba(170, 68, 221, 0.06)');
        gradient.addColorStop(1, 'rgba(170, 68, 221, 0)');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(x, y, radius + 18, 0, Math.PI * 2);
        ctx.fill();
    },
    toolCardBorder(state) {
        if (state === 'Error') return '#ff4444';
        if (state === 'Complete') return '#88ee44';
        if (state === 'Running') return '#aa44dd';
        return '#774499';
    },
    edgeColor: 'rgba(170, 68, 221, 0.18)',
    edgeActiveColor: 'rgba(170, 68, 221, 0.5)',
    labelColor: '#aa44dd',
    labelFont: '9px monospace',

    // Zerg FX: hatchery spawn, evolution complete, organism lost
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Hatchery spawn: egg burst — particles spray outward from creep
        for (let i = 0; i < 18; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 30 + Math.random() * 50;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i < 8 ? '#cc66ff' : i < 14 ? '#aa44dd' : '#88ee44',
                size: 1.5 + Math.random() * 1.5,
                life: 0.5 + Math.random() * 0.5,
                drag: 0.94,
                glow: true,
                trail: true,
            });
        }
        // Organic membrane burst ring
        fx.effects.push({
            type: 'ring', x, y, color: '#aa44dd',
            born: performance.now() / 1000, duration: 0.8,
            startRadius: r * 0.3, maxRadius: r * 3,
        });
        // Acid splash flash
        fx.effects.push({
            type: 'flash', x, y, color: '#88ee44',
            born: performance.now() / 1000, duration: 0.3,
            maxRadius: r * 1.5,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        const r = radius || 28;
        // Evolution complete: bioluminescent pulse + rising spore cloud
        fx.effects.push({
            type: 'flash', x, y, color: '#88ee44',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: r * 2,
        });
        // Rising spore particles
        for (let i = 0; i < 14; i++) {
            fx._emit(x + (Math.random() - 0.5) * r * 1.5, y + (Math.random() - 0.5) * r * 0.5, {
                vx: (Math.random() - 0.5) * 25,
                vy: -20 - Math.random() * 50,
                color: i < 6 ? '#88ee44' : '#aa44dd',
                size: 1 + Math.random() * 1.5,
                life: 0.8 + Math.random() * 0.6,
                drag: 0.98,
                glow: true,
            });
        }
        // Expanding creep ring
        fx.effects.push({
            type: 'ring', x, y, color: '#aa44dd',
            born: performance.now() / 1000, duration: 1.0,
            startRadius: r, maxRadius: r * 4,
        });
    },

    fxError(fx, x, y, color, radius) {
        // Organism lost: acid blood burst + tissue spray + carapace cracks
        const r = radius || 28;
        // Acid blood flash
        fx.effects.push({
            type: 'flash', x, y, color: '#88ee44',
            born: performance.now() / 1000, duration: 0.3,
            maxRadius: r * 2.5,
        });
        // Tissue destruction ring
        fx.effects.push({
            type: 'ring', x, y, color: '#ff4444',
            born: performance.now() / 1000, duration: 1.2,
            startRadius: r, maxRadius: r * 5,
        });
        // Acid blood and tissue particles
        for (let i = 0; i < 22; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 40 + Math.random() * 70;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i < 8 ? '#88ee44' : i < 15 ? '#ff4444' : '#aa44dd',
                size: 1.5 + Math.random() * 2,
                life: 0.4 + Math.random() * 0.6,
                drag: 0.92,
                glow: true,
            });
        }
        // Carapace crack lines
        for (let i = 0; i < 6; i++) {
            fx.effects.push({
                type: 'crack', x, y, color: '#88ee44',
                angle: (i / 6) * Math.PI * 2 + Math.random() * 0.3,
                born: performance.now() / 1000, duration: 1.3,
                length: r * 2 + Math.random() * 15,
                width: 1 + Math.random(),
            });
        }
    },
});

// ─── Theme: Protoss (Aiur / Psionic) ─────────────────

const protossState = {
    initialized: false,
    pylonFields: [],     // Pylon power field circles
    warpStreaks: [],      // Warp conduit energy streaks
    streakTimer: 0,
    crystals: [],         // Khaydarin crystal formations
    nexusPhase: 0,        // Nexus energy rotation
    psiStormTimer: 0,     // Occasional psi storm crackle
    psiStormActive: false,
    psiStormX: 0,
    psiStormY: 0,
    psiStormLife: 0,
};

function protossInitAmbient(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const vw = canvas.width / dpr;
    const vh = canvas.height / dpr;

    // Pylon power fields — soft glowing hexagonal areas
    protossState.pylonFields = [];
    const fieldCount = 5 + Math.floor(Math.random() * 4);
    for (let i = 0; i < fieldCount; i++) {
        protossState.pylonFields.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            radius: 40 + Math.random() * 60,
            phase: Math.random() * Math.PI * 2,
            pulseSpeed: 0.8 + Math.random() * 0.6,
        });
    }

    // Khaydarin crystal formations (stationary glowing points)
    protossState.crystals = [];
    for (let i = 0; i < 10; i++) {
        protossState.crystals.push({
            x: Math.random() * vw,
            y: Math.random() * vh,
            size: 2 + Math.random() * 3,
            phase: Math.random() * Math.PI * 2,
            color: Math.random() > 0.3 ? '#44aaff' : '#aaccff',
        });
    }

    // Warp conduit streaks
    protossState.warpStreaks = [];
    protossState.streakTimer = 0;
    protossState.nexusPhase = 0;
    protossState.psiStormTimer = 0;
    protossState.psiStormActive = false;
    protossState.initialized = true;
}

function protossRenderAmbient(ctx, w, h, dpr, dt) {
    if (!protossState.initialized) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    const vw = w / dpr;
    const vh = h / dpr;
    const now = performance.now() / 1000;

    // ── Pylon power fields (pulsing hexagonal glow zones) ──
    for (const pf of protossState.pylonFields) {
        pf.phase += pf.pulseSpeed * dt;
        const pulse = 0.5 + 0.5 * Math.sin(pf.phase);
        const alpha = 0.015 + pulse * 0.015;

        // Soft radial glow
        ctx.globalAlpha = alpha;
        const grad = ctx.createRadialGradient(pf.x, pf.y, 0, pf.x, pf.y, pf.radius);
        grad.addColorStop(0, 'rgba(68, 170, 255, 0.15)');
        grad.addColorStop(0.6, 'rgba(68, 170, 255, 0.04)');
        grad.addColorStop(1, 'rgba(68, 170, 255, 0)');
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.arc(pf.x, pf.y, pf.radius, 0, Math.PI * 2);
        ctx.fill();

        // Faint hexagonal boundary
        ctx.globalAlpha = alpha * 0.6;
        ctx.strokeStyle = '#44aaff';
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        for (let i = 0; i < 6; i++) {
            const angle = (Math.PI / 3) * i - Math.PI / 6 + pf.phase * 0.1;
            const px = pf.x + pf.radius * 0.8 * Math.cos(angle);
            const py = pf.y + pf.radius * 0.8 * Math.sin(angle);
            if (i === 0) ctx.moveTo(px, py);
            else ctx.lineTo(px, py);
        }
        ctx.closePath();
        ctx.stroke();
    }

    // ── Khaydarin crystals (stationary glowing diamond shapes) ──
    for (const c of protossState.crystals) {
        c.phase += dt * 1.5;
        const shimmer = 0.5 + 0.5 * Math.sin(c.phase);

        // Crystal glow halo
        ctx.globalAlpha = shimmer * 0.08;
        ctx.fillStyle = c.color;
        ctx.beginPath();
        ctx.arc(c.x, c.y, c.size * 4, 0, Math.PI * 2);
        ctx.fill();

        // Crystal shape (diamond)
        ctx.globalAlpha = 0.15 + shimmer * 0.1;
        ctx.fillStyle = c.color;
        ctx.beginPath();
        ctx.moveTo(c.x, c.y - c.size);
        ctx.lineTo(c.x + c.size * 0.5, c.y);
        ctx.lineTo(c.x, c.y + c.size);
        ctx.lineTo(c.x - c.size * 0.5, c.y);
        ctx.closePath();
        ctx.fill();
    }

    // ── Warp conduit energy streaks ──
    protossState.streakTimer += dt;
    if (protossState.streakTimer > 0.8 + Math.random() * 1.5) {
        protossState.streakTimer = 0;
        if (protossState.warpStreaks.length < 6) {
            const fromTop = Math.random() > 0.5;
            protossState.warpStreaks.push({
                x: fromTop ? Math.random() * vw : -20,
                y: fromTop ? -20 : Math.random() * vh,
                angle: fromTop ? Math.PI * 0.3 + Math.random() * 0.4 : Math.PI * -0.2 + Math.random() * 0.4,
                speed: 200 + Math.random() * 300,
                length: 30 + Math.random() * 50,
                life: 0,
                maxLife: 1.5 + Math.random(),
                width: 0.5 + Math.random() * 1,
            });
        }
    }

    for (let i = protossState.warpStreaks.length - 1; i >= 0; i--) {
        const s = protossState.warpStreaks[i];
        s.life += dt;
        if (s.life > s.maxLife) {
            protossState.warpStreaks.splice(i, 1);
            continue;
        }

        s.x += Math.cos(s.angle) * s.speed * dt;
        s.y += Math.sin(s.angle) * s.speed * dt;

        const fade = Math.max(0, 1 - (s.life / s.maxLife));
        ctx.globalAlpha = fade * 0.2;
        ctx.strokeStyle = '#66bbff';
        ctx.lineWidth = s.width;
        ctx.beginPath();
        ctx.moveTo(s.x, s.y);
        ctx.lineTo(
            s.x - Math.cos(s.angle) * s.length,
            s.y - Math.sin(s.angle) * s.length
        );
        ctx.stroke();

        // Bright tip
        ctx.globalAlpha = fade * 0.4;
        ctx.fillStyle = '#aaddff';
        ctx.beginPath();
        ctx.arc(s.x, s.y, s.width + 0.5, 0, Math.PI * 2);
        ctx.fill();
    }

    // ── Nexus energy rotation (faint concentric arcs at center) ──
    protossState.nexusPhase += dt * 0.5;
    const ncx = vw / 2, ncy = vh / 2;
    ctx.globalAlpha = 0.025;
    ctx.strokeStyle = '#44aaff';
    ctx.lineWidth = 1;
    for (let ring = 0; ring < 3; ring++) {
        const r = 80 + ring * 60;
        const startAngle = protossState.nexusPhase + ring * 0.8;
        const arcLen = 0.6 + ring * 0.3;
        ctx.beginPath();
        ctx.arc(ncx, ncy, r, startAngle, startAngle + arcLen);
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(ncx, ncy, r, startAngle + Math.PI, startAngle + Math.PI + arcLen);
        ctx.stroke();
    }

    // ── Psi storm crackle (rare) ──
    protossState.psiStormTimer += dt;
    if (!protossState.psiStormActive && protossState.psiStormTimer > 15 + Math.random() * 20) {
        protossState.psiStormTimer = 0;
        protossState.psiStormActive = true;
        protossState.psiStormX = Math.random() * vw;
        protossState.psiStormY = Math.random() * vh;
        protossState.psiStormLife = 0;
    }

    if (protossState.psiStormActive) {
        protossState.psiStormLife += dt;
        if (protossState.psiStormLife > 1.5) {
            protossState.psiStormActive = false;
        } else {
            const intensity = Math.max(0, 1 - protossState.psiStormLife / 1.5);
            ctx.globalAlpha = intensity * 0.25;
            ctx.strokeStyle = '#aaddff';
            ctx.lineWidth = 1;
            for (let b = 0; b < 3; b++) {
                ctx.beginPath();
                let bx = protossState.psiStormX;
                let by = protossState.psiStormY;
                ctx.moveTo(bx, by);
                const segs = 4 + Math.floor(Math.random() * 4);
                for (let s = 0; s < segs; s++) {
                    bx += (Math.random() - 0.5) * 40;
                    by += (Math.random() - 0.5) * 40;
                    ctx.lineTo(bx, by);
                }
                ctx.stroke();
            }
            ctx.globalAlpha = intensity * 0.1;
            ctx.fillStyle = '#44aaff';
            ctx.beginPath();
            ctx.arc(protossState.psiStormX, protossState.psiStormY, 25, 0, Math.PI * 2);
            ctx.fill();
        }
    }

    // ── Subtle vignette ──
    ctx.globalAlpha = 1;
    const vigGrad = ctx.createRadialGradient(vw / 2, vh / 2, Math.min(vw, vh) * 0.3,
        vw / 2, vh / 2, Math.max(vw, vh) * 0.65);
    vigGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
    vigGrad.addColorStop(1, 'rgba(0, 0, 0, 0.45)');
    ctx.fillStyle = vigGrad;
    ctx.fillRect(0, 0, vw, vh);

    ctx.restore();
}

registerTheme('protoss', {
    name: 'Protoss',
    colors: {
        void: '#020818',              // Deep void blue-black (Aiur night sky)
        cyan: '#44aaff',              // Psionic blue (primary)
        brightCyan: '#aaddff',        // Bright psionic
        amber: '#ffcc44',             // Khaydarin gold (energy/exec)
        green: '#44ddaa',             // Shields restored / success
        red: '#ff5555',               // Shield breach red
        purple: '#8866cc',            // Void energy (dark templar)
        gray: '#334466',              // Inactive / powered down
        nodeFill: 'rgba(4, 10, 30, 0.6)',
    },
    stateColors: {
        Active: '#44aaff',             // Psionic blue — active
        Idle: '#2266aa',              // Dim blue — standby
        Thinking: '#8866cc',           // Void purple — channeling
        ToolCalling: '#ffcc44',        // Gold — warping in / fabricating
        WaitingPermission: '#ffcc44',  // Gold — awaiting Conclave approval
        Complete: '#44ddaa',           // Teal — shields restored / success
        Error: '#ff5555',              // Red — shield breach
        Failed: '#ff5555',
        TimedOut: '#334466',           // Gray — signal lost
    },
    clearCanvas(ctx, w, h) {
        ctx.fillStyle = '#020818';
        ctx.fillRect(0, 0, w, h);
    },
    initAmbient: protossInitAmbient,
    renderAmbient: protossRenderAmbient,
    nodeGlow(ctx, x, y, radius, stateColor, isMain) {
        // Psionic shield glow — layered blue radiance
        const gradient = ctx.createRadialGradient(x, y, radius * 0.15, x, y, radius + 18);
        gradient.addColorStop(0, 'rgba(68, 170, 255, 0.2)');
        gradient.addColorStop(0.3, 'rgba(68, 170, 255, 0.08)');
        gradient.addColorStop(0.7, 'rgba(136, 102, 204, 0.03)');
        gradient.addColorStop(1, 'rgba(68, 170, 255, 0)');
        ctx.fillStyle = gradient;
        ctx.beginPath();
        ctx.arc(x, y, radius + 18, 0, Math.PI * 2);
        ctx.fill();
    },
    toolCardBorder(state) {
        if (state === 'Error') return '#ff5555';
        if (state === 'Complete') return '#44ddaa';
        if (state === 'Running') return '#ffcc44';
        return '#2266aa';
    },
    edgeColor: 'rgba(68, 170, 255, 0.15)',
    edgeActiveColor: 'rgba(68, 170, 255, 0.5)',
    labelColor: '#44aaff',
    labelFont: '9px monospace',

    // Protoss FX: warp-in spawn, shields restored complete, shield breach error
    fxSpawn(fx, x, y, color, radius) {
        const r = radius || 28;
        // Warp-in: bright converging particles + psionic flash + gateway ring
        for (let i = 0; i < 20; i++) {
            const angle = Math.random() * Math.PI * 2;
            const dist = r * 3 + Math.random() * r * 2;
            fx._emit(x + Math.cos(angle) * dist, y + Math.sin(angle) * dist, {
                vx: -Math.cos(angle) * (70 + Math.random() * 50),
                vy: -Math.sin(angle) * (70 + Math.random() * 50),
                color: i < 10 ? '#aaddff' : i < 16 ? '#44aaff' : '#ffcc44',
                size: 1.5 + Math.random(),
                life: 0.4 + Math.random() * 0.3,
                drag: 0.94,
                glow: true,
                trail: true,
            });
        }
        // Warp gateway flash (delayed — after particles converge)
        fx.effects.push({
            type: 'flash', x, y, color: '#44aaff',
            born: performance.now() / 1000 + 0.25, duration: 0.5,
            maxRadius: r * 3,
        });
        // Gateway ring (expanding outward)
        fx.effects.push({
            type: 'ring', x, y, color: '#aaddff',
            born: performance.now() / 1000 + 0.2, duration: 0.8,
            startRadius: r * 0.2, maxRadius: r * 4,
        });
        // Secondary gold khaydarin ring
        fx.effects.push({
            type: 'ring', x, y, color: '#ffcc44',
            born: performance.now() / 1000 + 0.35, duration: 0.6,
            startRadius: r * 0.5, maxRadius: r * 2.5,
        });
    },

    fxComplete(fx, x, y, color, radius) {
        const r = radius || 28;
        // Shields restored: teal pulse + ascending energy + golden confirmation
        fx.effects.push({
            type: 'flash', x, y, color: '#44ddaa',
            born: performance.now() / 1000, duration: 0.5,
            maxRadius: r * 2.5,
        });
        // Ascending psionic energy
        for (let i = 0; i < 14; i++) {
            fx._emit(x + (Math.random() - 0.5) * r, y + (Math.random() - 0.5) * r * 0.5, {
                vx: (Math.random() - 0.5) * 20,
                vy: -35 - Math.random() * 55,
                color: i < 5 ? '#44ddaa' : i < 10 ? '#44aaff' : '#ffcc44',
                size: 1.5 + Math.random(),
                life: 0.7 + Math.random() * 0.5,
                drag: 0.97,
                glow: true,
            });
        }
        // Shield restoration ring
        fx.effects.push({
            type: 'ring', x, y, color: '#44ddaa',
            born: performance.now() / 1000, duration: 1.0,
            startRadius: r, maxRadius: r * 4,
        });
    },

    fxError(fx, x, y, color, radius) {
        // Shield breach: red flash + shattering psi-fragments + void collapse
        const r = radius || 28;
        // Shield shatter flash
        fx.effects.push({
            type: 'flash', x, y, color: '#ff5555',
            born: performance.now() / 1000, duration: 0.35,
            maxRadius: r * 3,
        });
        // Void collapse ring
        fx.effects.push({
            type: 'ring', x, y, color: '#8866cc',
            born: performance.now() / 1000, duration: 1.2,
            startRadius: r, maxRadius: r * 5,
        });
        // Psi-fragment shards (angular outward burst)
        for (let i = 0; i < 20; i++) {
            const angle = (i / 20) * Math.PI * 2 + Math.random() * 0.2;
            const speed = 50 + Math.random() * 80;
            fx._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color: i < 8 ? '#ff5555' : i < 14 ? '#44aaff' : '#8866cc',
                size: 1.5 + Math.random() * 2,
                life: 0.4 + Math.random() * 0.5,
                drag: 0.93,
                glow: true,
            });
        }
        // Shield crack lines (energy fracture)
        for (let i = 0; i < 6; i++) {
            fx.effects.push({
                type: 'crack', x, y, color: '#44aaff',
                angle: (i / 6) * Math.PI * 2 + Math.random() * 0.3,
                born: performance.now() / 1000, duration: 1.0,
                length: r * 2 + Math.random() * 15,
                width: 1 + Math.random(),
            });
        }
    },
});
