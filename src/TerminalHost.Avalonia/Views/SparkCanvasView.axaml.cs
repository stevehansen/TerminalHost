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
using TerminalHost.Core.ViewModels;
#if !LINUX
using TerminalHost.AvaloniaSpark;
#endif

namespace TerminalHost.Views;

/// <summary>
/// Hosts the Spark Canvas visualization.
/// On macOS / Windows (Avalonia): embedded WebView via WebView.Avalonia.Cross.
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
    private AvaloniaWebViewCanvasTransport? _transport;
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
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;

        _viewModel = DataContext as SparkCanvasViewModel;

        if (_viewModel != null)
        {
            _viewModel.RequestOpenJsonlFile += OnRequestOpenJsonlFile;
#if !LINUX
            TryAttachTransport();
#endif
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
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;
#if LINUX
        StopHttpServer();
#endif
    }

#if !LINUX
    private void StartWebViewMode()
    {
        _webView = new AvaloniaWebView.WebView();
        _webView.NavigationCompleted += SparkWebView_NavigationCompleted;
        WebViewHost.Content = _webView;

        TryAttachTransport();

        var webAssetsPath = GetWebAssetsPath();
        var indexPath = Path.Combine(webAssetsPath, "index.html");

        if (File.Exists(indexPath))
        {
            var query = BuildQueryString();
            _webView.Url = new Uri($"file://{indexPath}{query}");
        }
    }

    private void SparkWebView_NavigationCompleted(object? sender, WebViewCore.Events.WebViewUrlLoadedEventArg e)
    {
        _isWebViewReady = true;
        LoadingOverlay.IsVisible = false;
    }

    private void TryAttachTransport()
    {
        if (_viewModel == null || _webView == null) return;
        if (_transport != null) return;
        _transport = new AvaloniaWebViewCanvasTransport(_webView);
        _viewModel.AttachTransport(_transport);
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
        catch { /* xdg-open not available */ }

        _isWebViewReady = true;
        LoadingOverlay.IsVisible = false;
        BrowserFallback.IsVisible = true;
        BrowserUrlText.Text = _browserUrl;

        // On Linux, the canvas runs in an external browser — wire a NullCanvasTransport
        // so the VM's state machine still has a transport reference (events flow via SSE/REST).
        _viewModel?.AttachTransport(new TerminalHost.Core.Services.Spark.NullCanvasTransport());
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
        // Normalize the root to end with a directory separator so the StartsWith check
        // below catches a sibling-prefix bypass (e.g. webRoot "/app/web" vs leaked
        // "/app/web_secret"). Path.GetFullPath also normalizes any "../" segments.
        var normalizedRoot = Path.GetFullPath(webRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(ct);
                var requestPath = context.Request.Url?.AbsolutePath?.TrimStart('/') ?? "index.html";
                if (string.IsNullOrEmpty(requestPath)) requestPath = "index.html";

                var filePath = Path.GetFullPath(Path.Combine(normalizedRoot, requestPath));

                if (!filePath.StartsWith(normalizedRoot, StringComparison.Ordinal) || !File.Exists(filePath))
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
        // Build static query — the new orchestrator-based VM doesn't expose
        // session/api URLs directly. Cache-bust to ensure fresh JS/CSS on relaunch.
        return $"?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
    }

    private static string GetWebAssetsPath()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var webDir = Path.Combine(exeDir, "web", "spark");

        if (Directory.Exists(webDir))
            return webDir;

        var devDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "web", "spark"));
        if (Directory.Exists(devDir))
            return devDir;

        return exeDir;
    }
}
