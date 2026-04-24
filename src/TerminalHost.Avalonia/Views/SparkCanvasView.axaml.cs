using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TerminalHost.ViewModels;
#if !LINUX
using WebViewCore.Events;
#endif

namespace TerminalHost.Views;

/// <summary>
/// Hosts the Spark Canvas visualization.
/// On macOS: embedded WebView via WebView.Avalonia.Cross.
/// On Linux: serves web assets on localhost and opens the default browser.
/// </summary>
public partial class SparkCanvasView : UserControl
{
    private bool _isWebViewReady;
    private SparkCanvasViewModel? _viewModel;
#if LINUX
    private HttpListener? _httpListener;
    private string? _browserUrl;
    private CancellationTokenSource? _serverCts;
#else
    private AvaloniaWebView.WebView? _webView;
#endif

    public SparkCanvasView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;
        }

        _viewModel = DataContext as SparkCanvasViewModel;

        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas += OnSendMessageToCanvas;
            _viewModel.RequestOpenJsonlFile += OnRequestOpenJsonlFile;

            // Check if a JSONL open was requested before the view was ready
            if (_viewModel.HasPendingJsonlOpen)
                Dispatcher.UIThread.Post(() => _viewModel.OpenJsonlFileCommand.Execute(null));
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_isWebViewReady)
        {
#if LINUX
            StartBrowserMode();
#else
            StartWebViewMode();
#endif
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;
        }
#if LINUX
        StopHttpServer();
#endif
    }

#if !LINUX
    private void StartWebViewMode()
    {
        _webView = new AvaloniaWebView.WebView();
        _webView.NavigationCompleted += SparkWebView_NavigationCompleted;
        _webView.WebMessageReceived += SparkWebView_WebMessageReceived;
        WebViewHost.Content = _webView;

        var webAssetsPath = GetWebAssetsPath();
        var indexPath = Path.Combine(webAssetsPath, "index.html");

        if (File.Exists(indexPath))
        {
            var query = BuildQueryString();
            _webView.Url = new Uri($"file://{indexPath}{query}");
        }
    }

    private void SparkWebView_NavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        _isWebViewReady = true;
        LoadingOverlay.IsVisible = false;
    }

    private void SparkWebView_WebMessageReceived(object? sender, WebViewMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.Message;
            if (string.IsNullOrEmpty(message)) return;

            if (message.Contains("\"action\":\"ready\""))
            {
                _viewModel?.OnCanvasReady();
                return;
            }

            if (message.Contains("\"action\":\"selectSession\""))
            {
                _viewModel?.OnCanvasMessage(message);
                return;
            }

            if (message.Contains("\"action\":\"refreshSessions\""))
            {
                _viewModel?.RefreshSessionsCommand.Execute(null);
                return;
            }

            if (message.Contains("\"action\":\"themeChanged\""))
            {
                _viewModel?.OnCanvasMessage(message);
                return;
            }

            _viewModel?.OnCanvasMessage(message);
        }
        catch
        {
            // Ignore parse errors
        }
    }
#endif

