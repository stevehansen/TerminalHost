using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// In-memory <see cref="ICanvasTransport"/>: records outbound messages, exposes
/// <see cref="Inject(CanvasInbound)"/> for inbound, and <see cref="MarkReady"/>
/// to trip the ready handshake. Deterministic — synchronous. Used by boundary
/// tests to exercise the orchestrator without WebView2 or Avalonia.
/// </summary>
public sealed class InMemoryCanvasTransport : ICanvasTransport
{
    private readonly List<CanvasOutbound> _sent = new();
    private readonly Queue<CanvasOutbound> _preReadyQueue = new();

    /// <summary>All outbound messages observed since construction, in order.</summary>
    public IReadOnlyList<CanvasOutbound> Sent => _sent;

    public bool IsReady { get; private set; }

    public event EventHandler<CanvasInbound>? Received;
    public event EventHandler? Ready;

    public Task SendAsync(CanvasOutbound message)
    {
        if (!IsReady)
        {
            _preReadyQueue.Enqueue(message);
            return Task.CompletedTask;
        }

        _sent.Add(message);
        return Task.CompletedTask;
    }

    public void Post(Action action) => action();

    /// <summary>Trips the ready handshake. Flushes any queued pre-ready messages.</summary>
    public void MarkReady()
    {
        if (IsReady) return;
        IsReady = true;

        while (_preReadyQueue.Count > 0)
            _sent.Add(_preReadyQueue.Dequeue());

        Ready?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates the canvas posting a message to the host.</summary>
    public void Inject(CanvasInbound message)
    {
        if (message is CanvasInbound.Ready)
        {
            MarkReady();
            return;
        }
        Received?.Invoke(this, message);
    }

    /// <summary>Clears the recorded outbound list. Useful between test phases.</summary>
    public void ClearSent() => _sent.Clear();
}
