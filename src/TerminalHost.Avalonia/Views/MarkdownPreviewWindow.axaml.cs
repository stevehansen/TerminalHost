using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// Window for Markdown preview using Markdown.Avalonia for rendering.
/// </summary>
public partial class MarkdownPreviewWindow : Window
{
    private MarkdownPreviewViewModel? _viewModel;

    public MarkdownPreviewWindow()
    {
        InitializeComponent();
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
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        // Subscribe to new ViewModel
        if (DataContext is MarkdownPreviewViewModel vm)
        {
            _viewModel = vm;
            vm.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // Clean up subscriptions
        if (_viewModel != null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.OnWindowClosed();
        }
    }
}