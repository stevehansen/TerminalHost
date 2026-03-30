# PRD: Collaboration Sync Bridge

> **Status**: Draft — design exploration, not yet implemented
> **Depends on**: Collab MCP persistence (completed)
> **Last updated**: 2026-03-30

## Problem Statement

The Collaboration MCP currently operates within a single TerminalHost instance. All topics, messages, and sessions live in one process on one machine. Developers who work across multiple devices (desktop + laptop, home + office, local + cloud VM) cannot coordinate Claude Code sessions across those machines.

## Goals

1. **Cross-device collaboration** — Claude Code sessions on Machine A can exchange messages with sessions on Machine B through the existing collab topic/message API (no MCP tool changes needed).
2. **Clean separation** — Sync traffic runs on a dedicated bridge process/port, completely isolated from the local API server. The local API remains localhost-only, no auth required.
3. **Security by default** — No port is exposed to the network unless the user explicitly enables it. When enabled, all sync traffic is authenticated and encrypted.
4. **Flexible transport** — Support multiple connectivity models (LAN, VPN, tunnel, relay) without baking in assumptions about network topology.
5. **Graceful degradation** — Network partitions don't crash anything. Messages queue locally and catch up on reconnect.

## Non-Goals

- Real-time terminal sharing or screen mirroring
- File synchronization between devices
- Supporting more than ~10 linked devices (this is a developer tool, not enterprise infrastructure)
- Replacing git as the source of truth for code

---

## Architecture Overview

```
Machine A                                          Machine B
+-------------------+                              +-------------------+
| TerminalHost      |                              | TerminalHost      |
|                   |                              |                   |
| CollabService ----+---> SyncBridge (port 19280)  | CollabService <---+--- SyncBridge (port 19280)
| (localhost:19277) |         |                    | (localhost:19277) |         |
+-------------------+         |                    +-------------------+         |
                              v                                                  v
                    +---------+--------------------------------------------------+---------+
                    |                     Transport Layer                                   |
                    |  Option A: Direct (LAN/VPN)    peer-to-peer HTTP+SSE                 |
                    |  Option B: Tunnel (Cloudflare/ngrok)   tunneled HTTP+SSE             |
                    |  Option C: Relay service        relay-mediated, E2E encrypted        |
                    +----------------------------------------------------------------------+
```

**SyncBridge** is a separate component (could be an in-process service on a different port, or a standalone executable) that:
1. Subscribes to local `CollabService` state changes
2. Serializes them as `SyncEvent` envelopes
3. Sends them to connected peers via the chosen transport
4. Receives remote events and injects them into local `CollabService`

---

## Connectivity Models

### Option A: Direct Connection (LAN / VPN / Tailscale)

**How it works**: Each SyncBridge listens on a TCP port (default 19280). Peers connect directly by IP:port. Works on LAN out of the box, or over VPN/Tailscale/WireGuard for cross-network.

**Pros**:
- Simplest to implement
- Lowest latency (direct peer-to-peer)
- No external dependencies
- Works with any VPN/overlay network (Tailscale, WireGuard, ZeroTier)

