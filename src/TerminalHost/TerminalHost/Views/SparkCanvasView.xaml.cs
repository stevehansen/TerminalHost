using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TerminalHost.Core.ViewModels;
using TerminalHost.Spark;

namespace TerminalHost.Views;

/// <summary>
/// Hosts the Spark Canvas WebView2 instance. Constructs a
/// <see cref="WebView2CanvasTransport"/> and hands it to the ViewModel — the
/// view has no knowledge of action verb strings or JSON envelope shape.
/// </summary>
public partial class SparkCanvasView : UserControl
{
    private bool _isWebViewInitialized;
    private WebView2CanvasTransport? _transport;
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
        if (_viewModel != null)
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;

        _viewModel = DataContext as SparkCanvasViewModel;

        if (_viewModel != null)
        {
            _viewModel.RequestOpenJsonlFile += OnRequestOpenJsonlFile;
            TryAttachTransport();

            if (_viewModel.HasPendingJsonlOpen)
                Dispatcher.BeginInvoke(new Action(() => _viewModel.OpenJsonlFileCommand.Execute(null)));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isWebViewInitialized)
            InitializeWebView();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.RequestOpenJsonlFile -= OnRequestOpenJsonlFile;
    }

    private async void InitializeWebView()
    {
        try
        {
            await SparkWebView.EnsureCoreWebView2Async();
            _isWebViewInitialized = true;

            SparkWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            SparkWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            SparkWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            SparkWebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;

            try
            {
                await SparkWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
            }
            catch { /* Older WebView2 runtimes may not support this */ }

            SparkWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            SparkWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            // Wire the transport — the orchestrator (via the VM) does the rest.
            TryAttachTransport();

            var webAssetsPath = GetWebAssetsPath();
            SparkWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                webAssetsPath,
                CoreWebView2HostResourceAccessKind.Allow);

            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SparkWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html?v={cacheBuster}");

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spark WebView2 init failed: {ex.Message}");
        }
    }

    private void TryAttachTransport()
    {
        if (_viewModel == null || !_isWebViewInitialized || SparkWebView.CoreWebView2 == null)
            return;
        if (_transport != null)
            return;
        _transport = new WebView2CanvasTransport(SparkWebView.CoreWebView2, Dispatcher);
        _viewModel.AttachTransport(_transport);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith($"https://{VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
            e.Cancel = true;
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // Prevent popups
    }

    private async void OnRequestOpenJsonlFile(object? sender, EventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open JSONL Transcript",
            Filter = "JSONL files (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            DefaultExt = ".jsonl"
        };

        var claudeProjectsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
        if (Directory.Exists(claudeProjectsDir))
            dialog.InitialDirectory = claudeProjectsDir;

        if (dialog.ShowDialog() == true && _viewModel != null)
            await _viewModel.LoadJsonlFileAsync(dialog.FileName);
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
