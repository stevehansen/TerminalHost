using System.Windows;
using System.Windows.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class FileViewerView : UserControl
{
    public FileViewerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileViewerViewModel oldVm)
        {
            oldVm.ScrollToLineRequested -= OnScrollToLineRequested;
            oldVm.SetCaretIndexRequested -= OnSetCaretIndexRequested;
        }

        if (e.NewValue is FileViewerViewModel newVm)
        {
            newVm.ScrollToLineRequested += OnScrollToLineRequested;
            newVm.SetCaretIndexRequested += OnSetCaretIndexRequested;
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is FileViewerViewModel vm && vm.IsEditMode)
            {
                // Scroll text box to line
                var lineHeight = EditTextBox.FontSize * 1.4;
                EditTextBox.ScrollToVerticalOffset(lineNumber * lineHeight);
            }
            else if (DataContext is FileViewerViewModel { IsPreviewMode: true })
            {
                // Scroll preview to approximate position
                // FlowDocumentScrollViewer doesn't have direct line scrolling
                // so we approximate based on document length
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

    private void EditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync line numbers scroll with editor scroll
        LineNumbersScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void EditTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FileViewerViewModel vm)
        {
            vm.UpdateCursorInfo(EditTextBox.CaretIndex);
        }
    }

    private void SideBySideEditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync line numbers scroll with editor scroll in side-by-side mode
        SideBySideLineNumbersScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void SideBySideEditTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FileViewerViewModel vm)
        {
            vm.UpdateCursorInfo(SideBySideEditTextBox.CaretIndex);
        }
    }

    private void MarkdownViewer_LinkClicked(object? sender, string filePath)
    {
        if (DataContext is FileViewerViewModel vm)
        {
            vm.Open(filePath);
        }
    }
}