**Cons**:
- Requires network reachability (firewall rules, port forwarding for WAN)
- NAT traversal is unsolved (won't work across NATs without VPN)
- User must know peer's IP address (or use LAN discovery)

**Security**: TLS with mutual authentication via pre-shared pairing key. Pairing flow generates a shared secret that derives TLS certificates or HMAC keys.

**Discovery**:
- Manual: enter `ip:port` in settings
- LAN: UDP broadcast beacon on port 19281 (opt-in)
- VPN: Tailscale/WireGuard IPs are stable, user enters once

**Best for**: Same LAN, or users already on a VPN/Tailscale network.

---

### Option B: Tunnel (Cloudflare Tunnel / ngrok / bore)

**How it works**: Each SyncBridge gets a public URL via a tunnel service. Peers connect to each other's tunnel URLs. The tunnel terminates TLS and forwards to the local SyncBridge port.

**Pros**:
- Works across NATs and firewalls without port forwarding
- Tunnel services handle TLS termination
- Stable URLs (Cloudflare Tunnel can use custom subdomains)
- No relay server to operate

**Cons**:
- Requires external tool installed (`cloudflared`, `ngrok`, `bore`)
- Free tiers have limitations (ngrok: bandwidth, session limits)
- Latency: traffic goes through tunnel provider's network
- Trust: tunnel provider can see traffic (unless E2E encrypted on top)
- Setup complexity for non-technical users

**Security**: Tunnel provides TLS. Add E2E encryption (pre-shared key) on top to protect against tunnel provider inspection. Alternatively, Cloudflare Access can add identity verification.

**Discovery**: Exchange tunnel URLs via QR code, clipboard, or manual entry.

**Best for**: Users who already use Cloudflare/ngrok, or need quick cross-network connectivity without VPN setup.

---

### Option C: Relay Service (Trusted Distributed Service)

**How it works**: A hosted relay server (operated by user, self-hosted, or provided as a service) acts as a message broker. Both devices connect outbound to the relay. The relay routes messages between paired devices.

```
Machine A ---> wss://relay.example.com/room/abc123 <--- Machine B
```

**Pros**:
- Works everywhere (all connections are outbound HTTPS/WSS)
- No NAT/firewall issues
- No tunnel tool installation needed
- Can support offline queuing (relay holds messages until peer reconnects)
- Clean UX: just enter a room code or scan QR

**Cons**:
- Requires operating/trusting a relay server
- Single point of failure (if relay is down, sync stops)
- Latency: all traffic routes through relay
- Cost: hosting the relay (though lightweight for small scale)
- Privacy: relay operator can see metadata (who connects when); content if not E2E encrypted

**Security**: E2E encryption is mandatory. Peers generate a shared key during pairing. Relay only sees encrypted blobs. Even a compromised relay cannot read message content.

**Relay implementation options**:
- **Self-hosted**: Simple WebSocket relay (~100 lines of code), run on a VPS, Raspberry Pi, or home server
- **Cloudflare Workers / Durable Objects**: Serverless, scales to zero, ~$0 for low usage
- **Firebase Realtime Database / Supabase**: Managed, real-time capable, free tier sufficient
- **Custom service**: Could eventually be an Anthropic-hosted or community-hosted relay

**Best for**: Cross-internet usage, users who don't want to manage VPN/tunnel infrastructure.

---

### Option D: Hybrid (Recommended Default)

**How it works**: SyncBridge supports all transports. Default configuration:
1. **LAN**: Auto-discover peers on local network (UDP broadcast, opt-in)
2. **Direct**: Connect by IP:port for VPN/Tailscale setups
3. **Relay**: Fall back to relay for internet connectivity

The transport is per-peer configurable. One peer might be on LAN (direct), another on a relay.

**Implementation**: Transport is abstracted behind an `ISyncTransport` interface:

```csharp
public interface ISyncTransport : IDisposable
{
    Task ConnectAsync(string peerId, CancellationToken ct);
    Task SendAsync(SyncEvent evt, CancellationToken ct);
    IAsyncEnumerable<SyncEvent> ReceiveAsync(CancellationToken ct);
    event Action<SyncTransportStatus>? StatusChanged;
}

// Implementations:
// - DirectSyncTransport (HTTP POST + SSE, or WebSocket)
// - RelaySyncTransport (WebSocket to relay server)
// - TunnelSyncTransport (wraps Direct with tunnel URL)
```

---

## Sync Protocol

Regardless of transport, the sync protocol is the same:

### Event Types

| Event | Payload | Direction |
|-------|---------|-----------|
| `handshake` | DeviceIdentity, protocol version | Bidirectional on connect |
| `snapshot_request` | `since_sequence` | Requester -> Provider |
| `snapshot` | Full state (topics + messages + counter) | Provider -> Requester |
| `message.send` | topic, sender, content, createdAt | Originator -> Peers |
| `topic.subscribe` | session, topic, description | Originator -> Peers |
| `topic.unsubscribe` | session, topic | Originator -> Peers |
| `session.ensure` | name, workingDir, projectName | Originator -> Peers |
| `heartbeat` | timestamp | Bidirectional, every 30s |

### Envelope

```json
{
  "seq": 42,
  "originDeviceId": "a1b2c3d4",
  "type": "message.send",
  "payload": { "topic": "work", "sender": "backend", "content": "API ready" },
  "timestamp": "2026-03-30T12:00:00Z"
}
```

### Sync Flow

1. **Connect**: Exchange `handshake` with device identity and last-known sequence per peer
2. **Catchup**: If peer has events we missed, they replay them (or send full snapshot if gap too large)
3. **Streaming**: After catchup, events flow in real-time
4. **Dedup**: Each device tracks `(originDeviceId, seq)` pairs to ignore duplicates
5. **Partition**: On disconnect, events queue locally. On reconnect, catchup replays missed events.

### Session Identity

Remote sessions are prefixed: `deviceName:sessionName` (e.g., `laptop:backend`). This is transparent to MCP tools -- the prefix appears in message senders and subscriber lists. Local sessions remain unprefixed.

---

## Security Design

### Pairing

1. Device A generates a 6-character alphanumeric code (displayed in Settings UI)
2. Device B enters the code + Device A's address (or relay room)
3. Both derive a shared secret: `HKDF(pairing_code, deviceId_A || deviceId_B, "terminalhost-sync-v1")`
4. Shared secret is stored in config (encrypted at rest if OS keychain is available)
5. All subsequent communication is authenticated with this key

### Transport Security

| Transport | Encryption | Auth |
|-----------|-----------|------|
| Direct (LAN/VPN) | TLS or HMAC-signed payloads | Shared secret from pairing |
| Tunnel | Tunnel TLS + E2E encryption layer | Shared secret from pairing |
| Relay | WSS (relay TLS) + E2E encryption layer | Shared secret from pairing |

**E2E encryption**: For relay/tunnel modes, each sync event payload is encrypted with AES-256-GCM using a key derived from the pairing secret. The relay/tunnel only sees ciphertext.

---

## Component Design

### New Project: `TerminalHost.SyncBridge` (or in-process service)

**Option 1 — Separate executable** (like `TerminalHost.Channel`):
- Pro: Clean process isolation, can be started/stopped independently, clear security boundary
- Con: Extra process to manage, IPC needed to CollabService

**Option 2 — In-process service on separate port**:
- Pro: Direct access to CollabService (no IPC), simpler lifecycle
- Con: Shares process/memory with main app, one port configuration

**Recommendation**: Start with Option 2 (in-process, separate port). Migrate to separate process later if needed. The `ISyncTransport` abstraction makes this a clean swap.

### Key Classes

```
src/TerminalHost.Core/
  Domain/
    SyncModels.cs           — SyncEvent, DeviceIdentity, PeerConfig, SyncState
    SyncSettings.cs         — Settings model (enabled, deviceId, deviceName, peers, transport)
  Interfaces/
    ISyncBridge.cs          — Start/Stop, peer management, transport selection
    ISyncTransport.cs       — Transport abstraction
  Services/
    SyncBridge.cs           — Orchestrator: event capture, dispatch, injection
    DirectSyncTransport.cs  — HTTP+SSE or WebSocket peer-to-peer
    RelaySyncTransport.cs   — WebSocket to relay server (Phase 2)
    CollabService.cs        — Add internal injection methods for remote events
```

### CollabService Changes (Minimal)

Add internal methods for the SyncBridge to inject remote events without triggering re-replication:

```csharp
// Called by SyncBridge to inject events from remote devices
internal void InjectRemoteMessage(string sender, string topic, string content, DateTime createdAt);
internal void InjectRemoteSubscription(string session, string topic, string? description);
internal void InjectRemoteSession(string name, string? workingDir);
```

These use the same lock and waiter-notification paths but are marked to avoid re-broadcasting.

---

## Settings UI

New section in Settings (Ctrl+,): **"Sync Bridge"**

```
[x] Enable Sync Bridge
    Port: [19280]
    Device Name: [Steve's Desktop]

Peers:
  +---------------------------------------------+--------+-----------+
  | Name              | Address / Relay          | Status | Actions   |
  +---------------------------------------------+--------+-----------+
  | Steve's Laptop    | 192.168.1.42:19280       | Online | [Remove]  |
  | Cloud VM          | relay:room-abc123        | Offline| [Remove]  |
  +---------------------------------------------+--------+-----------+

  [+ Add Peer]  [Show Pairing Code]

Transport: ( ) Direct only  ( ) Relay only  (x) Auto (direct on LAN, relay otherwise)
Relay URL: [wss://relay.example.com]  (only if relay enabled)
```

---

## Phased Implementation

### Phase 1: Core Plumbing + Direct Transport
- `SyncModels.cs`, `SyncSettings.cs` domain models
- `ISyncBridge`, `ISyncTransport` interfaces
- `SyncBridge` service (event capture from CollabService, dispatch, injection)
- `DirectSyncTransport` (HTTP POST + SSE on port 19280)
- `CollabService` internal injection methods
- Pairing flow (shared secret)
- Settings UI: enable, device name, add/remove peer by IP:port
- **Result**: Two TerminalHost instances on same LAN/VPN can sync

### Phase 2: Relay Transport
- `RelaySyncTransport` (WebSocket client to relay server)
- E2E encryption layer (AES-256-GCM with pairing-derived key)
- Simple relay server reference implementation (standalone, or Cloudflare Worker)
- Settings UI: relay URL, per-peer transport selection
- **Result**: Cross-internet sync via relay

### Phase 3: Discovery + Polish
- UDP broadcast LAN discovery (opt-in)
- QR code pairing (display + scan via clipboard)
- Connection health monitoring + auto-reconnect metrics
- Toast notifications for peer connect/disconnect
- Peer status in system tray menu
- **Result**: Polished UX for device linking

### Phase 4: Advanced (Future)
- Tunnel integration (detect `cloudflared`/`ngrok`, auto-configure)
- Selective topic sync (choose which topics to share)
- Conflict resolution for edge cases (concurrent topic deletion)
- Separate executable option for process isolation

---

## Open Questions

1. **Relay hosting**: Should we provide a reference relay implementation? If so, what platform? (Cloudflare Workers is appealing for zero-cost at low scale)
2. **Key storage**: Use OS keychain (Windows Credential Manager / macOS Keychain) for pairing secrets, or just store in config JSON?
3. **Scope of sync**: Sync everything (all topics) by default, or let users choose? Per-topic sync adds complexity but gives control.
4. **Session name collisions**: If both devices have a session named "backend", the prefix (`desktop:backend`, `laptop:backend`) disambiguates. But should we warn the user?
5. **Message ordering**: With multiple devices, messages may arrive out of order. Use local timestamps and display in arrival order, or try to reconstruct global order from timestamps?

---

*Document version: 1.0 — Initial draft*
