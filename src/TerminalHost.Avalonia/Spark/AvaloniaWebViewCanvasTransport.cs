#if !LINUX
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;
using TerminalHost.Core.Spark;
using WebViewCore.Events;

namespace TerminalHost.AvaloniaSpark;

/// <summary>
/// Avalonia <see cref="ICanvasTransport"/> adapter over <c>AvaloniaWebView.WebView</c>.
/// Mirrors <c>WebView2CanvasTransport</c> in responsibilities — UI-thread marshaling
/// via <see cref="Dispatcher.UIThread"/>, pre-ready queue, ready handshake, and JSON
/// serialization through <see cref="CanvasJsonProtocol"/>.
/// </summary>
public sealed class AvaloniaWebViewCanvasTransport : ICanvasTransport, IDisposable
{
    private readonly global::AvaloniaWebView.WebView _webView;
    private readonly Queue<CanvasOutbound> _preReadyQueue = new();
    private readonly object _gate = new();
    private bool _disposed;

    public AvaloniaWebViewCanvasTransport(global::AvaloniaWebView.WebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    public bool IsReady { get; private set; }

    public event EventHandler<CanvasInbound>? Received;
    public event EventHandler? Ready;

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

    public void Post(Action action)
    {
        if (action == null) return;
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void OnWebMessageReceived(object? sender, WebViewMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.Message;
            if (string.IsNullOrEmpty(json)) return;

            var inbound = CanvasJsonProtocol.TryParse(json);
            if (inbound == null) return;

            Post(() =>
            {
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
            // Ignore malformed inbound messages.
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
                try
                {
                    if (!_disposed)
                    {
                        // PostWebMessageAsString calls __dispatchMessageCallback(msg) in JS,
                        // which is registered by listenForWebViewMessages() in events.js.
                        _webView.PostWebMessageAsString(json, null);
                    }
                }
                catch
                {
                    // WebView may be torn down.
                }
            });
        }
        catch
        {
            // Serialization failure — drop the message.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _webView.WebMessageReceived -= OnWebMessageReceived; } catch { }
    }
}
#endif
