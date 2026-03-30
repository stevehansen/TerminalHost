using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WebViewCore.Events;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Hosts the Spark Canvas WebView instance.
/// Maps local web assets via file:// URI and bridges messages between C# and JS.
/// Uses WebView.Avalonia.Cross (WKWebView on macOS).
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
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;

        _viewModel = DataContext as SparkCanvasViewModel;

        if (_viewModel != null)
            _viewModel.SendMessageToCanvas += OnSendMessageToCanvas;
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
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;
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
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    // Call handleHostMessage directly via ExecuteScriptAsync
                    // PostWebMessageAsString doesn't reliably deliver to JS on WKWebView
                    var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
                    await SparkWebView.ExecuteScriptAsync($"if(typeof handleHostMessage==='function')handleHostMessage('{escaped}')");
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
