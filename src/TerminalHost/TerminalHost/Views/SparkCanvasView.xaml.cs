using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Hosts the Spark Canvas WebView2 instance.
/// Maps local web assets via virtual host and bridges messages between C# and JS.
/// </summary>
public partial class SparkCanvasView : UserControl
{
    private bool _isWebViewInitialized;
    private SparkCanvasViewModel? _viewModel;
    private const string VirtualHostName = "spark.local";

    public SparkCanvasView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old VM
        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;
        }

        _viewModel = DataContext as SparkCanvasViewModel;

        // Subscribe to new VM
        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas += OnSendMessageToCanvas;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isWebViewInitialized)
        {
            InitializeWebView();
        }
        else if (SparkWebView.CoreWebView2 != null)
        {
            // Re-hook events on reload
            SparkWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            SparkWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isWebViewInitialized && SparkWebView.CoreWebView2 != null)
        {
            SparkWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        if (_viewModel != null)
        {
            _viewModel.SendMessageToCanvas -= OnSendMessageToCanvas;
        }
    }

    private async void InitializeWebView()
    {
        try
        {
            await SparkWebView.EnsureCoreWebView2Async();
            _isWebViewInitialized = true;

            // Configure settings
            SparkWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            SparkWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            SparkWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            // Event handlers
            SparkWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            SparkWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            SparkWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            // Map the web assets folder to a virtual host
            var webAssetsPath = GetWebAssetsPath();
            SparkWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                webAssetsPath,
                CoreWebView2HostResourceAccessKind.Allow);

            // Navigate to the canvas
            SparkWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html");

            // Hide loading overlay
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spark WebView2 init failed: {ex.Message}");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Only allow navigation to our virtual host
        if (!e.Uri.StartsWith($"https://{VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // Prevent popups
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            // Check for "ready" signal
            if (message.Contains("\"action\":\"ready\""))
            {
                _viewModel?.OnCanvasReady();
                return;
            }

            // Session picker: user selected a session in the JS dropdown
            if (message.Contains("\"action\":\"selectSession\""))
            {
                _viewModel?.OnCanvasMessage(message);
                return;
            }

            // Session picker: user clicked refresh
            if (message.Contains("\"action\":\"refreshSessions\""))
            {
                _viewModel?.RefreshSessionsCommand.Execute(null);
                return;
            }

            // Theme changed
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
        if (!_isWebViewInitialized || SparkWebView.CoreWebView2 == null) return;

        try
        {
            // Must dispatch to UI thread since events may fire from background
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    SparkWebView.CoreWebView2?.PostWebMessageAsString(json);
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
        // Look for web assets relative to the executable
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var webDir = Path.Combine(exeDir, "web", "spark");

        if (Directory.Exists(webDir))
            return webDir;

        // Dev fallback: look relative to the project source
        var devDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "web", "spark"));
        if (Directory.Exists(devDir))
            return devDir;

        // Last resort: create temp dir with error page
        return exeDir;
    }
}
