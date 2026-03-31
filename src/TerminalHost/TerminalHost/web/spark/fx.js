/**
 * Spark Canvas — Visual Effects System.
 * Particle-based effects for agent lifecycle events:
 *   spawn  — expanding hexagon ring with white flash and scattered particles
 *   complete — radial flash with expanding ring
 *   error  — shatter effect with crack lines radiating from center
 *
 * Also manages persistent ambient particles (orbit dots, etc.).
 */

class SparkFX {
    constructor() {
        /** @type {Particle[]} */
        this.particles = [];
        /** @type {Effect[]} */
        this.effects = [];
        this._pool = []; // recycled particle objects
    }

    // ─── Theme-Aware Triggers ────────────────────────────
    // These check the active theme for custom FX before falling back to defaults.

    /** Trigger spawn effect — delegates to theme if it provides one */
    triggerSpawn(x, y, color, radius) {
        const theme = typeof getTheme === 'function' ? getTheme() : null;
        if (theme?.fxSpawn) {
            theme.fxSpawn(this, x, y, color, radius);
        } else {
            this.spawnEffect(x, y, color, radius);
        }
    }

    /** Trigger complete effect — delegates to theme if it provides one */
    triggerComplete(x, y, color, radius) {
        const theme = typeof getTheme === 'function' ? getTheme() : null;
        if (theme?.fxComplete) {
            theme.fxComplete(this, x, y, color, radius);
        } else {
            this.completeEffect(x, y, color, radius);
        }
    }

    /** Trigger error effect — delegates to theme if it provides one */
    triggerError(x, y, color, radius) {
        const theme = typeof getTheme === 'function' ? getTheme() : null;
        if (theme?.fxError) {
            theme.fxError(this, x, y, color, radius);
        } else {
            this.errorEffect(x, y, color, radius);
        }
    }

    // ─── Default Effects ─────────────────────────────────

