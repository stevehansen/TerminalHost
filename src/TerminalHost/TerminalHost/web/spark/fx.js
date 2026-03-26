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
 * Manages data-flow particles along edges.
 * Supports comet trails, sinusoidal wobble, and labeled particles.
 */
class EdgeParticleSystem {
    constructor() {
        /** @type {EdgeParticle[]} */
        this.particles = [];
        this._nextId = 0;
    }

    /**
     * Spawn a particle along a bezier edge.
     * @param {Object} source - {x, y} source node
     * @param {Object} target - {x, y} target node
     * @param {Object} control - {x, y} bezier control point
     * @param {Object} opts - { color, speed, label, size }
     */
    spawn(source, target, control, opts = {}) {
        this.particles.push({
            id: this._nextId++,
            sx: source.x, sy: source.y,
            tx: target.x, ty: target.y,
            cx: control.x, cy: control.y,
            t: 0, // progress 0→1
            speed: opts.speed || 0.4,
            color: opts.color || '#66ccff',
            size: opts.size || 2.5,
            label: opts.label || null,
            wobbleAmp: opts.wobble || 6,
            wobbleFreq: 3 + Math.random() * 2,
            wobblePhase: Math.random() * Math.PI * 2,
            trail: [],
        });
    }

    /** Update all edge particles */
    update(dt) {
        for (let i = this.particles.length - 1; i >= 0; i--) {
            const p = this.particles[i];
            p.t += p.speed * dt;

            // Store trail
            const pos = this._evalBezier(p, p.t);
            p.trail.unshift(pos);
            if (p.trail.length > 8) p.trail.pop();

            if (p.t >= 1) {
                this.particles.splice(i, 1);
            }
        }
    }

    /** Render all edge particles */
    render(ctx, time) {
        for (const p of this.particles) {
            const pos = this._evalBezier(p, p.t);

            // Wobble perpendicular to edge direction
            const tangent = this._evalBezierTangent(p, p.t);
            const len = Math.sqrt(tangent.x * tangent.x + tangent.y * tangent.y) || 1;
            const nx = -tangent.y / len;
            const ny = tangent.x / len;
            const wobble = Math.sin(time * p.wobbleFreq + p.wobblePhase) * p.wobbleAmp * (1 - p.t);
            const wx = pos.x + nx * wobble;
            const wy = pos.y + ny * wobble;

            ctx.save();

            // Comet trail
            if (p.trail.length > 1) {
                ctx.beginPath();
                ctx.moveTo(wx, wy);
                for (let i = 0; i < p.trail.length; i++) {
                    const tp = p.trail[i];
                    ctx.lineTo(tp.x, tp.y);
                }
                ctx.strokeStyle = p.color;
                ctx.lineWidth = p.size * 0.6;
                ctx.globalAlpha = 0.3;
                ctx.stroke();
            }

            // Main particle with glow
            ctx.globalAlpha = 0.9;
            const grad = ctx.createRadialGradient(wx, wy, 0, wx, wy, p.size * 2.5);
            grad.addColorStop(0, p.color + 'cc');
            grad.addColorStop(0.5, p.color + '44');
            grad.addColorStop(1, p.color + '00');
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.arc(wx, wy, p.size * 2.5, 0, Math.PI * 2);
            ctx.fill();

            // Core
            ctx.fillStyle = '#ffffff';
            ctx.globalAlpha = 0.9;
            ctx.beginPath();
            ctx.arc(wx, wy, p.size * 0.6, 0, Math.PI * 2);
            ctx.fill();

            // Label
            if (p.label) {
                ctx.fillStyle = p.color;
                ctx.globalAlpha = 0.6;
                ctx.font = '7px monospace';
                ctx.textAlign = 'center';
                ctx.fillText(p.label, wx, wy - p.size * 3);
            }

            ctx.restore();
        }
    }

    _evalBezier(p, t) {
        const inv = 1 - t;
        return {
            x: inv * inv * p.sx + 2 * inv * t * p.cx + t * t * p.tx,
            y: inv * inv * p.sy + 2 * inv * t * p.cy + t * t * p.ty,
        };
    }

    _evalBezierTangent(p, t) {
        const inv = 1 - t;
        return {
            x: 2 * inv * (p.cx - p.sx) + 2 * t * (p.tx - p.cx),
            y: 2 * inv * (p.cy - p.sy) + 2 * t * (p.ty - p.cy),
        };
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
