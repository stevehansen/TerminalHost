using System;
using System.Threading.Tasks;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// Headless / Linux <see cref="ICanvasTransport"/>: <see cref="SendAsync"/> is a no-op,
/// <see cref="Received"/> and <see cref="Ready"/> never fire. <see cref="IsReady"/> is true.
/// </summary>
/// <remarks>
/// Contract: <see cref="IsReady"/> is intentionally <c>true</c> from construction.
/// When attached, the orchestrator's <c>Attach</c> path will synthesize an immediate
/// <c>Ready</c> call, which runs the ready-handshake against this no-op transport.
/// The handshake sends are dropped (harmless), and any state mutation it triggers
/// (e.g. auto-connect to the first available session) happens silently — which is
/// the desired behavior on Linux, where the canvas runs in an external browser and
/// events flow via the SSE/REST path rather than this transport. S9.
/// </remarks>
public sealed class NullCanvasTransport : ICanvasTransport
{
    public Task SendAsync(CanvasOutbound message) => Task.CompletedTask;

    public void Post(Action action) => action();

    public event EventHandler<CanvasInbound>? Received { add { } remove { } }
    public event EventHandler? Ready { add { } remove { } }

    public bool IsReady => true;
}