    /** Default agent spawn effect at world position */
    spawnEffect(x, y, color, radius) {
        // Expanding hexagon ring
        this.effects.push({
            type: 'hexRing',
            x, y, color, radius: radius || 28,
            born: performance.now() / 1000,
            duration: 1.2,
            maxRadius: (radius || 28) * 4,
        });

        // White flash
        this.effects.push({
            type: 'flash',
            x, y, color: '#ffffff',
            born: performance.now() / 1000,
            duration: 0.4,
            maxRadius: (radius || 28) * 3,
        });

        // Scattered particles
        const count = 18;
        for (let i = 0; i < count; i++) {
            const angle = (i / count) * Math.PI * 2 + (Math.random() - 0.5) * 0.3;
            const speed = 60 + Math.random() * 80;
            this._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color,
                size: 1.5 + Math.random() * 2,
                life: 0.8 + Math.random() * 0.6,
                drag: 0.96,
                glow: true,
            });
        }
    }

    /** Trigger agent completion effect */
    completeEffect(x, y, color, radius) {
        // Expanding ring
        this.effects.push({
            type: 'ring',
            x, y, color,
            born: performance.now() / 1000,
            duration: 1.0,
            startRadius: radius || 28,
            maxRadius: (radius || 28) * 5,
        });

        // Radial flash
        this.effects.push({
            type: 'flash',
            x, y, color,
            born: performance.now() / 1000,
            duration: 0.6,
            maxRadius: (radius || 28) * 2.5,
        });

        // Upward celebration particles
        const count = 12;
        for (let i = 0; i < count; i++) {
            const angle = (i / count) * Math.PI * 2;
            const speed = 30 + Math.random() * 50;
            this._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed - 20,
                color,
                size: 1 + Math.random() * 1.5,
                life: 1.0 + Math.random() * 0.5,
                drag: 0.97,
                glow: true,
            });
        }
    }

    /** Trigger error/shatter effect */
    errorEffect(x, y, color, radius) {
        color = color || '#ff5566';

        // Crack lines radiating outward
        const cracks = 6 + Math.floor(Math.random() * 4);
        for (let i = 0; i < cracks; i++) {
            const angle = (i / cracks) * Math.PI * 2 + (Math.random() - 0.5) * 0.5;
            this.effects.push({
                type: 'crack',
                x, y, color,
                angle,
                born: performance.now() / 1000,
                duration: 1.5,
                length: (radius || 28) * 2.5 + Math.random() * 40,
                width: 1 + Math.random() * 1.5,
            });
        }

        // Shatter flash
        this.effects.push({
            type: 'flash',
            x, y, color,
            born: performance.now() / 1000,
            duration: 0.3,
            maxRadius: (radius || 28) * 2,
        });

        // Debris particles
        const count = 14;
        for (let i = 0; i < count; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 40 + Math.random() * 100;
            this._emit(x, y, {
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed,
                color,
                size: 1 + Math.random() * 2.5,
                life: 0.6 + Math.random() * 0.8,
                drag: 0.93,
                glow: false,
                angular: Math.random() * 4 - 2, // rotation speed
            });
        }
    }

    /** Emit a single particle (for edge data flow, etc.) */
    emitParticle(x, y, opts) {
        this._emit(x, y, opts);
    }

    // ─── Update & Render ─────────────────────────────────

    /** Tick all particles and effects. Call once per frame with delta time. */
    update(dt) {
        const now = performance.now() / 1000;

        // Update particles
        for (let i = this.particles.length - 1; i >= 0; i--) {
            const p = this.particles[i];
            p.age += dt;
            if (p.age >= p.life) {
                this._recycle(p);
                this.particles.splice(i, 1);
                continue;
            }
            p.x += p.vx * dt;
            p.y += p.vy * dt;
            p.vx *= p.drag;
            p.vy *= p.drag;
            if (p.angular) p.rotation = (p.rotation || 0) + p.angular * dt;

            // Store trail positions
            if (p.trail) {
                p.trailPoints = p.trailPoints || [];
                p.trailPoints.unshift({ x: p.x, y: p.y });
                if (p.trailPoints.length > 6) p.trailPoints.pop();
            }
        }

        // Expire finished effects (account for delayed born times)
        for (let i = this.effects.length - 1; i >= 0; i--) {
            const elapsed = now - this.effects[i].born;
            if (elapsed > 0 && elapsed > this.effects[i].duration) {
                this.effects.splice(i, 1);
            }
        }
    }

    /** Render all effects and particles onto a canvas context (in world space). */
    render(ctx) {
        const now = performance.now() / 1000;

        // Effects first (behind particles)
        for (const fx of this.effects) {
            const t = Math.max(0, Math.min(1, (now - fx.born) / fx.duration));
            if (t === 0) continue; // not started yet (delayed born)
            const alpha = 1 - t;

            switch (fx.type) {
                case 'flash': {
                    const r = fx.maxRadius * easeOutQuad(t);
                    const grad = ctx.createRadialGradient(fx.x, fx.y, 0, fx.x, fx.y, r);
                    grad.addColorStop(0, fx.color + hexAlpha(alpha * 0.6));
                    grad.addColorStop(0.5, fx.color + hexAlpha(alpha * 0.2));
                    grad.addColorStop(1, fx.color + '00');
                    ctx.fillStyle = grad;
                    ctx.beginPath();
                    ctx.arc(fx.x, fx.y, r, 0, Math.PI * 2);
                    ctx.fill();
                    break;
                }

                case 'ring': {
                    const r = fx.startRadius + (fx.maxRadius - fx.startRadius) * easeOutCubic(t);
                    ctx.strokeStyle = fx.color;
                    ctx.lineWidth = Math.max(0.5, 2 * (1 - t));
                    ctx.globalAlpha = alpha * 0.8;
                    ctx.beginPath();
                    ctx.arc(fx.x, fx.y, r, 0, Math.PI * 2);
                    ctx.stroke();
                    ctx.globalAlpha = 1;
                    break;
                }

                case 'hexRing': {
                    const r = fx.radius + (fx.maxRadius - fx.radius) * easeOutCubic(t);
                    ctx.strokeStyle = fx.color;
                    ctx.lineWidth = Math.max(0.5, 2.5 * (1 - t));
                    ctx.globalAlpha = alpha * 0.9;
                    ctx.beginPath();
                    for (let i = 0; i < 6; i++) {
                        const a = (i / 6) * Math.PI * 2 - Math.PI / 2;
                        const px = fx.x + Math.cos(a) * r;
                        const py = fx.y + Math.sin(a) * r;
                        if (i === 0) ctx.moveTo(px, py);
                        else ctx.lineTo(px, py);
                    }
                    ctx.closePath();
                    ctx.stroke();
                    ctx.globalAlpha = 1;
                    break;
                }

                case 'crack': {
                    const len = fx.length * easeOutQuad(Math.min(1, t * 3)); // fast extend
                    const endX = fx.x + Math.cos(fx.angle) * len;
                    const endY = fx.y + Math.sin(fx.angle) * len;
                    ctx.strokeStyle = fx.color;
                    ctx.lineWidth = fx.width * (1 - t * 0.7);
                    ctx.globalAlpha = alpha;
                    ctx.beginPath();
                    ctx.moveTo(fx.x, fx.y);
                    // Jagged line with random offsets
                    const steps = 4;
                    for (let s = 1; s <= steps; s++) {
                        const frac = s / steps;
                        const mx = fx.x + (endX - fx.x) * frac;
                        const my = fx.y + (endY - fx.y) * frac;
                        const jitter = (1 - frac) * 8;
                        ctx.lineTo(
                            mx + (Math.sin(s * 17.3 + fx.angle * 5) * jitter),
                            my + (Math.cos(s * 13.7 + fx.angle * 7) * jitter)
                        );
                    }
                    ctx.stroke();
                    ctx.globalAlpha = 1;
                    break;
                }
            }
        }

        // Particles
        for (const p of this.particles) {
            const t = p.age / p.life;
            const alpha = 1 - t;

            ctx.save();
            ctx.globalAlpha = alpha;

            // Trail
            if (p.trail && p.trailPoints && p.trailPoints.length > 1) {
                ctx.beginPath();
                ctx.moveTo(p.trailPoints[0].x, p.trailPoints[0].y);
                for (let i = 1; i < p.trailPoints.length; i++) {
                    ctx.lineTo(p.trailPoints[i].x, p.trailPoints[i].y);
                }
                ctx.strokeStyle = p.color;
                ctx.lineWidth = p.size * 0.5 * (1 - t);
                ctx.globalAlpha = alpha * 0.4;
                ctx.stroke();
                ctx.globalAlpha = alpha;
            }

            // Glow
            if (p.glow) {
                const grad = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, p.size * 3);
                grad.addColorStop(0, p.color + hexAlpha(alpha * 0.3));
                grad.addColorStop(1, p.color + '00');
                ctx.fillStyle = grad;
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.size * 3, 0, Math.PI * 2);
                ctx.fill();
            }

            // Particle body
            ctx.translate(p.x, p.y);
            if (p.rotation) ctx.rotate(p.rotation);
            ctx.fillStyle = p.color;
            ctx.beginPath();
            ctx.arc(0, 0, p.size * (1 - t * 0.3), 0, Math.PI * 2);
            ctx.fill();

            ctx.restore();
        }
    }

    // ─── Internal ────────────────────────────────────────

    _emit(x, y, opts) {
        const p = this._pool.pop() || {};
        p.x = x;
        p.y = y;
        p.vx = opts.vx || 0;
        p.vy = opts.vy || 0;
        p.color = opts.color || '#66ccff';
        p.size = opts.size || 2;
        p.life = opts.life || 1;
        p.age = 0;
        p.drag = opts.drag || 0.98;
        p.glow = !!opts.glow;
        p.trail = !!opts.trail;
        p.angular = opts.angular || 0;
        p.rotation = 0;
        p.trailPoints = null;
        p.label = opts.label || null;
        this.particles.push(p);
    }

    _recycle(p) {
        if (this._pool.length < 200) {
            this._pool.push(p);
        }
    }

    /** Clear all active effects and particles */
    clear() {
        this.particles.length = 0;
        this.effects.length = 0;
    }
}

