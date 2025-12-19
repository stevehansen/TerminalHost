using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FileViewerPopup : UserControl
{
    public FileViewerPopup()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        if (DataContext is not FileViewerViewModel vm) return;

        // When switching to edit/side-by-side mode or opening, focus the editor
        if (e.PropertyName == nameof(FileViewerViewModel.Mode) ||
            e.PropertyName == nameof(FileViewerViewModel.IsOpen))
        {
            if ((vm.IsEditMode || vm.IsSideBySideMode) && vm.IsOpen)
            {
                Dispatcher.BeginInvoke(() => FocusEditor(), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
    }

    private void FocusEditor()
    {
        var editTextBox = FindVisualChild<TextBox>(this);
        if (editTextBox != null)
        {
            editTextBox.Focus();
            Keyboard.Focus(editTextBox);
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is FileViewerViewModel { IsEditMode: true })
            {
                var editTextBox = FindVisualChild<TextBox>(this);
                if (editTextBox != null)
                {
                    editTextBox.ScrollToLine(Math.Max(0, lineNumber));
                    editTextBox.Focus();
                }
            }
        });
    }

    private void OnSetCaretIndexRequested(object? sender, int caretIndex)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var editTextBox = FindVisualChild<TextBox>(this);
            if (editTextBox != null)
            {
                editTextBox.Focus();
                editTextBox.CaretIndex = Math.Min(caretIndex, editTextBox.Text.Length);
            }
        });
    }

    #region Editor handling

    private void EditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Find the ScrollViewer that holds the line numbers (it's the first ScrollViewer we find, hopefully, 
        // or we can search by a property if we tag it, but standard traversal usually works if layout is simple)
        // Actually, we have two ScrollViewers. One for Text and one for LineNumbers.
        // We can distinguish them by HorizontalScrollBarVisibility.
        
        var scrollConsumers = FindVisualChildren<ScrollViewer>(this).ToList();
        var lineNumbersScroll = scrollConsumers.FirstOrDefault(s => s.HorizontalScrollBarVisibility == ScrollBarVisibility.Hidden);

        lineNumbersScroll?.ScrollToVerticalOffset(e.VerticalOffset);
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
            var editTextBox = FindVisualChild<TextBox>(this);
            if (editTextBox != null)
            {
                viewModel.UpdateCursorInfo(editTextBox.CaretIndex);
            }
        }
    }

    private void EditTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            Keyboard.Focus(textBox);
        }
    }

    #endregion

    #region Side-by-Side editor handling

    private void SideBySideEditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Find line numbers ScrollViewer by Tag and sync scroll
        var lineNumbersScroll = FindVisualChildren<ScrollViewer>(this)
            .FirstOrDefault(s => s.Tag?.ToString() == "SideBySideLineNumbers");
        lineNumbersScroll?.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void SideBySideEditTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FileViewerViewModel viewModel && sender is TextBox textBox)
        {
            viewModel.UpdateCursorInfo(textBox.CaretIndex);
        }
    }

    private void SideBySideEditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
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
            if (sender is TextBox textBox)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    viewModel.UpdateCursorInfo(textBox.CaretIndex);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }

    private void SideBySideEditTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            Keyboard.Focus(textBox);
        }
    }

    #endregion

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }
    
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T)
                {
                    yield return (T)child;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
}
