using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace TerminalHost.Controls;

public partial class DraggablePopup : UserControl
{
    public DraggablePopup()
    {
        InitializeComponent();

        // Wire up resize grip events
        var resizeGrip = this.FindControl<Thumb>("ResizeGrip");
        if (resizeGrip != null)
        {
            resizeGrip.DragDelta += ResizeGrip_DragDelta;
        }

        // Wire up keyboard events
        this.KeyDown += UserControl_KeyDown;
    }

    #region Avalonia Properties

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DraggablePopup, bool>(nameof(IsOpen), defaultValue: false, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
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

    private void ResizeGrip_DragDelta(object? sender, VectorEventArgs e)
    {
        var newWidth = PopupWidth + e.Vector.X;
        var newHeight = PopupHeight + e.Vector.Y;

        if (newWidth >= 400) PopupWidth = newWidth;
        if (newHeight >= 300) PopupHeight = newHeight;
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsOpen)
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