using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalHost.Controls;

/// <summary>
/// Simple markdown/HTML viewer for Avalonia.
/// NOTE: This is a temporary fallback implementation that strips HTML tags and displays plain text.
/// WebView2 is not available on macOS, so this provides basic content viewing.
/// Future enhancement: Use Markdown.Avalonia package for proper markdown rendering.
/// </summary>
public partial class MarkdownViewer : UserControl
{
    private TextBlock? _contentTextBlock;

    public MarkdownViewer()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _contentTextBlock = this.FindControl<TextBlock>("ContentTextBlock");
    }

    public static readonly StyledProperty<string> HtmlContentProperty =
        AvaloniaProperty.Register<MarkdownViewer, string>(nameof(HtmlContent), string.Empty);

    public string HtmlContent
    {
        get => GetValue(HtmlContentProperty);
        set => SetValue(HtmlContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HtmlContentProperty)
        {
            UpdateContent((string?)change.NewValue ?? string.Empty);
        }
    }

    private void UpdateContent(string html)
    {
        if (_contentTextBlock == null)
            return;

        // Strip HTML tags and convert to plain text
        var plainText = StripHtml(html);
        _contentTextBlock.Text = plainText;
    }

    /// <summary>
    /// Strips HTML tags and decodes common HTML entities to produce readable plain text.
    /// </summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove DOCTYPE, html, head, body, style, and script tags with their content
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<html[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</html>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<head[^>]*>.*?</head>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<body[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</body>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[^>]*>.*?</style>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<script[^>]*>.*?</script>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert common block elements to newlines
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<p[^>]*>", "\n\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</p>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<div[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</div>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<h[1-6][^>]*>", "\n\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</li>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<ul[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</ul>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<ol[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</ol>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<hr[^>]*>", "\n─────────────────────\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<blockquote[^>]*>", "\n  ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</blockquote>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<pre[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</pre>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<code[^>]*>", "`", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</code>", "`", RegexOptions.IgnoreCase);

        // Handle table elements
        html = Regex.Replace(html, @"<table[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</table>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<tr[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</tr>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<th[^>]*>", " | ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</th>", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<td[^>]*>", " | ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</td>", string.Empty, RegexOptions.IgnoreCase);

        // Remove all other HTML tags
        html = Regex.Replace(html, @"<[^>]+>", string.Empty);

        // Decode common HTML entities
        html = html.Replace("&nbsp;", " ");
        html = html.Replace("&lt;", "<");
        html = html.Replace("&gt;", ">");
        html = html.Replace("&amp;", "&");
        html = html.Replace("&quot;", "\"");
        html = html.Replace("&#39;", "'");
        html = html.Replace("&apos;", "'");

        // Clean up excessive whitespace
        html = Regex.Replace(html, @"[ \t]+", " ");
        html = Regex.Replace(html, @"\n[ \t]+", "\n");
        html = Regex.Replace(html, @"[ \t]+\n", "\n");
        html = Regex.Replace(html, @"\n{3,}", "\n\n");

        return html.Trim();
    }
}
