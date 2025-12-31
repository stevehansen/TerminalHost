using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.Controls;

public partial class SideBySideDiffViewer : UserControl
{
    private bool _isSyncingScroll;

    public SideBySideDiffViewer()
    {
        InitializeComponent();

        // Wire up scroll synchronization
        if (LeftScrollViewer != null)
        {
            LeftScrollViewer.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(offset => OnLeftScrollChanged(offset));
        }

        if (RightScrollViewer != null)
        {
            RightScrollViewer.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(offset => OnRightScrollChanged(offset));
        }
    }

    public static readonly StyledProperty<string> DiffTextProperty =
        AvaloniaProperty.Register<SideBySideDiffViewer, string>(
            nameof(DiffText),
            defaultValue: string.Empty);

    public string DiffText
    {
        get => GetValue(DiffTextProperty);
        set => SetValue(DiffTextProperty, value);
    }

    public static readonly StyledProperty<IDiffParserService?> DiffParserServiceProperty =
        AvaloniaProperty.Register<SideBySideDiffViewer, IDiffParserService?>(
            nameof(DiffParserService),
            defaultValue: null);

    public IDiffParserService? DiffParserService
    {
        get => GetValue(DiffParserServiceProperty);
        set => SetValue(DiffParserServiceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DiffTextProperty || change.Property == DiffParserServiceProperty)
        {
            UpdateDiffView();
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
            EmptyStateText.IsVisible = string.IsNullOrEmpty(diffText);
            return;
        }

        try
        {
            var rows = parserService.ParseToSideBySide(diffText);

            if (rows.Count == 0)
            {
                LeftItemsControl.ItemsSource = null;
                RightItemsControl.ItemsSource = null;
                EmptyStateText.IsVisible = true;
                return;
            }

            EmptyStateText.IsVisible = false;
            LeftItemsControl.ItemsSource = rows;
            RightItemsControl.ItemsSource = rows;
        }
        catch
        {
            LeftItemsControl.ItemsSource = null;
            RightItemsControl.ItemsSource = null;
            EmptyStateText.IsVisible = true;
        }
    }

    private void OnLeftScrollChanged(Vector offset)
    {
        if (_isSyncingScroll || RightScrollViewer == null) return;

        _isSyncingScroll = true;
        try
        {
            RightScrollViewer.Offset = offset;
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }

    private void OnRightScrollChanged(Vector offset)
    {
        if (_isSyncingScroll || LeftScrollViewer == null) return;

        _isSyncingScroll = true;
        try
        {
            LeftScrollViewer.Offset = offset;
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }
}