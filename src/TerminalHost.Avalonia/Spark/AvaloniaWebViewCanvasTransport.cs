#if !LINUX
using System;
using Avalonia.Threading;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;
using WebViewCore.Events;

namespace TerminalHost.AvaloniaSpark;

/// <summary>
/// Avalonia <see cref="ICanvasTransport"/> adapter over <c>AvaloniaWebView.WebView</c>. UI-thread
/// marshaling via <see cref="Dispatcher.UIThread"/>; queue, handshake, and JSON handled by the base.
/// </summary>
public sealed class AvaloniaWebViewCanvasTransport : WebViewCanvasTransportBase
{
    private readonly global::AvaloniaWebView.WebView _webView;

    public AvaloniaWebViewCanvasTransport(global::AvaloniaWebView.WebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Marshals onto the Avalonia UI-thread dispatcher.</summary>
    public override void Post(Action action)
    {
        if (action == null) return;
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <inheritdoc />
    protected override void PostOutboundJson(string json)
    {
        try
        {
            // PostWebMessageAsString calls __dispatchMessageCallback(msg) in JS,
            // which is registered by listenForWebViewMessages() in events.js.
            _webView.PostWebMessageAsString(json, null);
        }
        catch
        {
            // WebView may be torn down.
        }
    }

    private void OnWebMessageReceived(object? sender, WebViewMessageReceivedEventArgs e)
        => OnInboundJson(e.Message ?? string.Empty);

    /// <inheritdoc />
    protected override void OnDispose()
    {
        try { _webView.WebMessageReceived -= OnWebMessageReceived; } catch { }
    }
}
#endif
