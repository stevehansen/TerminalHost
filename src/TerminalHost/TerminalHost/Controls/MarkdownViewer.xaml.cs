using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace TerminalHost.Controls;

public partial class MarkdownViewer : UserControl
{
    private bool _isWebViewInitialized;

    public MarkdownViewer()
    {
        InitializeComponent();
        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();
            _isWebViewInitialized = true;
            if (!string.IsNullOrEmpty(HtmlContent))
            {
                NavigateToHtml(HtmlContent);
            }
        }
        catch
        {
            // WebView2 might not be installed
        }
    }

    public static readonly DependencyProperty HtmlContentProperty =
        DependencyProperty.Register("HtmlContent", typeof(string), typeof(MarkdownViewer), new PropertyMetadata(string.Empty, OnHtmlContentChanged));

    public string HtmlContent
    {
        get => (string)GetValue(HtmlContentProperty);
        set => SetValue(HtmlContentProperty, value);
    }

    private static void OnHtmlContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
        {
            viewer.NavigateToHtml((string)e.NewValue);
        }
    }

    private void NavigateToHtml(string html)
    {
        if (!_isWebViewInitialized || WebView.CoreWebView2 == null) return;
        
        try
        {
            WebView.CoreWebView2.NavigateToString(html);
        }
        catch
        {
            // Ignore
        }
    }
}
