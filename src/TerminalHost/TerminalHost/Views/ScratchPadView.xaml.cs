using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class ScratchPadView : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;

    public ScratchPadView()
    {
        InitializeComponent();
    }

    private void ScratchPadHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void ScratchPadHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _dragStartPoint;

        ScratchPadPopup.HorizontalOffset += diff.X;
        ScratchPadPopup.VerticalOffset += diff.Y;

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void ScratchPadHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void ScratchPadResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var viewModel = DataContext as ScratchPadViewModel;
        if (viewModel == null) return;

        var newWidth = viewModel.Width + e.HorizontalChange;
        var newHeight = viewModel.Height + e.VerticalChange;

        if (newWidth >= 400) // Min width
        {
            viewModel.Width = newWidth;
        }
        if (newHeight >= 300) // Min height
        {
            viewModel.Height = newHeight;
        }
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
}
