using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FileViewerPopup : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;

    public FileViewerPopup()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        FileViewerPop.Opened += OnPopupOpened;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileViewerViewModel oldViewModel)
        {
            oldViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            oldViewModel.SetCaretIndexRequested -= OnSetCaretIndexRequested;
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is FileViewerViewModel newViewModel)
        {
            newViewModel.ScrollToLineRequested += OnScrollToLineRequested;
            newViewModel.SetCaretIndexRequested += OnSetCaretIndexRequested;
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When switching to edit mode, focus the editor
        if (e.PropertyName == nameof(FileViewerViewModel.Mode) && DataContext is FileViewerViewModel vm)
        {
            if (vm.IsEditMode)
            {
                Dispatcher.BeginInvoke(() => FocusEditor(), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        // When popup opens, focus the appropriate control
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is FileViewerViewModel vm && vm.IsEditMode)
            {
                FocusEditor();
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void FocusEditor()
    {
        EditTextBox.Focus();
        Keyboard.Focus(EditTextBox);
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is FileViewerViewModel { IsEditMode: true })
            {
                EditTextBox.ScrollToLine(Math.Max(0, lineNumber));
                EditTextBox.Focus();
            }
        });
    }

    private void OnSetCaretIndexRequested(object? sender, int caretIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            EditTextBox.Focus();
            EditTextBox.CaretIndex = Math.Min(caretIndex, EditTextBox.Text.Length);
        });
    }

    #region Drag handling

    private void DragHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not FileViewerViewModel) return;

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

        if (DataContext is FileViewerViewModel viewModel)
        {
            viewModel.HorizontalOffset += diff.X;
            viewModel.VerticalOffset += diff.Y;
        }

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

    #endregion

    #region Resize handling

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not FileViewerViewModel viewModel) return;

        var newWidth = viewModel.Width + e.HorizontalChange;
        var newHeight = viewModel.Height + e.VerticalChange;

        if (newWidth >= 600) viewModel.Width = newWidth;
        if (newHeight >= 450) viewModel.Height = newHeight;
    }

    #endregion

    #region Editor handling

    private void EditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        LineNumbersScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void EditTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCursorInfo();
    }

    private void EditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not FileViewerViewModel viewModel) return;

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
            viewModel.GoToLineDialogCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            // Defer update to ensure caret has moved
            Dispatcher.BeginInvoke(UpdateCursorInfo, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void UpdateCursorInfo()
    {
        if (DataContext is FileViewerViewModel viewModel)
        {
            viewModel.UpdateCursorInfo(EditTextBox.CaretIndex);
        }
    }

    private void EditTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Ensure the TextBox is properly focused for keyboard input
        Keyboard.Focus(EditTextBox);
    }

    #endregion

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is FileViewerViewModel viewModel)
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
