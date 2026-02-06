using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TerminalHost.Core.Domain;

namespace TerminalHost.Controls;

public partial class CommitGraphControl : UserControl
{
    private const double ColumnSpacing = 16;
    private const double NodeRadius = 4;
    private const double LeftPadding = 10;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(List<GraphNode>), typeof(CommitGraphControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(CommitGraphControl),
            new PropertyMetadata(32.0, OnItemsSourceChanged));

    public List<GraphNode>? ItemsSource
    {
        get => (List<GraphNode>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public CommitGraphControl()
    {
        InitializeComponent();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CommitGraphControl control)
        {
            control.RenderGraph();
        }
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();

        var nodes = ItemsSource;
        if (nodes == null || nodes.Count == 0)
            return;

        var itemHeight = ItemHeight;
        var maxColumn = nodes.Max(n => n.Column);
        var totalWidth = (maxColumn + 1) * ColumnSpacing + LeftPadding * 2;
        var totalHeight = nodes.Count * itemHeight;

        GraphCanvas.Width = Math.Max(totalWidth, 40);
        GraphCanvas.Height = totalHeight;

        // Draw edges first (behind nodes)
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var y = i * itemHeight + itemHeight / 2;

            foreach (var edge in node.Edges)
            {
                var fromX = LeftPadding + edge.FromColumn * ColumnSpacing;
                var toX = LeftPadding + edge.ToColumn * ColumnSpacing;
                var toY = y + itemHeight;

                var brush = BrushFromHex(edge.Color);

                if (fromX == toX)
                {
                    // Straight vertical line
                    var line = new Line
                    {
                        X1 = fromX,
                        Y1 = y,
                        X2 = toX,
                        Y2 = toY,
                        Stroke = brush,
                        StrokeThickness = edge.IsMerge ? 1.5 : 2
                    };
                    if (edge.IsMerge)
                    {
                        line.StrokeDashArray = [4, 2];
                    }
                    GraphCanvas.Children.Add(line);
                }
                else
                {
                    // Curved line for merge/branch
                    var path = new Path
                    {
                        Stroke = brush,
                        StrokeThickness = edge.IsMerge ? 1.5 : 2,
                        Data = new PathGeometry
                        {
                            Figures =
                            [
                                new PathFigure
                                {
                                    StartPoint = new Point(fromX, y),
                                    Segments =
                                    {
                                        new BezierSegment(
                                            new Point(fromX, y + itemHeight * 0.5),
                                            new Point(toX, y + itemHeight * 0.5),
                                            new Point(toX, toY),
                                            true)
                                    }
                                }
                            ]
                        }
                    };
                    if (edge.IsMerge)
                    {
                        path.StrokeDashArray = [4, 2];
                    }
                    GraphCanvas.Children.Add(path);
                }
            }
        }

        // Draw nodes on top
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var x = LeftPadding + node.Column * ColumnSpacing;
            var y = i * itemHeight + itemHeight / 2;

            var circle = new Ellipse
            {
                Width = NodeRadius * 2,
                Height = NodeRadius * 2,
                Fill = BrushFromHex(node.Color),
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1
            };

            Canvas.SetLeft(circle, x - NodeRadius);
            Canvas.SetTop(circle, y - NodeRadius);
            GraphCanvas.Children.Add(circle);
        }
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7)
            return System.Windows.Media.Brushes.Gray;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return System.Windows.Media.Brushes.Gray;
        }
    }
}
