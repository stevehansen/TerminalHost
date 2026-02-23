using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Controls;

public partial class HunkStagingDiffViewer : UserControl
{
    public static readonly DependencyProperty DiffTextProperty =
        DependencyProperty.Register(nameof(DiffText), typeof(string), typeof(HunkStagingDiffViewer),
            new PropertyMetadata("", OnDiffTextChanged));

    public static readonly DependencyProperty IsStagedProperty =
        DependencyProperty.Register(nameof(IsStaged), typeof(bool), typeof(HunkStagingDiffViewer),
            new PropertyMetadata(false, OnDiffTextChanged));

    public static readonly DependencyProperty InvisibleChangeInfoProperty =
        DependencyProperty.Register(nameof(InvisibleChangeInfo), typeof(InvisibleChangeInfo), typeof(HunkStagingDiffViewer),
            new PropertyMetadata(null, OnDiffTextChanged));

    public string DiffText
    {
        get => (string)GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    public bool IsStaged
    {
        get => (bool)GetValue(IsStagedProperty);
        set => SetValue(IsStagedProperty, value);
    }

    public InvisibleChangeInfo? InvisibleChangeInfo
    {
        get => (InvisibleChangeInfo?)GetValue(InvisibleChangeInfoProperty);
        set => SetValue(InvisibleChangeInfoProperty, value);
    }

    public event EventHandler<int>? HunkStageRequested;
    public event EventHandler<int>? HunkUnstageRequested;
    public event EventHandler<int>? HunkDiscardRequested;
    public event EventHandler? FixInvisibleChangesRequested;

    private IDiffParserService? _diffParser;

    public HunkStagingDiffViewer()
    {
        InitializeComponent();
    }

    public void SetDiffParser(IDiffParserService parser)
    {
        _diffParser = parser;
    }

    private static void OnDiffTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HunkStagingDiffViewer viewer)
        {
            viewer.RenderDiff();
        }
    }

    private void RenderDiff()
    {
        HunksControl.Items.Clear();

        if (string.IsNullOrEmpty(DiffText))
            return;

        // Fallback: resolve parser from DI if not explicitly set
        if (_diffParser == null)
        {
            _diffParser = App.Current?.Services?.GetService(typeof(IDiffParserService)) as IDiffParserService;
            if (_diffParser == null)
                return;
        }

        // Invisible changes banner
        var info = InvisibleChangeInfo;
        if (info?.HasAnyChange == true)
        {
            var bannerPanel = new StackPanel();

            // Main warning banner
            var bannerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xB8, 0x86, 0x0B)), // amber tint
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B)),
                BorderThickness = new Thickness(0, 0, 0, info.Diagnosis == null ? 1 : 0),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var bannerDock = new DockPanel();

            // Fix / Renormalize button
            var buttonLabel = info.Diagnosis?.FixLabel ?? "Fix";
            var fixButton = CreateHunkButton(buttonLabel, Color.FromRgb(0xB8, 0x86, 0x0B));
            fixButton.Click += (s, e) => FixInvisibleChangesRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(fixButton, Dock.Right);
            bannerDock.Children.Add(fixButton);

            // Warning icon + summary text
            var textBlock = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x40)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            textBlock.Inlines.Add(new Run("\u26A0 Invisible changes: ") { FontWeight = FontWeights.SemiBold });
            textBlock.Inlines.Add(new Run(info.Summary));
            bannerDock.Children.Add(textBlock);

            bannerBorder.Child = bannerDock;
            bannerPanel.Children.Add(bannerBorder);

            // Diagnosis detail row
            if (info.Diagnosis != null)
            {
                var diagBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0x60, 0x90, 0xD0)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 6, 12, 6)
                };

                var diagStack = new StackPanel();

                var diagText = new TextBlock
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };
                diagText.Inlines.Add(new Run("Root cause: ") { FontWeight = FontWeights.SemiBold });
                diagText.Inlines.Add(new Run(info.Diagnosis.Explanation));
                diagStack.Children.Add(diagText);

                var cmdText = new TextBlock
                {
                    Text = $"git {info.Diagnosis.FixCommand}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontFamily = (FontFamily)FindResource("FontFamilyMonospace"),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                diagStack.Children.Add(cmdText);

                diagBorder.Child = diagStack;
                bannerPanel.Children.Add(diagBorder);
            }

            HunksControl.Items.Add(bannerPanel);
        }

        var parsed = _diffParser.Parse(DiffText);
        if (parsed.Hunks.Count == 0)
        {
            // Show header info for binary files (e.g., "Binary files /dev/null and b/image.png differ")
            var binaryLine = parsed.HeaderLines.FirstOrDefault(l => l.StartsWith("Binary files", StringComparison.Ordinal));
            if (binaryLine != null)
            {
                var message = new TextBlock
                {
                    Text = binaryLine,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)),
                    FontFamily = (FontFamily)FindResource("FontFamilyMonospace"),
                    FontSize = 13,
                    Padding = new Thickness(16, 16, 16, 16),
                    FontStyle = FontStyles.Italic
                };
                HunksControl.Items.Add(message);
            }
            return;
        }

        for (int i = 0; i < parsed.Hunks.Count; i++)
        {
            var hunk = parsed.Hunks[i];
            var hunkIndex = i;

            var hunkPanel = new StackPanel();

            // Hunk header bar with stage/unstage button
            var headerBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x55)),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var headerDock = new DockPanel();

            // Stage / Unstage button
            var actionButton = CreateHunkButton(
                IsStaged ? "Unstage Hunk" : "Stage Hunk",
                IsStaged
                    ? Color.FromRgb(0xB8, 0x86, 0x0B)  // amber for unstage
                    : Color.FromRgb(0x1E, 0x7A, 0x60)); // teal for stage
            actionButton.Click += (s, e) =>
            {
                if (IsStaged)
                    HunkUnstageRequested?.Invoke(this, hunkIndex);
                else
                    HunkStageRequested?.Invoke(this, hunkIndex);
            };
            DockPanel.SetDock(actionButton, Dock.Right);
            headerDock.Children.Add(actionButton);

            // Discard button (only for unstaged hunks)
            if (!IsStaged)
            {
                var discardButton = CreateHunkButton("Discard Hunk", Color.FromRgb(0x8B, 0x2E, 0x2E));
                discardButton.Margin = new Thickness(0, 0, 6, 0);
                discardButton.Click += (s, e) => HunkDiscardRequested?.Invoke(this, hunkIndex);
                DockPanel.SetDock(discardButton, Dock.Right);
                headerDock.Children.Add(discardButton);
            }

            // Hunk header text
            var hunkHeaderLine = hunk.Lines.FirstOrDefault(l => l.Type == DiffLineType.HunkHeader);
            var headerText = new TextBlock
            {
                Text = hunkHeaderLine?.Content ?? $"Hunk {i + 1}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)),
                FontFamily = (FontFamily)FindResource("FontFamilyMonospace"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerDock.Children.Add(headerText);

            headerBar.Child = headerDock;
            hunkPanel.Children.Add(headerBar);

            // Diff lines
            foreach (var line in hunk.Lines)
            {
                if (line.Type == DiffLineType.HunkHeader)
                    continue;

                // Render NoNewlineMarker in italic gray
                if (line.Type == DiffLineType.NoNewlineMarker)
                {
                    var markerBlock = new TextBlock
                    {
                        Text = line.Content,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        FontFamily = (FontFamily)FindResource("FontFamilyMonospace"),
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        Padding = new Thickness(54, 0, 4, 0)
                    };
                    hunkPanel.Children.Add(markerBlock);
                    continue;
                }

                var lineGrid = new Grid();
                lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bgColor = line.Type switch
                {
                    DiffLineType.Addition => Color.FromArgb(0x30, 0x4E, 0xC9, 0xB0),
                    DiffLineType.Deletion => Color.FromArgb(0x30, 0xF1, 0x4C, 0x4C),
                    _ => Colors.Transparent
                };
                lineGrid.Background = new SolidColorBrush(bgColor);

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
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    FontFamily = (FontFamily)FindResource("FontFamilyMonospace"),
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
                    DiffLineType.Addition => Color.FromRgb(0x4E, 0xC9, 0xB0),
                    DiffLineType.Deletion => Color.FromRgb(0xF1, 0x4C, 0x4C),
                    _ => Color.FromRgb(0xCC, 0xCC, 0xCC)
                };

                var content = new TextBlock
                {
                    Text = prefix + line.Content,
                    Foreground = new SolidColorBrush(contentFg),
                    FontFamily = (FontFamily)FindResource("FontFamilyMonospace"),
                    FontSize = 12,
                    Padding = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(content, 1);
                lineGrid.Children.Add(content);

                hunkPanel.Children.Add(lineGrid);
            }

            HunksControl.Items.Add(hunkPanel);
        }
    }

    private Button CreateHunkButton(string label, Color bgColor)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null,
            Background = new SolidColorBrush(bgColor),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
        };

        // Apply TerminalSwitchButton template so Background is respected
        if (FindResource("TerminalSwitchButton") is Style style)
        {
            btn.Style = style;
            // Re-apply our overrides after setting style (local values beat style setters)
            btn.Background = new SolidColorBrush(bgColor);
            btn.Foreground = new SolidColorBrush(Colors.White);
            btn.FontWeight = FontWeights.SemiBold;
            btn.Padding = new Thickness(10, 4, 10, 4);
        }

        return btn;
    }
}
