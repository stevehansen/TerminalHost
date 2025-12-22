using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class FileViewerView : UserControl
{
    private FileViewerViewModel? _viewModel;

    public FileViewerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Set up event handlers for TextBox controls
        if (EditTextBox != null)
        {
            EditTextBox.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(offset =>
                {
                    if (LineNumbersScroll != null)
                    {
                        LineNumbersScroll.Offset = new Vector(LineNumbersScroll.Offset.X, offset.Y);
                    }
                });

            EditTextBox.GetObservable(TextBox.CaretIndexProperty)
                .Subscribe(caretIndex =>
                {
                    if (_viewModel != null)
                    {
                        _viewModel.UpdateCursorInfo(caretIndex);
                    }
                });
        }

        if (SideBySideEditTextBox != null)
        {
            SideBySideEditTextBox.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(offset =>
                {
                    if (SideBySideLineNumbersScroll != null)
                    {
                        SideBySideLineNumbersScroll.Offset = new Vector(SideBySideLineNumbersScroll.Offset.X, offset.Y);
                    }
                });

            SideBySideEditTextBox.GetObservable(TextBox.CaretIndexProperty)
                .Subscribe(caretIndex =>
                {
                    if (_viewModel != null)
                    {
                        _viewModel.UpdateCursorInfo(caretIndex);
                    }
                });
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            _viewModel.SetCaretIndexRequested -= OnSetCaretIndexRequested;
        }

        _viewModel = DataContext as FileViewerViewModel;

        if (_viewModel != null)
        {
            _viewModel.ScrollToLineRequested += OnScrollToLineRequested;
            _viewModel.SetCaretIndexRequested += OnSetCaretIndexRequested;
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel != null && _viewModel.IsEditMode)
            {
                // Scroll text box to line
                var lineHeight = EditTextBox?.FontSize ?? 12;
                var targetOffset = lineNumber * lineHeight * 1.4;

                if (EditTextBox != null)
                {
                    EditTextBox.ScrollToLine(lineNumber);
                }
            }
            else if (_viewModel is { IsPreviewMode: true })
            {
                // Scroll preview to approximate position
                // ScrollViewer doesn't have direct line scrolling
                // so we approximate based on document length
                if (PreviewViewer != null)
                {
                    var lineHeight = 14.0; // Approximate line height
                    var targetOffset = lineNumber * lineHeight;
                    PreviewViewer.Offset = new Vector(PreviewViewer.Offset.X, targetOffset);
                }
            }
        });
    }

    private void OnSetCaretIndexRequested(object? sender, int caretIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (EditTextBox != null)
            {
                EditTextBox.Focus();
                var text = EditTextBox.Text ?? string.Empty;
                EditTextBox.CaretIndex = Math.Min(caretIndex, text.Length);
            }
        });
    }
}
