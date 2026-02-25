using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TerminalHost.Core.Domain;

namespace TerminalHost.Controls;

public partial class CommitGraphControl : UserControl
{
    private const double ColumnSpacing = 16;
    private const double NodeRadius = 4;
    private const double LeftPadding = 10;

    public static readonly StyledProperty<List<GraphNode>?> ItemsSourceProperty =
        AvaloniaProperty.Register<CommitGraphControl, List<GraphNode>?>(nameof(ItemsSource));

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<CommitGraphControl, double>(nameof(ItemHeight), 32.0);

    public List<GraphNode>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private static readonly IBrush WhiteBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.Parse("#808080"));

    public CommitGraphControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty || change.Property == ItemHeightProperty)
        {
            InvalidateVisual();
            UpdateCanvasSize();
        }
    }

    private void UpdateCanvasSize()
    {
        var nodes = ItemsSource;
        if (nodes == null || nodes.Count == 0)
        {
            Width = 40;
            Height = 0;
            GraphCanvas.Width = 40;
            GraphCanvas.Height = 0;
            return;
        }

        var itemHeight = ItemHeight;
        var maxColumn = nodes.Max(n => n.Column);
        var totalWidth = (maxColumn + 1) * ColumnSpacing + LeftPadding * 2;
        var totalHeight = nodes.Count * itemHeight;

        Width = Math.Max(totalWidth, 40);
        Height = totalHeight;
        GraphCanvas.Width = Math.Max(totalWidth, 40);
        GraphCanvas.Height = totalHeight;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var nodes = ItemsSource;
        if (nodes == null || nodes.Count == 0)
            return;

        var itemHeight = ItemHeight;

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

                var edgeBrush = BrushFromHex(edge.Color);
                var strokeThickness = edge.IsMerge ? 1.5 : 2.0;

                Pen pen;
                if (edge.IsMerge)
                {
                    pen = new Pen(edgeBrush, strokeThickness,
                        new DashStyle(new[] { 4.0, 2.0 }, 0));
                }
                else
                {
                    pen = new Pen(edgeBrush, strokeThickness);
                }

                if (Math.Abs(fromX - toX) < 0.1)
                {
                    // Straight vertical line
                    context.DrawLine(pen, new Point(fromX, y), new Point(toX, toY));
                }
                else
                {
                    // Curved line for merge/branch
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(fromX, y), false);
                        ctx.CubicBezierTo(
                            new Point(fromX, y + itemHeight * 0.5),
                            new Point(toX, y + itemHeight * 0.5),
                            new Point(toX, toY));
                    }

                    context.DrawGeometry(null, pen, geometry);
                }
            }
        }

        // Draw nodes on top
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var x = LeftPadding + node.Column * ColumnSpacing;
            var y = i * itemHeight + itemHeight / 2;

            var nodeBrush = BrushFromHex(node.Color);
            var outlinePen = new Pen(WhiteBrush, 1.0);

            context.DrawEllipse(nodeBrush, outlinePen, new Point(x, y), NodeRadius, NodeRadius);
        }
    }

    private static IBrush BrushFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7)
            return GrayBrush;

        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch
        {
            return GrayBrush;
        }
    }
}
