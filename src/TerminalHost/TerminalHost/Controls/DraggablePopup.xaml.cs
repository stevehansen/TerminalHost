using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TerminalHost.Controls;

public partial class DraggablePopup : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;

    public DraggablePopup()
    {
        InitializeComponent();
    }

    #region Dependency Properties

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register("IsOpen", typeof(bool), typeof(DraggablePopup), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
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