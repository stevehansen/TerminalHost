using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// UserControl for Markdown preview content.
/// Can be used in Panel, Popup, or Window mode.
/// </summary>
public partial class MarkdownPreviewView : UserControl
{
    private bool _webViewInitialized;
    private MarkdownPreviewViewModel? _viewModel;

    public MarkdownPreviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old ViewModel
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Subscribe to new ViewModel
        if (DataContext is MarkdownPreviewViewModel vm)
        {
            _viewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;

            // Load initial content if WebView is ready
            if (_webViewInitialized && !string.IsNullOrEmpty(vm.RenderedHtml))
            {
                NavigateToHtml(vm.RenderedHtml);
            }
        }
        else
        {
            _viewModel = null;
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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Clean up subscriptions
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
}
