using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.Controls;

public partial class DiffViewer : UserControl
{
    private DiffViewMode _currentMode = DiffViewMode.Unified;
    private string _currentDiffText = string.Empty;

    // Regex patterns for diff syntax highlighting
    private static readonly Regex DiffHeaderRegex = new(@"^diff --git", RegexOptions.Compiled);
    private static readonly Regex FileHeaderRegex = new(@"^(---|\+\+\+) ", RegexOptions.Compiled);
    private static readonly Regex HunkHeaderRegex = new(@"^@@\s+.*\s+@@", RegexOptions.Compiled);
    private static readonly Regex AdditionRegex = new(@"^\+(?!\+\+)", RegexOptions.Compiled);
    private static readonly Regex DeletionRegex = new(@"^-(?!--)", RegexOptions.Compiled);
    private static readonly Regex IndexRegex = new(@"^index [0-9a-f]+\.\.[0-9a-f]+", RegexOptions.Compiled);
    private static readonly Regex MetaRegex = new(@"^(new file mode|deleted file mode|old mode|new mode|similarity index|rename from|rename to|copy from|copy to|Binary files)", RegexOptions.Compiled);

    // Brushes for syntax highlighting
    private static readonly IBrush AdditionBrush = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush DeletionBrush = new SolidColorBrush(Color.Parse("#F14C4C"));
    private static readonly IBrush HunkHeaderBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush FileHeaderBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush MetaBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush IndexBrush = new SolidColorBrush(Color.Parse("#858585"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush AdditionBackgroundBrush = new SolidColorBrush(Color.Parse("#233623"));
    private static readonly IBrush DeletionBackgroundBrush = new SolidColorBrush(Color.Parse("#3E1E1E"));
    private static readonly IBrush TransparentBrush = Brushes.Transparent;

    public DiffViewer()
    {
        InitializeComponent();

        // Wire up mode toggle events
        if (UnifiedModeButton != null)
        {
            UnifiedModeButton.GetObservable(RadioButton.IsCheckedProperty)
                .Subscribe(isChecked =>
                {
                    if (isChecked == true)
                    {
                        ViewMode = DiffViewMode.Unified;
                    }
                });
        }

        if (SideBySideModeButton != null)
        {
            SideBySideModeButton.GetObservable(RadioButton.IsCheckedProperty)
                .Subscribe(isChecked =>
                {
                    if (isChecked == true)
                    {
                        ViewMode = DiffViewMode.SideBySide;
                    }
                });
        }
    }

    public static readonly StyledProperty<string> DiffTextProperty =
        AvaloniaProperty.Register<DiffViewer, string>(
            nameof(DiffText),
            defaultValue: string.Empty);

    public string DiffText
    {
        get => GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    public static readonly StyledProperty<IDiffParserService?> DiffParserServiceProperty =
        AvaloniaProperty.Register<DiffViewer, IDiffParserService?>(
            nameof(DiffParserService),
            defaultValue: null);

    public IDiffParserService? DiffParserService
    {
        get => GetValue(DiffParserServiceProperty);
        set => SetValue(DiffParserServiceProperty, value);
    }

    public static readonly StyledProperty<DiffViewMode> ViewModeProperty =
        AvaloniaProperty.Register<DiffViewer, DiffViewMode>(
            nameof(ViewMode),
            defaultValue: DiffViewMode.Unified);

    public DiffViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Try to get the service from DI if not explicitly set
        if (DiffParserService == null)
        {
            try
            {
                var app = Application.Current as App;
                if (app?.Services != null)
                {
                    DiffParserService = app.Services.GetService(typeof(IDiffParserService)) as IDiffParserService;
                }
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DiffTextProperty)
        {
            _currentDiffText = change.GetNewValue<string>() ?? string.Empty;
            UpdateDocument(_currentDiffText);
        }
        else if (change.Property == DiffParserServiceProperty)
        {
            if (SideBySideViewer != null)
            {
                SideBySideViewer.DiffParserService = change.GetNewValue<IDiffParserService?>();
            }
            UpdateDocument(_currentDiffText);
        }
        else if (change.Property == ViewModeProperty)
        {
            _currentMode = change.GetNewValue<DiffViewMode>();
            UpdateModeUI();
            UpdateDocument(_currentDiffText);
        }
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
            DiffScrollViewer.IsVisible = _currentMode == DiffViewMode.Unified;
        }

        if (SideBySideViewer != null)
        {
            SideBySideViewer.IsVisible = _currentMode == DiffViewMode.SideBySide;
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
        if (DiffItemsControl == null || EmptyStateText == null) return;

        if (string.IsNullOrEmpty(diffContent))
        {
            DiffItemsControl.ItemsSource = null;
            EmptyStateText.IsVisible = true;
            return;
        }

        try
        {
            var lines = diffContent.Split('\n');
            var diffLines = new List<DiffLineViewModel>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var lineNumber = i + 1;
                var (foreground, background) = GetDiffLineStyle(line);

                diffLines.Add(new DiffLineViewModel
                {
                    LineNumber = lineNumber.ToString().PadLeft(4),
                    Content = line,
                    Foreground = foreground,
                    Background = background
                });
            }

            DiffItemsControl.ItemsSource = diffLines;
            EmptyStateText.IsVisible = false;
        }
        catch
        {
            DiffItemsControl.ItemsSource = null;
            EmptyStateText.IsVisible = true;
        }
    }

    private void UpdateSideBySideView(string diffContent)
    {
        if (SideBySideViewer == null) return;

        SideBySideViewer.DiffText = diffContent;
    }

    private (IBrush Foreground, IBrush Background) GetDiffLineStyle(string line)
    {
        // Diff header: diff --git a/file b/file
        if (DiffHeaderRegex.IsMatch(line))
        {
            return (FileHeaderBrush, TransparentBrush);
        }

        // File headers: --- a/file or +++ b/file
        if (FileHeaderRegex.IsMatch(line))
        {
            return (FileHeaderBrush, TransparentBrush);
        }

        // Hunk header: @@ -1,5 +1,5 @@
        if (HunkHeaderRegex.IsMatch(line))
        {
            return (HunkHeaderBrush, TransparentBrush);
        }

        // Index line: index abc123..def456 100644
        if (IndexRegex.IsMatch(line))
        {
            return (IndexBrush, TransparentBrush);
        }

        // Meta information
        if (MetaRegex.IsMatch(line))
        {
            return (MetaBrush, TransparentBrush);
        }

        // Addition line: +added content
        if (AdditionRegex.IsMatch(line))
        {
            return (AdditionBrush, AdditionBackgroundBrush);
        }

        // Deletion line: -removed content
        if (DeletionRegex.IsMatch(line))
        {
            return (DeletionBrush, DeletionBackgroundBrush);
        }

        // Context line (unchanged) or any other line
        return (DefaultBrush, TransparentBrush);
    }

    /// <summary>
    /// View model for a single diff line in unified view.
    /// </summary>
    private class DiffLineViewModel
    {
        public string LineNumber { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public IBrush Foreground { get; init; } = DefaultBrush;
        public IBrush Background { get; init; } = TransparentBrush;
    }
}