// ─── Edge Particle System ────────────────────────────

/**
 * Manages data-flow particles along cubic bezier edges.
 * Agent-flow aligned: comet trail with individual circles, sinusoidal wobble
 * with per-particle phase offset, pre-cached glow sprites.
 */
class EdgeParticleSystem {
    constructor() {
        /** @type {EdgeParticle[]} */
        this.particles = [];
        this._nextId = 0;
        this._glowCache = new Map();
    }

    /**
     * Spawn a particle along a cubic bezier edge.
     * @param {Object} source - {x, y} source node
     * @param {Object} target - {x, y} target node
     * @param {Object} cp1 - {x, y} first control point
     * @param {Object} cp2 - {x, y} second control point
     * @param {Object} opts - { color, speed, label, size, wobble }
     */
    spawn(source, target, cp1, cp2, opts = {}) {
        this.particles.push({
            id: this._nextId++,
            sx: source.x, sy: source.y,
            tx: target.x, ty: target.y,
            c1x: cp1.x, c1y: cp1.y,
            c2x: cp2.x, c2y: cp2.y,
            t: 0,
            speed: opts.speed || 0.4,
            color: opts.color || '#66ccff',
            size: opts.size || 2.5,
            label: opts.label || null,
            wobbleAmp: opts.wobble || 3,
            wobbleFreq: 10,
            wobbleTimeFreq: 3,
            wobblePhase: (this._nextId * 0.7) % (Math.PI * 2),
        });
    }

