using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// Shared base for WebView-backed <see cref="ICanvasTransport"/> adapters. Owns the
/// ready-handshake, pre-ready outbound queue, JSON serialization, and disposal flag.
/// Subclasses provide UI-thread marshaling and the platform-specific outbound post.
/// </summary>
public abstract class WebViewCanvasTransportBase : ICanvasTransport, IDisposable
{
    private readonly Queue<CanvasOutbound> _preReadyQueue = new();
    private readonly object _gate = new();
    private volatile bool _disposed;

    /// <summary>True once the canvas has reported ready.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Raised on the UI thread when the canvas posts a message to the host.</summary>
    public event EventHandler<CanvasInbound>? Received;

    /// <summary>Raised on the UI thread once, when the canvas reports ready.</summary>
    public event EventHandler? Ready;

    /// <summary>Sends a message to the canvas, queuing it if the channel is not yet ready.</summary>
    public Task SendAsync(CanvasOutbound message)
    {
        if (_disposed) return Task.CompletedTask;

        lock (_gate)
        {
            if (!IsReady)
            {
                _preReadyQueue.Enqueue(message);
                return Task.CompletedTask;
            }
        }

        PostSerialized(message);
        return Task.CompletedTask;
    }

    /// <summary>Marshals <paramref name="action"/> onto the UI thread the orchestrator runs on.</summary>
    public abstract void Post(Action action);

    /// <summary>Posts a serialized outbound JSON string to the underlying WebView.</summary>
    protected abstract void PostOutboundJson(string json);

    /// <summary>
    /// Handles a raw inbound JSON string from the WebView: parses, marshals to UI thread,
    /// performs the ready handshake or raises <see cref="Received"/>. Malformed input is
    /// silently dropped.
    /// </summary>
    protected void OnInboundJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        var inbound = CanvasJsonProtocol.TryParse(json);
        if (inbound == null) return;

        try
        {
            Post(() =>
            {
                // Suppress callbacks if Dispose ran between scheduling and execution —
                // the orchestrator's teardown contract forbids events after Dispose.
                if (_disposed) return;

                if (inbound is CanvasInbound.Ready)
                {
                    if (IsReady) return;
                    IsReady = true;
                    FlushPreReadyQueue();
                    Ready?.Invoke(this, EventArgs.Empty);
                    return;
                }

                Received?.Invoke(this, inbound);
            });
        }
        catch
        {
            // Dispatcher may be torn down during transport teardown.
        }
    }

    private void FlushPreReadyQueue()
    {
        List<CanvasOutbound> pending;
        lock (_gate)
        {
            if (_preReadyQueue.Count == 0) return;
            pending = new List<CanvasOutbound>(_preReadyQueue);
            _preReadyQueue.Clear();
        }
        foreach (var m in pending)
            PostSerialized(m);
    }

    private void PostSerialized(CanvasOutbound message)
    {
        if (_disposed) return;

        try
        {
            var json = CanvasJsonProtocol.Serialize(message);
            Post(() =>
            {
                if (_disposed) return;
                try
                {
                    PostOutboundJson(json);
                }
                catch
                {
                    // WebView may be torn down.
                }
            });
        }
        catch
        {
            // Serialization failure — drop the message rather than break the channel.
        }
    }

    /// <summary>Idempotently tears down the transport. Subclasses unsubscribe via <see cref="OnDispose"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            OnDispose();
        }
        catch
        {
            // Subclass teardown failures must not propagate.
        }
    }

    /// <summary>Subclass hook for unsubscribing the WebView inbound event. Default is a no-op.</summary>
    protected virtual void OnDispose() { }
}
