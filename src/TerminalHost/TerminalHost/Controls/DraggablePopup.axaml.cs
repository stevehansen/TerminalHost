using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TerminalHost.Services;

namespace TerminalHost.Controls;

public partial class DraggablePopup : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private bool _hasBeenPositioned;
    private IScreenService? _screenService;

    public DraggablePopup()
    {
        InitializeComponent();

        // Wire up drag events
        var dragHeader = this.FindControl<Border>("DragHeader");
        if (dragHeader != null)
        {
            dragHeader.PointerPressed += DragHeader_PointerPressed;
            dragHeader.PointerMoved += DragHeader_PointerMoved;
            dragHeader.PointerReleased += DragHeader_PointerReleased;
        }

        // Wire up resize grip events
        var resizeGrip = this.FindControl<Thumb>("ResizeGrip");
        if (resizeGrip != null)
        {
            resizeGrip.DragDelta += ResizeGrip_DragDelta;
        }

        // Wire up keyboard events
        this.KeyDown += UserControl_KeyDown;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Get screen service from DI
        _screenService = App.Current.Services.GetService(typeof(IScreenService)) as IScreenService;
    }

    #region Avalonia Properties

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DraggablePopup, bool>(nameof(IsOpen), defaultValue: false, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty && change.NewValue is true)
        {
            CenterOnScreen();
        }
    }

    private void CenterOnScreen()
    {
        // Only center if this is the first time opening or offsets are at default (0,0)
        if (_hasBeenPositioned && (HorizontalOffset != 0 || VerticalOffset != 0))
            return;

        // Try to center relative to the main window
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = desktop.MainWindow;
            if (mainWindow != null && _screenService != null)
            {
                // Get the screen containing the main window
                var windowPos = mainWindow.Position;
                var windowBounds = _screenService.GetScreenFromPoint(windowPos.X, windowPos.Y);
                var workArea = _screenService.GetPrimaryWorkingArea();

                // Get window position and size
                double windowLeft, windowTop, windowWidth, windowHeight;

                if (mainWindow.WindowState == WindowState.Maximized)
                {
                    // For maximized, use the working area
                    windowLeft = workArea.X;
                    windowTop = workArea.Y;
                    windowWidth = workArea.Width;
                    windowHeight = workArea.Height;
                }
                else
                {
                    // Use actual window bounds
                    var bounds = mainWindow.Bounds;
                    windowLeft = windowPos.X;
                    windowTop = windowPos.Y;
                    windowWidth = bounds.Width;
                    windowHeight = bounds.Height;
                }

                // Calculate popup size
                var popupWidth = PopupWidth;
                var popupHeight = PopupHeight;

                // Center within the window bounds
                HorizontalOffset = windowLeft + (windowWidth - popupWidth) / 2;
                VerticalOffset = windowTop + (windowHeight - popupHeight) / 2;

                // Clamp to screen bounds
                if (HorizontalOffset < workArea.X) HorizontalOffset = workArea.X;
                if (VerticalOffset < workArea.Y) HorizontalOffset = workArea.Y;
                if (HorizontalOffset + popupWidth > workArea.X + workArea.Width)
                    HorizontalOffset = workArea.X + workArea.Width - popupWidth;
                if (VerticalOffset + popupHeight > workArea.Y + workArea.Height)
                    VerticalOffset = workArea.Y + workArea.Height - popupHeight;
            }
            else if (_screenService != null)
            {
                // Fallback to primary screen center
                var primaryScreen = _screenService.GetPrimaryScreenBounds();
                HorizontalOffset = primaryScreen.X + (primaryScreen.Width - PopupWidth) / 2;
                VerticalOffset = primaryScreen.Y + (primaryScreen.Height - PopupHeight) / 2;
            }
        }

        _hasBeenPositioned = true;
    }

    public static readonly StyledProperty<double> PopupWidthProperty =
        AvaloniaProperty.Register<DraggablePopup, double>(nameof(PopupWidth), defaultValue: 800.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double PopupWidth
    {
        get => GetValue(PopupWidthProperty);
        set => SetValue(PopupWidthProperty, value);
    }

    public static readonly StyledProperty<double> PopupHeightProperty =
        AvaloniaProperty.Register<DraggablePopup, double>(nameof(PopupHeight), defaultValue: 600.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double PopupHeight
    {
        get => GetValue(PopupHeightProperty);
        set => SetValue(PopupHeightProperty, value);
    }

    public static readonly StyledProperty<double> HorizontalOffsetProperty =
        AvaloniaProperty.Register<DraggablePopup, double>(nameof(HorizontalOffset), defaultValue: 0.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double HorizontalOffset
    {
        get => GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<DraggablePopup, double>(nameof(VerticalOffset), defaultValue: 0.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DraggablePopup, string>(nameof(Title), defaultValue: string.Empty);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<DraggablePopup, ICommand?>(nameof(CloseCommand));

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public static readonly StyledProperty<object?> HeaderRightContentProperty =
        AvaloniaProperty.Register<DraggablePopup, object?>(nameof(HeaderRightContent));

    public object? HeaderRightContent
    {
        get => GetValue(HeaderRightContentProperty);
        set => SetValue(HeaderRightContentProperty, value);
    }

    public static readonly StyledProperty<object?> HeaderLeftContentProperty =
        AvaloniaProperty.Register<DraggablePopup, object?>(nameof(HeaderLeftContent));

    public object? HeaderLeftContent
    {
        get => GetValue(HeaderLeftContentProperty);
        set => SetValue(HeaderLeftContentProperty, value);
    }

    public static readonly StyledProperty<object?> PopupContentProperty =
        AvaloniaProperty.Register<DraggablePopup, object?>(nameof(PopupContent));

    public object? PopupContent
    {
        get => GetValue(PopupContentProperty);
        set => SetValue(PopupContentProperty, value);
    }

    #endregion

    #region Event Handlers

    private void DragHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = pointer.Position;

            // Capture pointer for drag operation
            if (sender is Border border)
            {
                e.Pointer.Capture(border);
            }

            e.Handled = true;
        }
    }

    private void DragHeader_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = e.GetCurrentPoint(this).Position;
        var diff = currentPos - _dragStartPoint;

        HorizontalOffset += diff.X;
        VerticalOffset += diff.Y;

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void DragHeader_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void ResizeGrip_DragDelta(object? sender, VectorEventArgs e)
    {
        var newWidth = PopupWidth + e.Vector.X;
        var newHeight = PopupHeight + e.Vector.Y;

        if (newWidth >= 400) PopupWidth = newWidth;
        if (newHeight >= 300) PopupHeight = newHeight;
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (CloseCommand?.CanExecute(null) == true)
            {
                CloseCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    #endregion
}