    /** Update all edge particles */
    update(dt) {
        for (let i = this.particles.length - 1; i >= 0; i--) {
            const p = this.particles[i];
            p.t += p.speed * 1.2 * dt;
            if (p.t >= 1) {
                this.particles.splice(i, 1);
            }
        }
    }

    /** Render all edge particles (agent-flow style: individual trail circles + glow sprite) */
    render(ctx, time) {
        const trailSegments = 8;
        const trailOffset = 0.15;

        for (const p of this.particles) {
            const t = p.t;

            // Compute edge direction for wobble normal
            const dx = p.tx - p.sx, dy = p.ty - p.sy;
            const dist = Math.sqrt(dx * dx + dy * dy) || 1;
            const normalX = -dy / dist;
            const normalY = dx / dist;

            // Wobble amount — sine wave with per-particle phase, fades at endpoints
            const wobbleAmt = Math.sin(t * p.wobbleFreq + time * p.wobbleTimeFreq + p.wobblePhase)
                * p.wobbleAmp * Math.sin(t * Math.PI);

            const baseX = this._cubicBezierX(p, t);
            const baseY = this._cubicBezierY(p, t);
            const px = baseX + normalX * wobbleAmt;
            const py = baseY + normalY * wobbleAmt;

            ctx.save();

            // Comet trail — individual circles with fading alpha (agent-flow style)
            for (let i = trailSegments; i >= 0; i--) {
                const offset = (i / trailSegments) * trailOffset;
                const tt = Math.max(0, t - offset);
                const wob = Math.sin(tt * p.wobbleFreq + time * p.wobbleTimeFreq + p.wobblePhase)
                    * p.wobbleAmp * Math.sin(tt * Math.PI);
                const tx = this._cubicBezierX(p, tt) + normalX * wob;
                const ty = this._cubicBezierY(p, tt) + normalY * wob;
                const alpha = ((trailSegments - i) / trailSegments) * 0.6;
                ctx.beginPath();
                ctx.fillStyle = p.color + hexAlpha(alpha);
                ctx.arc(tx, ty, p.size * ((trailSegments - i) / trailSegments), 0, Math.PI * 2);
                ctx.fill();
            }

            // Glow (pre-cached sprite)
            const glowR = 15;
            const glowSprite = this._getGlowSprite(p.color, glowR);
            ctx.drawImage(glowSprite, px - glowR, py - glowR);

            // Particle core
            ctx.beginPath();
            ctx.fillStyle = p.color;
            ctx.arc(px, py, p.size, 0, Math.PI * 2);
            ctx.fill();

            // Core highlight
            ctx.beginPath();
            ctx.fillStyle = '#ffffff80';
            ctx.arc(px, py, p.size * 0.4, 0, Math.PI * 2);
            ctx.fill();

            // Label near particle (only mid-journey)
            if (p.label && t > 0.2 && t < 0.8) {
                ctx.fillStyle = p.color + 'aa';
                ctx.font = '8px monospace';
                ctx.textAlign = 'center';
                ctx.fillText(p.label, px, py - 12);
            }

            ctx.restore();
        }
    }

