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
