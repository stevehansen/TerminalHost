using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Spark;

/// <summary>
/// WPF <see cref="ICanvasTransport"/> adapter over <see cref="CoreWebView2"/>.
/// Owns:
/// <list type="bullet">
///   <item>UI-thread marshaling via <see cref="Dispatcher"/></item>
///   <item>The ready-handshake (consumes the first inbound <c>{"action":"ready"}</c>)</item>
///   <item>The pre-ready outbound queue (flushed on Ready)</item>
///   <item>JSON serialization via <see cref="CanvasJsonProtocol"/></item>
/// </list>
/// </summary>
public sealed class WebView2CanvasTransport : ICanvasTransport, IDisposable
{
    private readonly CoreWebView2 _webView;
    private readonly Dispatcher _dispatcher;
    private readonly Queue<CanvasOutbound> _preReadyQueue = new();
    private readonly object _gate = new();
    private bool _disposed;

    public WebView2CanvasTransport(CoreWebView2 webView, Dispatcher dispatcher)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(action);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
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
                        _webView.PostWebMessageAsString(json);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
        }
        catch { }
    }
}
