using System;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Services.Spark;

namespace TerminalHost.Spark;

/// <summary>
/// WPF <see cref="ICanvasTransport"/> adapter over <see cref="CoreWebView2"/>. UI-thread
/// marshaling via <see cref="Dispatcher"/>; queue, handshake, and JSON handled by the base.
/// </summary>
public sealed class WebView2CanvasTransport : WebViewCanvasTransportBase
{
    private readonly CoreWebView2 _webView;
    private readonly Dispatcher _dispatcher;

    public WebView2CanvasTransport(CoreWebView2 webView, Dispatcher dispatcher)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Marshals onto the WPF dispatcher this transport was constructed with.</summary>
    public override void Post(Action action)
    {
        if (action == null) return;
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(action);
    }

    /// <inheritdoc />
    protected override void PostOutboundJson(string json)
    {
        try
        {
            _webView.PostWebMessageAsString(json);
        }
        catch
        {
            // WebView may be torn down.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        => OnInboundJson(e.TryGetWebMessageAsString() ?? string.Empty);

    /// <inheritdoc />
    protected override void OnDispose()
    {
        try { _webView.WebMessageReceived -= OnWebMessageReceived; } catch { }
    }
}
