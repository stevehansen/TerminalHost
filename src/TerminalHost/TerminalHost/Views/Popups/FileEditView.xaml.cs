using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FileEditView : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;

    public FileEditView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileEditViewModel oldViewModel)
        {
            oldViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            oldViewModel.SetCaretIndexRequested -= OnSetCaretIndexRequested;
        }

        if (e.NewValue is FileEditViewModel newViewModel)
        {
            newViewModel.ScrollToLineRequested += OnScrollToLineRequested;
            newViewModel.SetCaretIndexRequested += OnSetCaretIndexRequested;
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        FileEditTextBox.ScrollToLine(lineNumber);
        FileEditTextBox.Focus();
    }

    private void OnSetCaretIndexRequested(object? sender, int caretIndex)
    {
        FileEditTextBox.CaretIndex = caretIndex;
        FileEditTextBox.Focus();
    }

    private void FileEditHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var viewModel = DataContext as FileEditViewModel;
        if (viewModel == null) return;

        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void FileEditHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _dragStartPoint;

        var viewModel = DataContext as FileEditViewModel;
        if (viewModel != null)
        {
            viewModel.HorizontalOffset += diff.X;
            viewModel.VerticalOffset += diff.Y;
        }

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void FileEditHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void FileEditResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var viewModel = DataContext as FileEditViewModel;
        if (viewModel == null) return;

        var newWidth = viewModel.Width + e.HorizontalChange;
        var newHeight = viewModel.Height + e.VerticalChange;

        if (newWidth >= 500) viewModel.Width = newWidth;
        if (newHeight >= 400) viewModel.Height = newHeight;
    }

    private void FileEditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        LineNumberScroller.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void FileEditTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCursorInfo();
    }

    private void FileEditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = DataContext as FileEditViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (viewModel.SaveCommand.CanExecute(null))
            {
                viewModel.SaveCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            viewModel.ShowGoToLineDialogCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            // Defer update to ensure caret has moved
            Dispatcher.BeginInvoke(new Action(UpdateCursorInfo), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void UpdateCursorInfo()
    {
        var viewModel = DataContext as FileEditViewModel;
        if (viewModel != null)
        {
            viewModel.UpdateCursorInfo(FileEditTextBox.CaretIndex);
        }
    }
}
