using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Controls;

public partial class DraggablePopup : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private bool _hasBeenPositioned;
    private object? _lastDataContext;

    public DraggablePopup()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Reset positioning when we get a new/different DataContext
        // This ensures each panel gets centered properly
        if (e.NewValue != _lastDataContext)
        {
            _hasBeenPositioned = false;
            _lastDataContext = e.NewValue;

            // Reset offsets to trigger re-centering
            // (bindings may fail if new DataContext doesn't have these properties)
            HorizontalOffset = 0;
            VerticalOffset = 0;
        }
    }

    #region Dependency Properties

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register("IsOpen", typeof(bool), typeof(DraggablePopup), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DraggablePopup popup && e.NewValue is true)
        {
            popup.CenterOnScreen();
        }
    }

    private void CenterOnScreen()
    {
        // Only center if this is the first time opening or offsets are at default (0,0)
        if (_hasBeenPositioned && (HorizontalOffset != 0 || VerticalOffset != 0))
            return;

        // Try to center relative to the main window
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow != null)
        {
            // Get the DPI scaling factor for proper coordinate conversion
            var source = PresentationSource.FromVisual(mainWindow);
            var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            // Get window bounds in screen pixels using Win32
            var hwnd = new WindowInteropHelper(mainWindow).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var workArea = screen.WorkingArea;

            // Get window position and size in screen pixels
            double windowLeft, windowTop, windowWidth, windowHeight;

            if (mainWindow.WindowState == WindowState.Maximized)
            {
                // For maximized, use the working area
                windowLeft = workArea.Left;
                windowTop = workArea.Top;
                windowWidth = workArea.Width;
                windowHeight = workArea.Height;
            }
            else
            {
                // Convert WPF DIUs to screen pixels
                windowLeft = mainWindow.Left * dpiX;
                windowTop = mainWindow.Top * dpiY;
                windowWidth = mainWindow.ActualWidth * dpiX;
                windowHeight = mainWindow.ActualHeight * dpiY;
            }

            // Calculate responsive size based on SizePreset if available
            var (targetWidth, targetHeight) = CalculateResponsiveSize(
                mainWindow.ActualWidth, mainWindow.ActualHeight, dpiX, dpiY);

            // Update popup size
            PopupWidth = targetWidth;
            PopupHeight = targetHeight;

            // Calculate popup size in screen pixels
            var popupWidth = PopupWidth * dpiX;
            var popupHeight = PopupHeight * dpiY;

            // Center within the window bounds (in screen pixels for Popup with Placement=Absolute)
            HorizontalOffset = windowLeft + (windowWidth - popupWidth) / 2;
            VerticalOffset = windowTop + (windowHeight - popupHeight) / 2;

            // Clamp to screen bounds
            if (HorizontalOffset < workArea.Left) HorizontalOffset = workArea.Left;
            if (VerticalOffset < workArea.Top) VerticalOffset = workArea.Top;
            if (HorizontalOffset + popupWidth > workArea.Right)
                HorizontalOffset = workArea.Right - popupWidth;
            if (VerticalOffset + popupHeight > workArea.Bottom)
                VerticalOffset = workArea.Bottom - popupHeight;
        }
        else
        {
            // Fallback to primary screen center
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            HorizontalOffset = (screenWidth - PopupWidth) / 2;
            VerticalOffset = (screenHeight - PopupHeight) / 2;
        }

        _hasBeenPositioned = true;
    }

    /// <summary>
    /// Calculates responsive popup size based on the SizePreset from the DataContext.
    /// </summary>
    private (double width, double height) CalculateResponsiveSize(
        double windowWidth, double windowHeight, double dpiX, double dpiY)
    {
        // Check if DataContext implements IPanelableViewModel
        var sizePreset = (DataContext as IPanelableViewModel)?.SizePreset ?? PanelSizePreset.Custom;

        switch (sizePreset)
        {
            case PanelSizePreset.Compact:
                // Fixed compact size for narrow panels
                return (Math.Clamp(350, 300, 400), Math.Clamp(500, 400, 800));

            case PanelSizePreset.Medium:
                // Fixed medium size
                return (Math.Clamp(600, 500, 800), Math.Clamp(500, 400, 700));

            case PanelSizePreset.Large:
                // Responsive: ~60% width, ~70% height with constraints
                var largeWidth = Math.Clamp(windowWidth * 0.6, 600, 1200);
                var largeHeight = Math.Clamp(windowHeight * 0.7, 500, 900);
                return (largeWidth, largeHeight);

            case PanelSizePreset.Full:
                // Responsive: ~80% width, ~80% height with constraints
                var fullWidth = Math.Clamp(windowWidth * 0.8, 800, 1600);
                var fullHeight = Math.Clamp(windowHeight * 0.8, 600, 1000);
                return (fullWidth, fullHeight);

            case PanelSizePreset.Custom:
            default:
                // Use the existing PopupWidth/PopupHeight values
                return (PopupWidth, PopupHeight);
        }
    }

    public static readonly DependencyProperty PopupWidthProperty =
        DependencyProperty.Register("PopupWidth", typeof(double), typeof(DraggablePopup), new FrameworkPropertyMetadata(800.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double PopupWidth
    {
        get => (double)GetValue(PopupWidthProperty);
        set => SetValue(PopupWidthProperty, value);
    }

    public static readonly DependencyProperty PopupHeightProperty =
        DependencyProperty.Register("PopupHeight", typeof(double), typeof(DraggablePopup), new FrameworkPropertyMetadata(600.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double PopupHeight
    {
        get => (double)GetValue(PopupHeightProperty);
        set => SetValue(PopupHeightProperty, value);
    }

    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register("HorizontalOffset", typeof(double), typeof(DraggablePopup), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register("VerticalOffset", typeof(double), typeof(DraggablePopup), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register("Title", typeof(string), typeof(DraggablePopup), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register("CloseCommand", typeof(ICommand), typeof(DraggablePopup), new PropertyMetadata(null));

    public ICommand CloseCommand
    {
        get => (ICommand)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public static readonly DependencyProperty HeaderRightContentProperty =
        DependencyProperty.Register("HeaderRightContent", typeof(object), typeof(DraggablePopup), new PropertyMetadata(null));

    public object HeaderRightContent
    {
        get => GetValue(HeaderRightContentProperty);
        set => SetValue(HeaderRightContentProperty, value);
    }

    public static readonly DependencyProperty HeaderLeftContentProperty =
        DependencyProperty.Register("HeaderLeftContent", typeof(object), typeof(DraggablePopup), new PropertyMetadata(null));

    public object HeaderLeftContent
    {
        get => GetValue(HeaderLeftContentProperty);
        set => SetValue(HeaderLeftContentProperty, value);
    }

    public static readonly DependencyProperty PopupContentProperty =
        DependencyProperty.Register("PopupContent", typeof(object), typeof(DraggablePopup), new PropertyMetadata(null));

    public object PopupContent
    {
        get => GetValue(PopupContentProperty);
        set => SetValue(PopupContentProperty, value);
    }

    #endregion

    #region Event Handlers

    private void DragHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start drag if clicking on an interactive element (button, etc.)
        if (e.OriginalSource is System.Windows.DependencyObject source)
        {
            // Walk up the visual tree to check if we clicked on a Button
            var element = source;
            while (element != null)
            {
                if (element is System.Windows.Controls.Button)
                {
                    // Let the button handle the click, don't start dragging
                    return;
                }
                if (element is Border border && border.Name == "DragHeader")
                {
                    // Reached the drag header itself, safe to start drag
                    break;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
        }

        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void DragHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _dragStartPoint;

        HorizontalOffset += diff.X;
        VerticalOffset += diff.Y;

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void DragHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newWidth = PopupWidth + e.HorizontalChange;
        var newHeight = PopupHeight + e.VerticalChange;

        if (newWidth >= 400) PopupWidth = newWidth;
        if (newHeight >= 300) PopupHeight = newHeight;
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
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