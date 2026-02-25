using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Controls;

public partial class HunkStagingDiffViewer : UserControl
{
    public static readonly StyledProperty<string> DiffTextProperty =
        AvaloniaProperty.Register<HunkStagingDiffViewer, string>(
            nameof(DiffText),
            defaultValue: string.Empty);

    public static readonly StyledProperty<bool> IsStagedProperty =
        AvaloniaProperty.Register<HunkStagingDiffViewer, bool>(
            nameof(IsStaged),
            defaultValue: false);

    public string DiffText
    {
        get => GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    public bool IsStaged
    {
        get => GetValue(IsStagedProperty);
        set => SetValue(IsStagedProperty, value);
    }

    public event EventHandler<int>? HunkStageRequested;
    public event EventHandler<int>? HunkUnstageRequested;
    public event EventHandler<int>? HunkDiscardRequested;

    private IDiffParserService? _diffParser;

    // Brushes
    private static readonly IBrush HunkHeaderBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x55));
    private static readonly IBrush HunkHeaderTextBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush StageButtonBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x7A, 0x60));
    private static readonly IBrush UnstageButtonBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
    private static readonly IBrush DiscardButtonBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E));
    private static readonly IBrush LineNumberBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
    private static readonly IBrush AdditionForegroundBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush DeletionForegroundBrush = new SolidColorBrush(Color.Parse("#F14C4C"));
    private static readonly IBrush ContextForegroundBrush = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush AdditionBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x4E, 0xC9, 0xB0));
    private static readonly IBrush DeletionBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xF1, 0x4C, 0x4C));

    private FontFamily MonospaceFont => (FontFamily)(this.FindResource("FontFamilyMonospace") ?? FontFamily.Default);

    public HunkStagingDiffViewer()
    {
        InitializeComponent();
    }

    public void SetDiffParser(IDiffParserService parser)
    {
        _diffParser = parser;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Resolve parser from DI if not explicitly set
        if (_diffParser == null)
        {
            try
            {
                var app = Application.Current as TerminalHost.App;
                if (app?.Services != null)
                {
                    _diffParser = app.Services.GetService(typeof(IDiffParserService)) as IDiffParserService;
                }
            }
            catch
            {
                // Ignore if DI not available (design time, etc.)
            }
        }

        // Re-render if we have content
        if (!string.IsNullOrEmpty(DiffText))
        {
            RenderDiff();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DiffTextProperty || change.Property == IsStagedProperty)
        {
            RenderDiff();
        }
    }

    private void RenderDiff()
    {
        HunksControl.ItemsSource = null;

        if (string.IsNullOrEmpty(DiffText))
            return;

        if (_diffParser == null)
            return;

        var parsed = _diffParser.Parse(DiffText);
        if (parsed.Hunks.Count == 0)
        {
            // Show header info for binary files
            var binaryLine = parsed.HeaderLines.FirstOrDefault(l => l.StartsWith("Binary files", StringComparison.Ordinal));
            if (binaryLine != null)
            {
                var message = new TextBlock
                {
                    Text = binaryLine,
                    Foreground = HunkHeaderTextBrush,
                    FontFamily = MonospaceFont,
                    FontSize = 13,
                    Padding = new Thickness(16),
                    FontStyle = FontStyle.Italic
                };
                HunksControl.ItemsSource = new Control[] { message };
            }
            return;
        }

        var panels = new List<Control>();

        for (int i = 0; i < parsed.Hunks.Count; i++)
        {
            var hunk = parsed.Hunks[i];
            var hunkIndex = i;

            var hunkPanel = new StackPanel();

            // Hunk header bar with stage/unstage/discard buttons
            var headerBar = new Border
            {
                Background = HunkHeaderBackgroundBrush,
                Padding = new Thickness(8, 4, 8, 4)
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            // Hunk header text
            var hunkHeaderLine = hunk.Lines.FirstOrDefault(l => l.Type == DiffLineType.HunkHeader);
            var headerText = new TextBlock
            {
                Text = hunkHeaderLine?.Content ?? $"Hunk {i + 1}",
                Foreground = HunkHeaderTextBrush,
                FontFamily = MonospaceFont,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerText, 0);
            headerGrid.Children.Add(headerText);

            // Action buttons panel
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };

            // Stage / Unstage button
            var actionButton = CreateHunkButton(
                IsStaged ? "Unstage Hunk" : "Stage Hunk",
                IsStaged ? UnstageButtonBrush : StageButtonBrush);
            actionButton.Click += (s, e) =>
            {
                if (IsStaged)
                    HunkUnstageRequested?.Invoke(this, hunkIndex);
                else
                    HunkStageRequested?.Invoke(this, hunkIndex);
            };
            buttonsPanel.Children.Add(actionButton);

            // Discard button (only for unstaged hunks)
            if (!IsStaged)
            {
                var discardButton = CreateHunkButton("Discard Hunk", DiscardButtonBrush);
                discardButton.Click += (s, e) => HunkDiscardRequested?.Invoke(this, hunkIndex);
                buttonsPanel.Children.Add(discardButton);
            }

            Grid.SetColumn(buttonsPanel, 1);
            headerGrid.Children.Add(buttonsPanel);

            headerBar.Child = headerGrid;
            hunkPanel.Children.Add(headerBar);

            // Diff lines
            foreach (var line in hunk.Lines)
            {
                if (line.Type == DiffLineType.HunkHeader)
                    continue;

                var lineGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("50,*")
                };

                var bgBrush = line.Type switch
                {
                    DiffLineType.Addition => AdditionBackgroundBrush,
                    DiffLineType.Deletion => DeletionBackgroundBrush,
                    _ => Brushes.Transparent
                };
                lineGrid.Background = bgBrush;

                // Line numbers
                var lineNumText = line.Type switch
                {
                    DiffLineType.Addition => $"  +{line.NewLineNumber}",
                    DiffLineType.Deletion => $"-{line.OldLineNumber}  ",
                    DiffLineType.Context => $"{line.OldLineNumber,3} {line.NewLineNumber}",
                    _ => ""
                };

                var lineNum = new TextBlock
                {
                    Text = lineNumText,
                    Foreground = LineNumberBrush,
                    FontFamily = MonospaceFont,
                    FontSize = 12,
                    Padding = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(lineNum, 0);
                lineGrid.Children.Add(lineNum);

                var prefix = line.Type switch
                {
                    DiffLineType.Addition => "+",
                    DiffLineType.Deletion => "-",
                    _ => " "
                };

                var contentFg = line.Type switch
                {
                    DiffLineType.Addition => AdditionForegroundBrush,
                    DiffLineType.Deletion => DeletionForegroundBrush,
                    _ => ContextForegroundBrush
                };

                var content = new TextBlock
                {
                    Text = prefix + line.Content,
                    Foreground = contentFg,
                    FontFamily = MonospaceFont,
                    FontSize = 12,
                    Padding = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(content, 1);
                lineGrid.Children.Add(content);

                hunkPanel.Children.Add(lineGrid);
            }

            panels.Add(hunkPanel);
        }

        HunksControl.ItemsSource = panels;
    }

    private Button CreateHunkButton(string label, IBrush bgBrush)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Background = bgBrush,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3)
        };

        return btn;
    }
}
