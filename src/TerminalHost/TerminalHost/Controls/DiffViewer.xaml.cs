using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;
using TerminalHost.Services.SyntaxHighlighting;

namespace TerminalHost.Controls;

public partial class DiffViewer : UserControl
{
    private readonly DiffHighlighter _highlighter = new();
    private DiffViewMode _currentMode = DiffViewMode.Unified;
    private string _currentDiffText = string.Empty;

    public DiffViewer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Try to get the service from DI if not explicitly set
        if (DiffParserService == null)
        {
            try
            {
                DiffParserService = App.Current.Services.GetService(typeof(IDiffParserService)) as IDiffParserService;
            }
            catch
            {
                // Ignore if DI not available (design time, etc.)
            }
        }

        // Pass the service to the side-by-side viewer
        if (SideBySideViewer != null && DiffParserService != null)
        {
            SideBySideViewer.DiffParserService = DiffParserService;
        }

        // Refresh the view if we have content
        if (!string.IsNullOrEmpty(_currentDiffText))
        {
            UpdateDocument(_currentDiffText);
        }
    }

    public static readonly DependencyProperty DiffTextProperty =
        DependencyProperty.Register(
            nameof(DiffText),
            typeof(string),
            typeof(DiffViewer),
            new PropertyMetadata(string.Empty, OnDiffTextChanged));

    public string DiffText
    {
        get => (string)GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    public static readonly DependencyProperty DiffParserServiceProperty =
        DependencyProperty.Register(
            nameof(DiffParserService),
            typeof(IDiffParserService),
            typeof(DiffViewer),
            new PropertyMetadata(null, OnDiffParserServiceChanged));

    public IDiffParserService? DiffParserService
    {
        get => (IDiffParserService?)GetValue(DiffParserServiceProperty);
        set => SetValue(DiffParserServiceProperty, value);
    }

    public static readonly DependencyProperty ViewModeProperty =
        DependencyProperty.Register(
            nameof(ViewMode),
            typeof(DiffViewMode),
            typeof(DiffViewer),
            new PropertyMetadata(DiffViewMode.Unified, OnViewModeChanged));

    public DiffViewMode ViewMode
    {
        get => (DiffViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    private static void OnDiffTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer)
        {
            viewer._currentDiffText = (string)e.NewValue;
            viewer.UpdateDocument(viewer._currentDiffText);
        }
    }

    private static void OnDiffParserServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer && viewer.SideBySideViewer != null)
        {
            viewer.SideBySideViewer.DiffParserService = e.NewValue as IDiffParserService;
            viewer.UpdateDocument(viewer._currentDiffText);
        }
    }

    private static void OnViewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer)
        {
            viewer._currentMode = (DiffViewMode)e.NewValue;
            viewer.UpdateModeUI();
            viewer.UpdateDocument(viewer._currentDiffText);
        }
    }

    private void OnUnifiedModeChecked(object sender, RoutedEventArgs e)
    {
        ViewMode = DiffViewMode.Unified;
    }

    private void OnSideBySideModeChecked(object sender, RoutedEventArgs e)
    {
        ViewMode = DiffViewMode.SideBySide;
    }

    private void UpdateModeUI()
    {
        if (UnifiedModeButton != null)
        {
            UnifiedModeButton.IsChecked = _currentMode == DiffViewMode.Unified;
        }

        if (SideBySideModeButton != null)
        {
            SideBySideModeButton.IsChecked = _currentMode == DiffViewMode.SideBySide;
        }

        if (DiffScrollViewer != null)
        {
            DiffScrollViewer.Visibility = _currentMode == DiffViewMode.Unified ? Visibility.Visible : Visibility.Collapsed;
        }

        if (SideBySideViewer != null)
        {
            SideBySideViewer.Visibility = _currentMode == DiffViewMode.SideBySide ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateDocument(string diffContent)
    {
        if (_currentMode == DiffViewMode.Unified)
        {
            UpdateUnifiedView(diffContent);
        }
        else
        {
            UpdateSideBySideView(diffContent);
        }
    }

    private void UpdateUnifiedView(string diffContent)
    {
        if (DiffScrollViewer == null) return;

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

    private void UpdateSideBySideView(string diffContent)
    {
        if (SideBySideViewer == null) return;

        SideBySideViewer.DiffText = diffContent;
    }

    private static FlowDocument CreateInfoDocument(string message)
    {
        var doc = new FlowDocument
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), // BackgroundDarkestBrush
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };

        doc.Blocks.Add(new Paragraph(new Run(message)));
        return doc;
    }
}
