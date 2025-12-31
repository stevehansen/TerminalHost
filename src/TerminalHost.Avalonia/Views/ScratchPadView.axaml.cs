using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class ScratchPadView : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private bool _isResizing;
    private Point _resizeStartPoint;
    private double _initialWidth;
    private double _initialHeight;

    public ScratchPadView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            if (DataContext is ScratchPadViewModel viewModel)
            {
                if (viewModel.CloseCommand.CanExecute(null))
                {
                    viewModel.CloseCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }

    private void ScratchPadHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            e.Handled = true;
        }
    }

    private void ScratchPadHeader_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = e.GetPosition(this);
        var diff = currentPos - _dragStartPoint;

        // In Avalonia, we need to move the parent window or container
        // This will be handled by the host window that contains this popup
        if (DataContext is ScratchPadViewModel viewModel)
        {
            // The actual window positioning will be handled by the MainWindow
            // or a dedicated popup window that hosts this view
        }

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void ScratchPadHeader_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Handled = true;
        }
    }

    private void ResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(this);

            if (DataContext is ScratchPadViewModel viewModel)
            {
                _initialWidth = viewModel.Width;
                _initialHeight = viewModel.Height;
            }

            e.Handled = true;
        }
    }

    private void ResizeGrip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;

        var currentPos = e.GetPosition(this);
        var diff = currentPos - _resizeStartPoint;

        if (DataContext is ScratchPadViewModel viewModel)
        {
            var newWidth = _initialWidth + diff.X;
            var newHeight = _initialHeight + diff.Y;

            if (newWidth >= 400) // Min width
            {
                viewModel.Width = newWidth;
            }
            if (newHeight >= 300) // Min height
            {
                viewModel.Height = newHeight;
            }
        }

        e.Handled = true;
    }

    private void ResizeGrip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            e.Handled = true;
        }
    }
}