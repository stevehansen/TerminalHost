using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WebViewCore.Events;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Hosts the Spark Canvas WebView instance.
/// Maps local web assets via file:// URI and bridges messages between C# and JS.
/// Uses WebView.Avalonia.Cross (WKWebView on macOS).
///
/// Communication:
///   JS → C#: window.webkit.messageHandlers.webview.postMessage(msg)
///             Received via WebMessageReceived event.
///   C# → JS: PostWebMessageAsString(json) which calls __dispatchMessageCallback(msg)
///             in JS. The JS registers this callback in listenForWebViewMessages().
/// </summary>
public partial class SparkCanvasView : UserControl
{
    private bool _isWebViewReady;
    private SparkCanvasViewModel? _viewModel;

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
            var webAssetsPath = GetWebAssetsPath();
            var indexPath = Path.Combine(webAssetsPath, "index.html");

            if (File.Exists(indexPath))
            {
                // Pass session ID and API base as URL params so JS auto-connects to the right session
                var query = "";
                if (_viewModel != null)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(_viewModel.CurrentSessionId))
                        parts.Add($"session={Uri.EscapeDataString(_viewModel.CurrentSessionId)}");
                    if (!string.IsNullOrEmpty(_viewModel.ApiBaseUrl))
                        parts.Add($"api={Uri.EscapeDataString(_viewModel.ApiBaseUrl)}");
                    if (parts.Count > 0)
                        query = "?" + string.Join("&", parts);
                }
                SparkWebView.Url = new Uri($"file://{indexPath}{query}");
            }
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;
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

    private void OnSendMessageToCanvas(object? sender, string json)
    {
        if (!_isWebViewReady) return;

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
                    SparkWebView.PostWebMessageAsString(json, null);
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
