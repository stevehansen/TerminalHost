using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TerminalHost.Controls;

/// <summary>
/// Markdown viewer control using Markdig for parsing and custom Avalonia rendering.
/// Supports headings, bold, italic, code blocks, lists, links, and more.
/// </summary>
public partial class MarkdownViewer : UserControl
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    // Colors for dark theme
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush HeadingBrush = new SolidColorBrush(Color.Parse("#E0E0E0"));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush CodeBlockBg = new SolidColorBrush(Color.Parse("#2D2D2D"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush QuoteBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush QuoteBorderBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush ListMarkerBrush = new SolidColorBrush(Color.Parse("#569CD6"));

    public MarkdownViewer()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// The raw markdown content to render.
    /// </summary>
    public static readonly StyledProperty<string> MarkdownContentProperty =
        AvaloniaProperty.Register<MarkdownViewer, string>(nameof(MarkdownContent), string.Empty);

    public string MarkdownContent
    {
        get => GetValue(MarkdownContentProperty);
        set => SetValue(MarkdownContentProperty, value);
    }

    /// <summary>
    /// Collection of rendered controls for the ItemsControl.
    /// </summary>
    public static readonly StyledProperty<ObservableCollection<Control>> MarkdownBlocksProperty =
        AvaloniaProperty.Register<MarkdownViewer, ObservableCollection<Control>>(
            nameof(MarkdownBlocks),
            new ObservableCollection<Control>());

    public ObservableCollection<Control> MarkdownBlocks
    {
        get => GetValue(MarkdownBlocksProperty);
        set => SetValue(MarkdownBlocksProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownContentProperty)
        {
            RenderMarkdown((string?)change.NewValue ?? string.Empty);
        }
    }

    private void RenderMarkdown(string markdown)
    {
        var blocks = new ObservableCollection<Control>();

        if (string.IsNullOrEmpty(markdown))
        {
            MarkdownBlocks = blocks;
            return;
        }

        try
        {
            var document = Markdig.Markdown.Parse(markdown, Pipeline);

            foreach (var block in document)
            {
                var control = RenderBlock(block);
                if (control != null)
                {
                    blocks.Add(control);
                }
            }
        }
        catch
        {
            // Fallback to plain text on parsing error
            blocks.Add(new TextBlock
            {
                Text = markdown,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            });
        }

        MarkdownBlocks = blocks;
    }

    private Control? RenderBlock(Block block, bool skipTaskListInline = false)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph, skipTaskListInline),
            FencedCodeBlock fencedCode => RenderCodeBlock(fencedCode),
            CodeBlock codeBlock => RenderCodeBlock(codeBlock),
            QuoteBlock quote => RenderQuote(quote),
            ListBlock list => RenderList(list),
            ThematicBreakBlock => RenderHorizontalRule(),
            LinkReferenceDefinitionGroup => null, // Link references are not rendered visually
            LinkReferenceDefinition => null, // Link references are not rendered visually
            _ => RenderGenericBlock(block)
        };
    }

    private Control RenderHeading(HeadingBlock heading)
    {
        var textBlock = new SelectableTextBlock
        {
            Foreground = HeadingBrush,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, heading.Level <= 2 ? 16 : 8, 0, 4)
        };

        textBlock.FontSize = heading.Level switch
        {
            1 => 28,
            2 => 24,
            3 => 20,
            4 => 18,
            5 => 16,
            _ => 14
        };

        RenderInlines(textBlock.Inlines, heading.Inline);
        return textBlock;
    }

    private Control RenderParagraph(ParagraphBlock paragraph, bool skipTaskListInline = false)
    {
        var textBlock = new SelectableTextBlock
        {
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 24
        };

        RenderInlines(textBlock.Inlines, paragraph.Inline, skipTaskListInline);
        return textBlock;
    }

    private Control RenderCodeBlock(Block codeBlock)
    {
        string code;
        if (codeBlock is FencedCodeBlock fenced)
        {
            code = fenced.Lines.ToString().TrimEnd();
        }
        else if (codeBlock is CodeBlock cb)
        {
            code = cb.Lines.ToString().TrimEnd();
        }
        else
        {
            return new TextBlock { Text = "" };
        }

        return new Border
        {
            Background = CodeBlockBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8),
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(1),
            Child = new SelectableTextBlock
            {
                Text = code,
                Foreground = TextBrush,
                FontFamily = new FontFamily("Consolas,Menlo,Monaco,monospace"),
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap
            }
        };
    }

    private Control RenderQuote(QuoteBlock quote)
    {
        var panel = new StackPanel { Spacing = 4 };

        foreach (var block in quote)
        {
            var control = RenderBlock(block);
            if (control is TextBlock tb)
            {
                tb.Foreground = QuoteBrush;
            }
            if (control != null)
            {
                panel.Children.Add(control);
            }
        }

        return new Border
        {
            BorderBrush = QuoteBorderBrush,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 8),
            Child = panel
        };
    }

    private Control RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4) };
        int index = 1;

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };

                // Check if this is a task list item
                var taskListInfo = GetTaskListInfo(listItem);

                // Bullet, number, or checkbox
                string marker;
                if (taskListInfo.HasValue)
                {
                    marker = taskListInfo.Value ? "☑" : "☐";
                }
                else
                {
                    marker = list.IsOrdered ? $"{index++}." : "•";
                }

                itemPanel.Children.Add(new TextBlock
                {
                    Text = marker,
                    Foreground = ListMarkerBrush,
                    FontWeight = FontWeight.Bold,
                    Width = 24,
                    Margin = new Thickness(8, 0, 8, 0)
                });

                // Item content
                var contentPanel = new StackPanel { Spacing = 2 };
                foreach (var block in listItem)
                {
                    var control = RenderBlock(block, skipTaskListInline: taskListInfo.HasValue);
                    if (control != null)
                    {
                        contentPanel.Children.Add(control);
                    }
                }
                itemPanel.Children.Add(contentPanel);

                panel.Children.Add(itemPanel);
            }
        }

        return panel;
    }

    /// <summary>
    /// Gets task list info from a list item. Returns true if checked, false if unchecked, null if not a task list.
    /// </summary>
    private bool? GetTaskListInfo(ListItemBlock listItem)
    {
        foreach (var block in listItem)
        {
            if (block is ParagraphBlock paragraph && paragraph.Inline != null)
            {
                foreach (var inline in paragraph.Inline)
                {
                    if (inline is TaskList taskList)
                    {
                        return taskList.Checked;
                    }
                }
            }
        }
        return null;
    }

    private Control RenderHorizontalRule()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Margin = new Thickness(0, 16)
        };
    }

    private Control? RenderGenericBlock(Block block)
    {
        // Try to extract text from unknown blocks
        var text = block.ToString();
        if (!string.IsNullOrEmpty(text))
        {
            return new TextBlock
            {
                Text = text,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            };
        }
        return null;
    }

    private void RenderInlines(InlineCollection inlines, ContainerInline? container, bool skipTaskListInline = false)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            // Skip TaskList inline if we're rendering inside a task list item (checkbox already shown)
            if (skipTaskListInline && inline is TaskList)
                continue;

            RenderInline(inlines, inline);
        }
    }

    private void RenderInline(InlineCollection inlines, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                inlines.Add(new Run(literal.Content.ToString()));
                break;

            case EmphasisInline emphasis:
                var emphasisRuns = new List<Run>();
                CollectRuns(emphasis, emphasisRuns);
                foreach (var run in emphasisRuns)
                {
                    if (emphasis.DelimiterCount == 2)
                    {
                        run.FontWeight = FontWeight.Bold;
                    }
                    else
                    {
                        run.FontStyle = FontStyle.Italic;
                    }
                    inlines.Add(run);
                }
                break;

            case CodeInline code:
                inlines.Add(new Run(code.Content)
                {
                    Foreground = CodeBrush,
                    FontFamily = new FontFamily("Consolas,Menlo,Monaco,monospace")
                });
                break;

            case LinkInline link:
                var linkText = GetLinkText(link);
                inlines.Add(new Run(linkText)
                {
                    Foreground = LinkBrush,
                    TextDecorations = TextDecorations.Underline
                });
                break;

            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;

            case TaskList:
                // TaskList is handled by RenderList, skip rendering here
                break;

            case ContainerInline container:
                RenderInlines(inlines, container);
                break;

            default:
                // Try to get text content
                var text = inline.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    inlines.Add(new Run(text));
                }
                break;
        }
    }

    private void CollectRuns(ContainerInline container, List<Run> runs)
    {
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal)
            {
                runs.Add(new Run(literal.Content.ToString()));
            }
            else if (inline is ContainerInline child)
            {
                CollectRuns(child, runs);
            }
        }
    }

    private string GetLinkText(LinkInline link)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in link)
        {
            if (inline is LiteralInline literal)
            {
                sb.Append(literal.Content.ToString());
            }
        }
        return sb.Length > 0 ? sb.ToString() : link.Url ?? "";
    }
}
