/**
 * Force-directed graph layout simulation.
 * Pure JS implementation (no D3 dependency) for the agent flow canvas.
 */

class ForceSimulation {
    constructor() {
        this.nodes = [];      // { id, x, y, vx, vy, fx, fy, radius, pinned, groupId? }
        this.edges = [];      // { source, target, distance, strength }
        this.alpha = 1.0;
        this.alphaDecay = 0.02;
        this.alphaMin = 0.001;
        this.velocityDecay = 0.4;

        // Force parameters
        this.repulsionStrength = -1200;
        this.centerStrength = 0.03;
        this.collisionRadius = 140;
        this.defaultLinkDistance = 350;
        this.defaultLinkStrength = 0.4;

        // Center of simulation
        this.centerX = 0;
        this.centerY = 0;

        // Cluster support (multi-session)
        this.groups = new Map();       // groupId -> { cx, cy, nodeCount }
        this.clusterStrength = 0.06;   // How strongly nodes pull toward group center
        this.clusterSpacing = 350;     // Desired spacing between cluster centers
    }

    setCenter(x, y) {
        this.centerX = x;
        this.centerY = y;
    }

    addNode(node) {
        if (!node.x) node.x = this.centerX + (Math.random() - 0.5) * 100;
        if (!node.y) node.y = this.centerY + (Math.random() - 0.5) * 100;
        node.vx = 0;
        node.vy = 0;
        node.radius = node.radius || 28;
        this.nodes.push(node);
        this.alpha = 1.0; // Reheat
        return node;
    }

    removeNode(id) {
        this.nodes = this.nodes.filter(n => n.id !== id);
        this.edges = this.edges.filter(e => e.source !== id && e.target !== id);
    }

    addEdge(sourceId, targetId, distance, strength) {
        this.edges.push({
            source: sourceId,
            target: targetId,
            distance: distance || this.defaultLinkDistance,
            strength: strength || this.defaultLinkStrength
        });
    }

    removeEdge(sourceId, targetId) {
        this.edges = this.edges.filter(e => !(e.source === sourceId && e.target === targetId));
    }

    getNode(id) {
        return this.nodes.find(n => n.id === id);
    }

    /** Register a group for cluster layout */
    setGroup(groupId, cx, cy) {
        this.groups.set(groupId, { cx, cy, nodeCount: 0 });
    }

    removeGroup(groupId) {
        this.groups.delete(groupId);
        for (const node of this.nodes) {
            if (node.groupId === groupId) node.groupId = null;
        }
    }

    /** Arrange group centers in a ring pattern */
    arrangeGroups() {
        const ids = [...this.groups.keys()];
        const n = ids.length;
        if (n === 0) return;
        if (n === 1) {
            this.groups.get(ids[0]).cx = this.centerX;
            this.groups.get(ids[0]).cy = this.centerY;
            return;
        }
        // For 2 sessions: place closer. For more: ring layout.
        const radius = n === 2
            ? this.clusterSpacing * 0.5
            : this.clusterSpacing * Math.max(0.6, n / (2 * Math.PI));
        for (let i = 0; i < n; i++) {
            const angle = (i / n) * Math.PI * 2 - Math.PI / 2;
            const g = this.groups.get(ids[i]);
            g.cx = this.centerX + Math.cos(angle) * radius;
            g.cy = this.centerY + Math.sin(angle) * radius;
        }
    }