    _cubicBezierX(p, t) {
        const mt = 1 - t;
        return mt*mt*mt*p.sx + 3*mt*mt*t*p.c1x + 3*mt*t*t*p.c2x + t*t*t*p.tx;
    }

    _cubicBezierY(p, t) {
        const mt = 1 - t;
        return mt*mt*mt*p.sy + 3*mt*mt*t*p.c1y + 3*mt*t*t*p.c2y + t*t*t*p.ty;
    }

    _getGlowSprite(color, radius) {
        const key = `${color}_${radius}`;
        if (this._glowCache.has(key)) return this._glowCache.get(key);

        const size = radius * 2;
        const canvas = document.createElement('canvas');
        canvas.width = size;
        canvas.height = size;
        const ctx = canvas.getContext('2d');
        const grad = ctx.createRadialGradient(radius, radius, 0, radius, radius, radius);
        grad.addColorStop(0, color + '60');
        grad.addColorStop(1, color + '00');
        ctx.fillStyle = grad;
        ctx.fillRect(0, 0, size, size);

        this._glowCache.set(key, canvas);
        return canvas;
    }
}

// ─── Message Bubble System ───────────────────────────

class MessageBubbleSystem {
    constructor() {
        /** @type {MessageBubble[]} */
        this.bubbles = [];
        this.maxBubblesPerAgent = 2;
    }

    /**
     * Add a message bubble near an agent.
     * @param {string} agentId
     * @param {string} text
     * @param {'user'|'assistant'|'thinking'} type
     */
    add(agentId, text, type) {
        // Remove old bubbles for this agent if at limit
        const agentBubbles = this.bubbles.filter(b => b.agentId === agentId);
        while (agentBubbles.length >= this.maxBubblesPerAgent) {
            const oldest = agentBubbles.shift();
            oldest.fadeStart = performance.now() / 1000;
            oldest.fadeDuration = 0.3;
        }

        this.bubbles.push({
            agentId,
            text: text.length > 80 ? text.substring(0, 77) + '\u2026' : text,
            type,
            born: performance.now() / 1000,
            fadeStart: null,
            fadeDuration: 0.5,
            life: type === 'thinking' ? 5 : 8,
            offsetY: 0, // computed during render
        });
    }

    update(dt) {
        const now = performance.now() / 1000;
        for (let i = this.bubbles.length - 1; i >= 0; i--) {
            const b = this.bubbles[i];
            const age = now - b.born;

            // Auto-fade after lifetime
            if (!b.fadeStart && age > b.life) {
                b.fadeStart = now;
            }

            // Remove fully faded
            if (b.fadeStart && (now - b.fadeStart) > b.fadeDuration) {
                this.bubbles.splice(i, 1);
            }
        }
    }

