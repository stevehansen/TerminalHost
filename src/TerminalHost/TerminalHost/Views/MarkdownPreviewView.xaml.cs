using System.Windows;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

/// <summary>
/// UserControl for Markdown preview content.
/// Can be used in Panel, Popup, or Window mode.
/// Uses MarkdownViewer control for consistent link handling and error toasts.
/// </summary>
public partial class MarkdownPreviewView : UserControl
{
    private MarkdownPreviewViewModel? _viewModel;

    public MarkdownPreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = DataContext as MarkdownPreviewViewModel;
    }

    private void OnMarkdownLinkClicked(object? sender, string filePath)
    {
        if (_viewModel != null)
        {
            _ = _viewModel.OpenAsync(filePath);
        }
    }
}
