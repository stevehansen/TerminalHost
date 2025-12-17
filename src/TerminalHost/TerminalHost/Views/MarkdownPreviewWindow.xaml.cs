using System.ComponentModel;
using System.Windows;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Window for Markdown preview - resizable, draggable, proper window behavior.
/// </summary>
public partial class MarkdownPreviewWindow : Window
{
    private bool _webViewInitialized;
    private MarkdownPreviewViewModel? _viewModel;

    public MarkdownPreviewWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old ViewModel
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        // Subscribe to new ViewModel
        if (DataContext is MarkdownPreviewViewModel vm)
        {
            _viewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.CloseRequested += OnCloseRequested;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize WebView2
        if (!_webViewInitialized)
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                _webViewInitialized = true;

                // Load initial content if available
                if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.RenderedHtml))
                {
                    NavigateToHtml(_viewModel.RenderedHtml);
                }
            }
            catch
            {
                // WebView2 runtime not installed
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarkdownPreviewViewModel.RenderedHtml))
        {
            if (_viewModel != null)
            {
                NavigateToHtml(_viewModel.RenderedHtml);
            }
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void NavigateToHtml(string html)
    {
        if (!_webViewInitialized || WebView.CoreWebView2 == null)
            return;

        try
        {
            WebView.CoreWebView2.NavigateToString(html);
        }
        catch
        {
            // Ignore navigation errors
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Clean up subscriptions
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.OnWindowClosed();
        }
    }
}