    /** Get bounding box for nodes in a specific group */
    getGroupBounds(groupId, padding = 80) {
        const groupNodes = this.nodes.filter(n => n.groupId === groupId);
        if (groupNodes.length === 0) {
            const g = this.groups.get(groupId);
            // Compact empty/single-agent groups
            return g ? { x: g.cx - 60, y: g.cy - 60, width: 120, height: 120 } : null;
        }
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const node of groupNodes) {
            minX = Math.min(minX, node.x - node.radius);
            minY = Math.min(minY, node.y - node.radius);
            maxX = Math.max(maxX, node.x + node.radius);
            maxY = Math.max(maxY, node.y + node.radius);
        }
        return {
            x: minX - padding,
            y: minY - padding,
            width: (maxX - minX) + padding * 2,
            height: (maxY - minY) + padding * 2
        };
    }

    /** Get the "effective radius" of a group for dynamic spacing */
    getGroupRadius(groupId) {
        const bounds = this.getGroupBounds(groupId, 20);
        if (!bounds) return 60;
        return Math.max(bounds.width, bounds.height) / 2;
    }

    tick() {
        if (this.alpha < this.alphaMin) return;

        // Apply forces
        this._applyRepulsion();
        this._applyLinks();
        if (this.groups.size > 0) {
            this._applyCluster();
        } else {
            this._applyCenter();
        }
        this._applyCollision();

        // Update positions
        for (const node of this.nodes) {
            if (node.pinned) {
                node.vx = 0;
                node.vy = 0;
                if (node.fx != null) node.x = node.fx;
                if (node.fy != null) node.y = node.fy;
                continue;
            }

            node.vx *= (1 - this.velocityDecay);
            node.vy *= (1 - this.velocityDecay);
            node.x += node.vx;
            node.y += node.vy;
        }

        this.alpha += (0 - this.alpha) * this.alphaDecay;
    }

    /** Run N ticks immediately (for initial stabilization) */
    stabilize(ticks = 100) {
        this.alpha = 1.0;
        for (let i = 0; i < ticks; i++) {
            this.tick();
        }
    }

    reheat() {
        this.alpha = 1.0;
    }

    _applyRepulsion() {
        const nodes = this.nodes;
        const n = nodes.length;
        for (let i = 0; i < n; i++) {
            for (let j = i + 1; j < n; j++) {
                const a = nodes[i], b = nodes[j];
                let dx = b.x - a.x;
                let dy = b.y - a.y;
                let dist = Math.sqrt(dx * dx + dy * dy) || 1;
                let force = this.repulsionStrength / (dist * dist);

                let fx = (dx / dist) * force;
                let fy = (dy / dist) * force;

                a.vx -= fx;
                a.vy -= fy;
                b.vx += fx;
                b.vy += fy;
            }
        }
    }

    _applyLinks() {
        for (const edge of this.edges) {
            const source = this.getNode(edge.source);
            const target = this.getNode(edge.target);
            if (!source || !target) continue;

            let dx = target.x - source.x;
            let dy = target.y - source.y;
            let dist = Math.sqrt(dx * dx + dy * dy) || 1;
            let force = (dist - edge.distance) * edge.strength;

            let fx = (dx / dist) * force;
            let fy = (dy / dist) * force;

            source.vx += fx;
            source.vy += fy;
            target.vx -= fx;
            target.vy -= fy;
        }
    }

    _applyCenter() {
        for (const node of this.nodes) {
            node.vx += (this.centerX - node.x) * this.centerStrength;
            node.vy += (this.centerY - node.y) * this.centerStrength;
        }
    }

    _applyCluster() {
        // Attract nodes to their group center; ungrouped nodes pull to sim center
        for (const node of this.nodes) {
            if (node.groupId && this.groups.has(node.groupId)) {
                const g = this.groups.get(node.groupId);
                node.vx += (g.cx - node.x) * this.clusterStrength;
                node.vy += (g.cy - node.y) * this.clusterStrength;
            } else {
                node.vx += (this.centerX - node.x) * this.centerStrength;
                node.vy += (this.centerY - node.y) * this.centerStrength;
            }
        }

        // Repel group centers from each other — dynamic spacing based on content size
        // Groups with more nodes/tools need more space; empty groups can be close
        const ids = [...this.groups.keys()];
        for (let i = 0; i < ids.length; i++) {
            for (let j = i + 1; j < ids.length; j++) {
                const a = this.groups.get(ids[i]);
                const b = this.groups.get(ids[j]);
                let dx = b.cx - a.cx;
                let dy = b.cy - a.cy;
                let dist = Math.sqrt(dx * dx + dy * dy) || 1;

                // Dynamic minimum distance: sum of group radii + small gap
                const rA = this.getGroupRadius(ids[i]);
                const rB = this.getGroupRadius(ids[j]);
                const minSpacing = rA + rB + 30; // 30px gap between boundaries

                if (dist < minSpacing) {
                    const push = (minSpacing - dist) * 0.008;
                    const nx = dx / dist;
                    const ny = dy / dist;
                    a.cx -= nx * push;
                    a.cy -= ny * push;
                    b.cx += nx * push;
                    b.cy += ny * push;
                }
            }
        }
    }

    _applyCollision() {
        const nodes = this.nodes;
        const n = nodes.length;
        for (let i = 0; i < n; i++) {
            for (let j = i + 1; j < n; j++) {
                const a = nodes[i], b = nodes[j];
                let dx = b.x - a.x;
                let dy = b.y - a.y;
                let dist = Math.sqrt(dx * dx + dy * dy) || 1;
                let minDist = this.collisionRadius;

                if (dist < minDist) {
                    let overlap = (minDist - dist) * 0.5;
                    let nx = dx / dist;
                    let ny = dy / dist;
                    a.x -= nx * overlap;
                    a.y -= ny * overlap;
                    b.x += nx * overlap;
                    b.y += ny * overlap;
                }
            }
        }
    }

    /** Get bounding box of all nodes with padding */
    getBounds(padding = 100) {
        if (this.nodes.length === 0) {
            return { x: -200, y: -200, width: 400, height: 400 };
        }
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const node of this.nodes) {
            minX = Math.min(minX, node.x - node.radius);
            minY = Math.min(minY, node.y - node.radius);
            maxX = Math.max(maxX, node.x + node.radius);
            maxY = Math.max(maxY, node.y + node.radius);
        }
        return {
            x: minX - padding,
            y: minY - padding,
            width: (maxX - minX) + padding * 2,
            height: (maxY - minY) + padding * 2
        };
    }
}
