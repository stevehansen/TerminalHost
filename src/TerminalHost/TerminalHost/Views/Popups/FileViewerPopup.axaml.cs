using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FileViewerPopup : UserControl
{
    public FileViewerPopup()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is FileViewerViewModel oldViewModel)
        {
            oldViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            oldViewModel.SetCaretIndexRequested -= OnSetCaretIndexRequested;
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (DataContext is FileViewerViewModel newViewModel)
        {
            newViewModel.ScrollToLineRequested += OnScrollToLineRequested;
            newViewModel.SetCaretIndexRequested += OnSetCaretIndexRequested;
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Hook up scroll synchronization for Edit mode
        var editTextBox = this.FindControl<TextBox>("EditTextBox");
        if (editTextBox != null)
        {
            var scrollViewer = editTextBox.FindDescendantOfType<ScrollViewer>();
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += EditTextBox_ScrollChanged;
            }

            editTextBox.PropertyChanged += EditTextBox_PropertyChanged;
            editTextBox.KeyDown += EditTextBox_KeyDown;
            editTextBox.GotFocus += EditTextBox_GotFocus;
        }

        // Hook up scroll synchronization for Side-by-Side mode
        var sideBySideTextBox = this.FindControl<TextBox>("SideBySideEditTextBox");
        if (sideBySideTextBox != null)
        {
            var scrollViewer = sideBySideTextBox.FindDescendantOfType<ScrollViewer>();
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += SideBySideEditTextBox_ScrollChanged;
            }

            sideBySideTextBox.PropertyChanged += SideBySideEditTextBox_PropertyChanged;
            sideBySideTextBox.KeyDown += SideBySideEditTextBox_KeyDown;
            sideBySideTextBox.GotFocus += SideBySideEditTextBox_GotFocus;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Unhook Edit mode events
        var editTextBox = this.FindControl<TextBox>("EditTextBox");
        if (editTextBox != null)
        {
            var scrollViewer = editTextBox.FindDescendantOfType<ScrollViewer>();
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged -= EditTextBox_ScrollChanged;
            }

            editTextBox.PropertyChanged -= EditTextBox_PropertyChanged;
            editTextBox.KeyDown -= EditTextBox_KeyDown;
            editTextBox.GotFocus -= EditTextBox_GotFocus;
        }

        // Unhook Side-by-Side mode events
        var sideBySideTextBox = this.FindControl<TextBox>("SideBySideEditTextBox");
        if (sideBySideTextBox != null)
        {
            var scrollViewer = sideBySideTextBox.FindDescendantOfType<ScrollViewer>();
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged -= SideBySideEditTextBox_ScrollChanged;
            }

            sideBySideTextBox.PropertyChanged -= SideBySideEditTextBox_PropertyChanged;
            sideBySideTextBox.KeyDown -= SideBySideEditTextBox_KeyDown;
            sideBySideTextBox.GotFocus -= SideBySideEditTextBox_GotFocus;
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
                Dispatcher.UIThread.Post(() => FocusEditor(), DispatcherPriority.Input);
            }
        }
    }

    private void FocusEditor()
    {
        var editTextBox = this.FindControl<TextBox>("EditTextBox") ??
                         this.FindControl<TextBox>("SideBySideEditTextBox");
        if (editTextBox != null)
        {
            editTextBox.Focus();
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is FileViewerViewModel { IsEditMode: true })
            {
                var editTextBox = this.FindControl<TextBox>("EditTextBox") ??
                                 this.FindControl<TextBox>("SideBySideEditTextBox");
                if (editTextBox != null)
                {
                    // Calculate approximate line position
                    var lines = editTextBox.Text?.Split('\n') ?? Array.Empty<string>();
                    var targetLine = Math.Max(0, Math.Min(lineNumber, lines.Length - 1));
                    var caretIndex = 0;
                    for (int i = 0; i < targetLine; i++)
                    {
                        caretIndex += lines[i].Length + 1; // +1 for newline
                    }

                    editTextBox.CaretIndex = caretIndex;
                    editTextBox.Focus();
                }
            }
        });
    }

    private void OnSetCaretIndexRequested(object? sender, int caretIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var editTextBox = this.FindControl<TextBox>("EditTextBox") ??
                             this.FindControl<TextBox>("SideBySideEditTextBox");
            if (editTextBox != null)
            {
                editTextBox.Focus();
                editTextBox.CaretIndex = Math.Min(caretIndex, editTextBox.Text?.Length ?? 0);
            }
        });
    }

    #region Editor handling (Edit mode)

    private void EditTextBox_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Sync line numbers scroll with editor scroll
        var lineNumbersScroll = this.FindControl<ScrollViewer>("EditLineNumbersScroll");
        if (lineNumbersScroll != null && sender is ScrollViewer sourceScroll)
        {
            lineNumbersScroll.Offset = new Vector(lineNumbersScroll.Offset.X, sourceScroll.Offset.Y);
        }
    }

    private void EditTextBox_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty)
        {
            UpdateCursorInfo();
        }
    }

    private void EditTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileViewerViewModel viewModel) return;

        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            if (viewModel.SaveCommand.CanExecute(null))
            {
                viewModel.SaveCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control)
        {
            viewModel.GoToLineDialogCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            // Defer update to ensure caret has moved
            Dispatcher.UIThread.Post(UpdateCursorInfo, DispatcherPriority.Background);
        }
    }

    private void UpdateCursorInfo()
    {
        if (DataContext is FileViewerViewModel viewModel)
        {
            var editTextBox = this.FindControl<TextBox>("EditTextBox");
            if (editTextBox != null)
            {
                viewModel.UpdateCursorInfo(editTextBox.CaretIndex);
            }
        }
    }

    private void EditTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
        }
    }

    #endregion

    #region Side-by-Side editor handling

    private void SideBySideEditTextBox_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Sync line numbers scroll with editor scroll
        var lineNumbersScroll = this.FindControl<ScrollViewer>("SideBySideLineNumbersScroll");
        if (lineNumbersScroll != null && sender is ScrollViewer sourceScroll)
        {
            lineNumbersScroll.Offset = new Vector(lineNumbersScroll.Offset.X, sourceScroll.Offset.Y);
        }
    }

    private void SideBySideEditTextBox_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty)
        {
            if (DataContext is FileViewerViewModel viewModel && sender is TextBox textBox)
            {
                viewModel.UpdateCursorInfo(textBox.CaretIndex);
            }
        }
    }

    private void SideBySideEditTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not FileViewerViewModel viewModel) return;

        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            if (viewModel.SaveCommand.CanExecute(null))
            {
                viewModel.SaveCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control)
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
                Dispatcher.UIThread.Post(() =>
                {
                    viewModel.UpdateCursorInfo(textBox.CaretIndex);
                }, DispatcherPriority.Background);
            }
        }
    }

    private void SideBySideEditTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
        }
    }

    #endregion
}
