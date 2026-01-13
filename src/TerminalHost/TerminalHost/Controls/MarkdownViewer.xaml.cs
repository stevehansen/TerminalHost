using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace TerminalHost.Controls;

public partial class MarkdownViewer : UserControl
{
    private bool _isWebViewInitialized;
    private string? _currentVirtualHostBasePath;
    private const string VirtualHostName = "localmd.files";

    /// <summary>
    /// Event raised when a relative markdown link is clicked.
    /// The event argument contains the full path to the markdown file.
    /// </summary>
    public event EventHandler<string>? MarkdownLinkClicked;

    public MarkdownViewer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        InitializeWebView();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-hook WebView2 events when control is loaded again after being unloaded
        // Always unhook first to avoid double-hooking
        if (_isWebViewInitialized && WebView.CoreWebView2 != null)
        {
            WebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;

            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unhook WebView2 event handlers when control is unloaded
        if (_isWebViewInitialized && WebView.CoreWebView2 != null)
        {
            WebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
    }

    private static void ShowError(string message)
    {
        try
        {
            var toastService = App.Current.Services.GetService<IToastService>();
            toastService?.Show(message, ToastType.Error);
        }
        catch
        {
            // Toast service not available, ignore
        }
    }

    private async void InitializeWebView()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();
            _isWebViewInitialized = true;

            // Configure settings - keep context menu enabled for debugging
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            // Handle all navigation-related events
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            if (!string.IsNullOrEmpty(HtmlContent))
            {
                NavigateToHtml(HtmlContent);
            }
        }
        catch (Exception ex)
        {
            ShowError($"WebView2 initialization failed: {ex.Message}");
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Prevent new window, handle the URL ourselves
        e.Handled = true;

        if (!string.IsNullOrEmpty(e.Uri))
        {
            HandleUrl(e.Uri);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Handle messages from JavaScript (for intercepted link clicks)
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(message))
            {
                HandleUrl(message);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to handle link: {ex.Message}");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Allow initial content load:
        // - NavigateToString uses data:text/html URIs
        // - about:blank is the initial state
        // - about:blank#blocked happens when file:// navigation is blocked
        if (string.IsNullOrEmpty(e.Uri) ||
            e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return;

        // Allow virtual host requests (for images)
        if (e.Uri.StartsWith($"https://{VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
            return;

        // Cancel all other navigations - we handle them via NewWindowRequested or WebMessage
        e.Cancel = true;
        HandleUrl(e.Uri);
    }

    private void HandleUrl(string url)
    {
        try
        {
            // Handle file:// URLs
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = new Uri(url).LocalPath;

                // Check if it's a markdown file
                if (localPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(localPath))
                    {
                        MarkdownLinkClicked?.Invoke(this, localPath);
                    }
                    else
                    {
                        ShowError($"File not found: {localPath}");
                    }
                    return;
                }

                // For other local files, let the system handle them
                if (File.Exists(localPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = localPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    ShowError($"File not found: {localPath}");
                }
                return;
            }

            // Handle http/https URLs - open in default browser (except our virtual host)
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Don't open virtual host URLs in browser
                if (url.StartsWith($"https://{VirtualHostName}/", StringComparison.OrdinalIgnoreCase))
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return;
            }

            // Unrecognized URL scheme
            ShowError($"Unsupported link type: {url}");
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open: {ex.Message}");
        }
    }

    #region Dependency Properties

    public static readonly DependencyProperty HtmlContentProperty =
        DependencyProperty.Register("HtmlContent", typeof(string), typeof(MarkdownViewer),
            new PropertyMetadata(string.Empty, OnHtmlContentChanged));

    public string HtmlContent
    {
        get => (string)GetValue(HtmlContentProperty);
        set => SetValue(HtmlContentProperty, value);
    }

    public static readonly DependencyProperty BasePathProperty =
        DependencyProperty.Register("BasePath", typeof(string), typeof(MarkdownViewer),
            new PropertyMetadata(null, OnBasePathChanged));

    /// <summary>
    /// Base path for resolving relative resources (images).
    /// When set, enables virtual host mapping to load local images.
    /// </summary>
    public string? BasePath
    {
        get => (string?)GetValue(BasePathProperty);
        set => SetValue(BasePathProperty, value);
    }

    private static void OnHtmlContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
        {
            viewer.NavigateToHtml((string)e.NewValue);
        }
    }

    private static void OnBasePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
        {
            viewer.SetupVirtualHostMapping((string?)e.NewValue);
        }
    }

    #endregion

    private void SetupVirtualHostMapping(string? basePath)
    {
        if (!_isWebViewInitialized || WebView.CoreWebView2 == null)
            return;

        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            return;

        // Only set up mapping if it changed
        if (_currentVirtualHostBasePath == basePath)
            return;

        try
        {
            // Clear previous mapping by setting to a non-existent path first (WebView2 limitation)
            // Then set the new mapping
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                basePath,
                CoreWebView2HostResourceAccessKind.Allow);

            _currentVirtualHostBasePath = basePath;
        }
        catch (Exception ex)
        {
            ShowError($"Failed to set up local file access: {ex.Message}");
        }
    }

    private void NavigateToHtml(string html)
    {
        if (!_isWebViewInitialized || WebView.CoreWebView2 == null) return;

        try
        {
            // Set up virtual host mapping if we have a base path
            if (!string.IsNullOrEmpty(BasePath))
            {
                SetupVirtualHostMapping(BasePath);

                // Convert file:// URLs to virtual host URLs for images
                html = ConvertFileUrlsToVirtualHost(html, BasePath);
            }

            WebView.CoreWebView2.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to render content: {ex.Message}");
        }
    }

    private string ConvertFileUrlsToVirtualHost(string html, string basePath)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(basePath))
            return html;

        // Normalize base path for comparison
        var normalizedBasePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var basePathUri = new Uri(normalizedBasePath + Path.DirectorySeparatorChar);

        // Match src="file://..." for images
        var srcPattern = @"src=""(file:///[^""]+)""";

        return Regex.Replace(html, srcPattern, match =>
        {
            var fileUrl = match.Groups[1].Value;
            try
            {
                var fileUri = new Uri(fileUrl);
                var localPath = fileUri.LocalPath;

                // Normalize the local path
                var normalizedLocalPath = Path.GetFullPath(localPath);

                // Check if the file is under the base path
                if (normalizedLocalPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Get relative path from base
                    var relativePath = normalizedLocalPath.Substring(normalizedBasePath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/');

                    return $"src=\"https://{VirtualHostName}/{relativePath}\"";
                }

                // File is outside base path - try mapping to parent directories
                // Find common ancestor and map from there
                var parentPath = normalizedBasePath;
                while (!string.IsNullOrEmpty(parentPath))
                {
                    var parent = Path.GetDirectoryName(parentPath);
                    if (parent != null && normalizedLocalPath.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                    {
                        // Remap virtual host to this parent directory
                        SetupVirtualHostMapping(parent);

                        var relativePath = normalizedLocalPath.Substring(parent.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace(Path.DirectorySeparatorChar, '/');

                        return $"src=\"https://{VirtualHostName}/{relativePath}\"";
                    }
                    parentPath = parent;
                }

                // Can't resolve - leave as-is (won't load, but won't break)
                return match.Value;
            }
            catch
            {
                return match.Value;
            }
        }, RegexOptions.IgnoreCase);
    }
}