    /**
     * Render bubbles near their agent nodes.
     * @param {CanvasRenderingContext2D} ctx
     * @param {Map} agents - agent map
     * @param {ForceSimulation} sim
     */
    render(ctx, agents, sim, time) {
        const now = performance.now() / 1000;

        // Group by agent, assign vertical offsets
        const byAgent = new Map();
        for (const b of this.bubbles) {
            if (!byAgent.has(b.agentId)) byAgent.set(b.agentId, []);
            byAgent.get(b.agentId).push(b);
        }

        for (const [agentId, bubbles] of byAgent) {
            const node = sim.getNode(agentId);
            const agent = agents.get(agentId);
            if (!node || !agent) continue;

            let yOff = -(node.radius + 30);
            for (let i = bubbles.length - 1; i >= 0; i--) {
                const b = bubbles[i];
                const age = now - b.born;

                // Alpha
                let alpha = 1;
                if (age < 0.3) alpha = age / 0.3; // fade in
                if (b.fadeStart) alpha = Math.max(0, 1 - (now - b.fadeStart) / b.fadeDuration);
                if (alpha <= 0) continue;

                const isThinking = b.type === 'thinking';
                const maxWidth = isThinking ? 120 : 160;

                ctx.save();
                ctx.globalAlpha = alpha * (isThinking ? 0.5 : 0.75);
                ctx.font = isThinking ? 'italic 8px monospace' : '8px monospace';

                // Word wrap
                const lines = wrapText(ctx, b.text, maxWidth);
                const lineHeight = 11;
                const padding = 6;
                const boxW = maxWidth + padding * 2;
                const boxH = lines.length * lineHeight + padding * 2;

                const bx = node.x - boxW / 2;
                const by = node.y + yOff - boxH;

                // Background
                const bgColor = b.type === 'user' ? 'rgba(40, 60, 120, 0.7)'
                    : isThinking ? 'rgba(30, 30, 50, 0.5)'
                    : 'rgba(20, 40, 60, 0.7)';
                ctx.fillStyle = bgColor;
                roundRect(ctx, bx, by, boxW, boxH, 5);
                ctx.fill();

                // Border
                const borderColor = b.type === 'user' ? '#4488cc'
                    : isThinking ? '#666688'
                    : '#44aacc';
                ctx.strokeStyle = borderColor;
                ctx.lineWidth = 0.5;
                roundRect(ctx, bx, by, boxW, boxH, 5);
                ctx.stroke();

                // Text
                ctx.fillStyle = isThinking ? '#9999bb' : '#ccddee';
                for (let l = 0; l < lines.length; l++) {
                    ctx.fillText(lines[l], bx + padding, by + padding + 8 + l * lineHeight);
                }

                ctx.restore();

                yOff -= boxH + 4;
            }
        }
    }
}

// ─── Utility Functions ───────────────────────────────

function easeOutQuad(t) { return t * (2 - t); }
function easeOutCubic(t) { return 1 - Math.pow(1 - t, 3); }

function hexAlpha(a) {
    const v = Math.max(0, Math.min(255, Math.round(a * 255)));
    return v.toString(16).padStart(2, '0');
}

function wrapText(ctx, text, maxWidth) {
    const words = text.split(' ');
    const lines = [];
    let current = '';

    for (const word of words) {
        const test = current ? current + ' ' + word : word;
        if (ctx.measureText(test).width > maxWidth && current) {
            lines.push(current);
            current = word;
        } else {
            current = test;
        }
    }
    if (current) lines.push(current);

    // Max 3 lines
    if (lines.length > 3) {
        lines.length = 3;
        lines[2] = lines[2].substring(0, lines[2].length - 1) + '\u2026';
    }

    return lines;
}
