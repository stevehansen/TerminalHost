using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.Controls;

public partial class SideBySideDiffViewer : UserControl
{
    private bool _isSyncingScroll;

    public SideBySideDiffViewer()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty DiffTextProperty =
        DependencyProperty.Register(
            nameof(DiffText),
            typeof(string),
            typeof(SideBySideDiffViewer),
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
            typeof(SideBySideDiffViewer),
            new PropertyMetadata(null, OnDiffParserServiceChanged));

    public IDiffParserService? DiffParserService
    {
        get => (IDiffParserService?)GetValue(DiffParserServiceProperty);
        set => SetValue(DiffParserServiceProperty, value);
    }

    private static void OnDiffTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SideBySideDiffViewer viewer)
        {
            viewer.UpdateDiffView();
        }
    }

    private static void OnDiffParserServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SideBySideDiffViewer viewer)
        {
            viewer.UpdateDiffView();
        }
    }

    private void UpdateDiffView()
    {
        var diffText = DiffText;
        var parserService = DiffParserService;

        if (string.IsNullOrEmpty(diffText) || parserService == null)
        {
            LeftItemsControl.ItemsSource = null;
            RightItemsControl.ItemsSource = null;
            EmptyStateText.Visibility = string.IsNullOrEmpty(diffText) ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        try
        {
            var rows = parserService.ParseToSideBySide(diffText);

            if (rows.Count == 0)
            {
                LeftItemsControl.ItemsSource = null;
                RightItemsControl.ItemsSource = null;
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            EmptyStateText.Visibility = Visibility.Collapsed;
            LeftItemsControl.ItemsSource = rows;
            RightItemsControl.ItemsSource = rows;
        }
        catch
        {
            LeftItemsControl.ItemsSource = null;
            RightItemsControl.ItemsSource = null;
            EmptyStateText.Visibility = Visibility.Visible;
        }
    }

    private void LeftScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll) return;

        _isSyncingScroll = true;
        try
        {
            RightScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            RightScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }

    private void RightScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll) return;

        _isSyncingScroll = true;
        try
        {
            LeftScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            LeftScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }
}
