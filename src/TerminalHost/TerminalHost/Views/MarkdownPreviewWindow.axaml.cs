using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Window for Markdown preview - resizable, draggable, proper window behavior.
/// NOTE: WebView2 integration requires platform-specific implementation for Avalonia.
/// </summary>
public partial class MarkdownPreviewWindow : Window
{
    private bool _webViewInitialized;
    private MarkdownPreviewViewModel? _viewModel;

    public MarkdownPreviewWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
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

    private async void OnOpened(object? sender, EventArgs e)
    {
        // Initialize WebView (platform-specific implementation needed)
        if (!_webViewInitialized)
        {
            try
            {
                // TODO: Initialize Avalonia WebView equivalent
                // This requires platform-specific WebView control
                // Options:
                // 1. CefGlue/CefNet for Chromium Embedded Framework
                // 2. Platform-specific native WebView wrappers
                // 3. HTML rendering library (limited feature set)

                _webViewInitialized = true;

                // Load initial content if available
                if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.RenderedHtml))
                {
                    NavigateToHtml(_viewModel.RenderedHtml);
                }
            }
            catch
            {
                // WebView runtime not available
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
        if (!_webViewInitialized)
            return;

        try
        {
            // TODO: Navigate to HTML in WebView
            // Implementation depends on chosen WebView control
        }
        catch
        {
            // Ignore navigation errors
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
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