#if LINUX
    private void StartBrowserMode()
    {
        var webAssetsPath = GetWebAssetsPath();
        if (!Directory.Exists(webAssetsPath))
        {
            LoadingOverlay.IsVisible = false;
            BrowserFallback.IsVisible = true;
            BrowserUrlText.Text = "Web assets not found";
            return;
        }

        // Find a free port and start serving
        var listener = new HttpListener();
        var port = FindFreePort();
        var prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch
        {
            LoadingOverlay.IsVisible = false;
            BrowserFallback.IsVisible = true;
            BrowserUrlText.Text = "Failed to start local server";
            return;
        }

        _httpListener = listener;
        _serverCts = new CancellationTokenSource();

        var query = BuildQueryString();
        _browserUrl = $"http://localhost:{port}/index.html{query}";

        // Serve files in background
        var cts = _serverCts;
        System.Threading.Tasks.Task.Run(() => ServeFiles(listener, webAssetsPath, cts.Token));

        // Open browser
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", _browserUrl) { UseShellExecute = false });
        }
        catch
        {
            // xdg-open not available
        }

        _isWebViewReady = true;
        LoadingOverlay.IsVisible = false;
        BrowserFallback.IsVisible = true;
        BrowserUrlText.Text = _browserUrl;

        // Notify ViewModel that canvas is ready (events flow via SSE, not postMessage)
        _viewModel?.OnCanvasReady();
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async void ServeFiles(HttpListener listener, string webRoot, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(ct);
                var requestPath = context.Request.Url?.AbsolutePath?.TrimStart('/') ?? "index.html";
                if (string.IsNullOrEmpty(requestPath)) requestPath = "index.html";

                var filePath = Path.GetFullPath(Path.Combine(webRoot, requestPath));

                // Security: ensure path is within web root
                if (!filePath.StartsWith(webRoot) || !File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                context.Response.ContentType = ext switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".js" => "application/javascript; charset=utf-8",
                    ".css" => "text/css; charset=utf-8",
                    ".json" => "application/json; charset=utf-8",
                    ".png" => "image/png",
                    ".svg" => "image/svg+xml",
                    _ => "application/octet-stream"
                };

                // Allow CORS for API access
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

                var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
                context.Response.ContentLength64 = fileBytes.Length;
                await context.Response.OutputStream.WriteAsync(fileBytes, ct);
                context.Response.Close();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue serving
            }
        }
    }

    private void StopHttpServer()
    {
        _serverCts?.Cancel();
        try { _httpListener?.Stop(); } catch { }
        try { _httpListener?.Close(); } catch { }
        _httpListener = null;
        _serverCts?.Dispose();
        _serverCts = null;
    }

    private void OnBrowserUrlTapped(object? sender, TappedEventArgs e)
    {
        if (_browserUrl != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo("xdg-open", _browserUrl) { UseShellExecute = false });
            }
            catch { }
        }
    }
#else
    private void OnBrowserUrlTapped(object? sender, TappedEventArgs e) { }
#endif

    private void OnSendMessageToCanvas(object? sender, string json)
    {
        if (!_isWebViewReady) return;

#if !LINUX
        try
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // PostWebMessageAsString calls __dispatchMessageCallback(msg) in JS
                    // which is registered by listenForWebViewMessages() in events.js.
                    // Do NOT use ExecuteScriptAsync — it crashes on macOS due to a bug
                    // in WebView.Avalonia.Cross (null NSObject in result callback).
                    _webView?.PostWebMessageAsString(json, null);
                }
                catch
                {
                    // WebView may be disposed
                }
            });
        }
        catch
        {
            // Dispatcher may fail if window is closing
        }
#endif
        // On Linux, events flow via the REST API SSE endpoint — no postMessage bridge needed.
    }

    private async void OnRequestOpenJsonlFile(object? sender, EventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var claudeProjectsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");

        var options = new FilePickerOpenOptions
        {
            Title = "Open JSONL Transcript",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("JSONL files")
                {
                    Patterns = new[] { "*.jsonl" },
                    AppleUniformTypeIdentifiers = new[] { "public.json", "public.plain-text" }
                },
                new("All files") { Patterns = new[] { "*" } }
            }
        };

        if (Directory.Exists(claudeProjectsDir))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(new Uri($"file://{claudeProjectsDir}"));
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0 && _viewModel != null)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
                await _viewModel.LoadJsonlFileAsync(path);
        }
    }

    private string BuildQueryString()
    {
        if (_viewModel == null) return "";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_viewModel.CurrentSessionId))
            parts.Add($"session={Uri.EscapeDataString(_viewModel.CurrentSessionId)}");
        if (!string.IsNullOrEmpty(_viewModel.ApiBaseUrl))
            parts.Add($"api={Uri.EscapeDataString(_viewModel.ApiBaseUrl)}");
        // Cache-busting to ensure fresh JS/CSS on relaunch
        parts.Add($"v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private static string GetWebAssetsPath()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var webDir = Path.Combine(exeDir, "web", "spark");

        if (Directory.Exists(webDir))
            return webDir;

        // Dev fallback: look relative to the project source
        var devDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "web", "spark"));
        if (Directory.Exists(devDir))
            return devDir;

        return exeDir;
    }
}
