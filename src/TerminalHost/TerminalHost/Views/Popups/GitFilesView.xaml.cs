using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitFilesView : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;

    public GitFilesView()
    {
        InitializeComponent();
    }

    private void GitFilesHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var viewModel = DataContext as GitFilesViewModel;
        if (viewModel == null) return;

        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void GitFilesHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _dragStartPoint;

        // Update ViewModel's offset properties
        var viewModel = DataContext as GitFilesViewModel;
        if (viewModel != null)
        {
            viewModel.HorizontalOffset += diff.X;
            viewModel.VerticalOffset += diff.Y;
        }

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void GitFilesHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void GitFilesResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var viewModel = DataContext as GitFilesViewModel;
        if (viewModel == null) return;

        var newWidth = viewModel.Width + e.HorizontalChange;
        var newHeight = viewModel.Height + e.VerticalChange;

        // Apply min/max constraints if necessary (from ViewModel or hardcoded)
        if (newWidth >= 500) // MinWidth from XAML
        {
            viewModel.Width = newWidth;
        }
        if (newHeight >= 400) // MinHeight from XAML
        {
            viewModel.Height = newHeight;
        }
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is GitFilesViewModel viewModel)
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
