using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost;

/// <summary>
/// Scratch pad popup logic.
/// </summary>
public partial class MainWindow
{
    private bool _isDraggingScratchPad;
    private Point _scratchPadDragStart;
    private bool _isLoadingScratchPad;
    private System.Windows.Threading.DispatcherTimer? _scratchPadSaveTimer;

    private void ShowScratchPad()
    {
        // Determine if we have a project context
        var hasProject = _viewModel.SelectedTab is TerminalPairTabViewModel;

        if (!hasProject)
        {
            // No project selected, use global scratch pad
            ScratchPadGlobalRadio.IsChecked = true;
            ScratchPadProjectRadio.IsEnabled = false;
        }
        else
        {
            ScratchPadProjectRadio.IsEnabled = true;
            ScratchPadProjectRadio.IsChecked = true;
        }

        LoadScratchPadContent();

        // Center the popup on the window
        var windowPos = PointToScreen(new Point(0, 0));
        ScratchPadPopup.HorizontalOffset = windowPos.X + (ActualWidth - 600) / 2;
        ScratchPadPopup.VerticalOffset = windowPos.Y + (ActualHeight - 450) / 2;

        ScratchPadPopup.IsOpen = true;
        ScratchPadTextBox.Focus();
    }

    private void LoadScratchPadContent()
    {
        _isLoadingScratchPad = true;
        try
        {
            var config = _configService.Load();
            var isGlobal = ScratchPadGlobalRadio.IsChecked == true;

            if (isGlobal)
            {
                ScratchPadTextBox.Text = config.GlobalScratchPad;
                ScratchPadTitle.Text = "Scratch Pad (Global)";
                ScratchPadInfo.Text = "Shared across all projects";
            }
            else if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var path = NormalizePath(terminalTab.Pair.WorkingDirectory);
                var content = config.ScratchPads.TryGetValue(path, out var c) ? c : "";
                ScratchPadTextBox.Text = content;
                ScratchPadTitle.Text = $"Scratch Pad ({terminalTab.Title})";
                ScratchPadInfo.Text = terminalTab.Pair.WorkingDirectory;
            }
        }
        finally
        {
            _isLoadingScratchPad = false;
        }
    }

    private void SaveScratchPadContent()
    {
        if (_isLoadingScratchPad) return;

        var config = _configService.Load();
        var isGlobal = ScratchPadGlobalRadio.IsChecked == true;

        if (isGlobal)
        {
            config.GlobalScratchPad = ScratchPadTextBox.Text;
        }
        else if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            var path = NormalizePath(terminalTab.Pair.WorkingDirectory);
            config.ScratchPads[path] = ScratchPadTextBox.Text;
        }

        _configService.Save(config);
    }

    private static string NormalizePath(string path)
    {
        return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    private void ScratchPadScope_Changed(object sender, RoutedEventArgs e)
    {
        if (!ScratchPadPopup.IsOpen) return;
        LoadScratchPadContent();
    }

    private void ScratchPadTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingScratchPad) return;

        // Debounce saving - wait 500ms after last change
        _scratchPadSaveTimer?.Stop();
        _scratchPadSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _scratchPadSaveTimer.Tick += (s, args) =>
        {
            _scratchPadSaveTimer?.Stop();
            SaveScratchPadContent();
        };
        _scratchPadSaveTimer.Start();
    }

    private void ScratchPadClose_Click(object sender, RoutedEventArgs e)
    {
        // Save immediately on close
        SaveScratchPadContent();
        ScratchPadPopup.IsOpen = false;
    }

    private void ScratchPadHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingScratchPad = true;
        _scratchPadDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void ScratchPadHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingScratchPad) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _scratchPadDragStart;

        ScratchPadPopup.HorizontalOffset += diff.X;
        ScratchPadPopup.VerticalOffset += diff.Y;

        _scratchPadDragStart = currentPos;
        e.Handled = true;
    }

    private void ScratchPadHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingScratchPad)
        {
            _isDraggingScratchPad = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void ScratchPadResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newWidth = ScratchPadBorder.Width + e.HorizontalChange;
        var newHeight = ScratchPadBorder.Height + e.VerticalChange;

        if (newWidth >= ScratchPadBorder.MinWidth)
        {
            ScratchPadBorder.Width = newWidth;
        }
        if (newHeight >= ScratchPadBorder.MinHeight)
        {
            ScratchPadBorder.Height = newHeight;
        }
    }
}
