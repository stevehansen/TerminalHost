using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TerminalHost.Services.SyntaxHighlighting;

namespace TerminalHost.Controls;

public partial class DiffViewer : UserControl
{
    private readonly DiffHighlighter _highlighter = new();

    public DiffViewer()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DiffTextProperty =
        DependencyProperty.Register("DiffText", typeof(string), typeof(DiffViewer), new PropertyMetadata(string.Empty, OnDiffTextChanged));

    public string DiffText
    {
        get => (string)GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    private static void OnDiffTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer)
        {
            viewer.UpdateDocument((string)e.NewValue);
        }
    }

    private void UpdateDocument(string diffContent)
    {
        if (string.IsNullOrEmpty(diffContent))
        {
            DiffScrollViewer.Document = CreateInfoDocument("No changes to display.");
            return;
        }

        try 
        {
            DiffScrollViewer.Document = _highlighter.CreateHighlightedDocument(diffContent, null);
        }
        catch (Exception ex)
        {
            DiffScrollViewer.Document = CreateInfoDocument($"Error rendering diff: {ex.Message}");
        }
    }

    private static FlowDocument CreateInfoDocument(string message)
    {
        return new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), // BackgroundDarkestBrush
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };
    }
}
