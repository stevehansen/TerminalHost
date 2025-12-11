using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TerminalHost;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigurationService _configService;
    private readonly SystemTrayService? _systemTrayService;
    private bool _isExiting;

    // Drag-and-drop tab reordering
    private System.Windows.Point _dragStartPoint;
    private ITabViewModel? _draggedTab;

    public MainWindow(MainViewModel viewModel, ConfigurationService configService, SystemTrayService? systemTrayService = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _systemTrayService = systemTrayService;
        DataContext = viewModel;

        RestoreWindowState();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;

        // Subscribe to view model property changes to sync column widths
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Subscribe to config reload events to update tray setting
        _viewModel.ConfigReloaded += OnConfigReloaded;

        // Subscribe to file preview events
        _viewModel.FilePreviewRequested += OnFilePreviewRequested;

        // Subscribe to help events
        _viewModel.HelpRequested += OnHelpRequested;
    }

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        if (_systemTrayService != null)
        {
            var config = _configService.Load();
            _systemTrayService.IsEnabled = config.Settings.ShowInSystemTray;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Minimize to tray when enabled
        if (WindowState == WindowState.Minimized && _systemTrayService?.IsEnabled == true)
        {
            Hide();
        }
    }

    public void ForceClose()
    {
        _isExiting = true;
        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Column widths are now bound in XAML, no code-behind sync needed
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new split ratio
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid to get actual column widths
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
            {
                var customWidth = grid.ColumnDefinitions[0].ActualWidth;
                var shellWidth = grid.ColumnDefinitions[2].ActualWidth;
                terminalTab.UpdateSplitRatioFromColumnWidths(customWidth, shellWidth);
            }
        }
    }

    private void RestoreWindowState()
    {
        var config = _configService.Load();
        var state = config.WindowState;

        // Validate position is on screen
        var left = state.Left;
        var top = state.Top;
        var width = Math.Max(400, state.Width);
        var height = Math.Max(300, state.Height);

        // Ensure window is visible on at least one monitor
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        if (left < virtualLeft || left > virtualLeft + virtualWidth - 100)
            left = 100;
        if (top < virtualTop || top > virtualTop + virtualHeight - 100)
            top = 100;

        Left = left;
        Top = top;
        Width = width;
        Height = height;

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        var config = _configService.Load();

        // Save window state (use restore bounds if maximized)
        if (WindowState == WindowState.Maximized)
        {
            config.WindowState.Left = RestoreBounds.Left;
            config.WindowState.Top = RestoreBounds.Top;
            config.WindowState.Width = RestoreBounds.Width;
            config.WindowState.Height = RestoreBounds.Height;
            config.WindowState.IsMaximized = true;
        }
        else
        {
            config.WindowState.Left = Left;
            config.WindowState.Top = Top;
            config.WindowState.Width = Width;
            config.WindowState.Height = Height;
            config.WindowState.IsMaximized = false;
        }

        _configService.Save(config);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // If tray is enabled and not explicitly exiting, minimize to tray instead of closing
        if (_systemTrayService?.IsEnabled == true && !_isExiting)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }

        SaveWindowState();
        _viewModel.Shutdown();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Escape: Close popups if open
        if (e.Key == Key.Escape)
        {
            if (FilePreviewPopup.IsOpen)
            {
                FilePreviewPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (HelpPopup.IsOpen)
            {
                HelpPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
        }

        // F1: Toggle help popup
        if (e.Key == Key.F1)
        {
            HelpPopup.IsOpen = !HelpPopup.IsOpen;
            e.Handled = true;
            return;
        }

        // Only handle shortcuts with modifiers - let plain Tab pass through to terminal
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            return; // Don't intercept unmodified keys
        }

        // Ctrl+PageDown: Next tab
        if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CycleTab(forward: true);
            e.Handled = true;
        }
        // Ctrl+PageUp: Previous tab
        else if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CycleTab(forward: false);
            e.Handled = true;
        }
        // Ctrl+1-9: Jump to specific tab
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < _viewModel.Tabs.Count)
            {
                _viewModel.SelectedTab = _viewModel.Tabs[index];
            }
            e.Handled = true;
        }
        // Ctrl+W: Close current tab
        else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_viewModel.SelectedTab != null)
            {
                _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab);
            }
            e.Handled = true;
        }
        // Ctrl+`: Switch between custom and shell terminal
        else if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control) // Oem3 is the ` key
        {
            _viewModel.SwitchActiveTerminalCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+N: New project
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenNewProjectCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+,: Open settings
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+P: Open profiles
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenProfilesCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+E: Open in Explorer
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenInExplorerCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+T: Open tab switcher
        else if (e.Key == Key.T && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowTabSwitcher();
            e.Handled = true;
        }
        // Ctrl+O: Open file preview
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenFilePreviewDialog();
            e.Handled = true;
        }
        // Check quick command shortcuts
        else if (TryExecuteQuickCommandShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private bool TryExecuteQuickCommandShortcut(Key key, ModifierKeys modifiers)
    {
        foreach (var command in _viewModel.QuickCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _viewModel.ExecuteQuickCommandCommand.Execute(command);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryParseShortcut(string shortcut, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        // Parse modifiers and key
        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();
            switch (upperPart)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                default:
                    // Try to parse as a Key
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (part.Length == 1 && char.IsLetter(part[0]))
                    {
                        // Single letter key (A-Z)
                        key = (Key)Enum.Parse(typeof(Key), part.ToUpperInvariant());
                    }
                    else if (part.Length == 1 && char.IsDigit(part[0]))
                    {
                        // Number key (0-9) - use D0-D9 for top row
                        key = (Key)Enum.Parse(typeof(Key), "D" + part);
                    }
                    break;
            }
        }

        return key != Key.None && modifiers != ModifierKeys.None;
    }

    private void CycleTab(bool forward)
    {
        if (_viewModel.Tabs.Count <= 1) return;

        var currentIndex = _viewModel.SelectedTab != null
            ? _viewModel.Tabs.IndexOf(_viewModel.SelectedTab)
            : 0;

        int newIndex;
        if (forward)
        {
            newIndex = (currentIndex + 1) % _viewModel.Tabs.Count;
        }
        else
        {
            newIndex = (currentIndex - 1 + _viewModel.Tabs.Count) % _viewModel.Tabs.Count;
        }

        _viewModel.SelectedTab = _viewModel.Tabs[newIndex];
    }

    public void BringToFront()
    {
        // Show window if hidden (minimized to tray)
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void TestTerminal_GotFocus(object sender, RoutedEventArgs e)
    {
        System.Console.WriteLine("[MainWindow] TestTerminal got focus");
    }

    private void TestTerminal_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        System.Console.WriteLine("[MainWindow] TestTerminal mouse down - focusing");
        TestTerminal.Focus();
    }

    #region Tab Drag-Drop and Middle-Click

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle-click to close tab
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
            {
                _viewModel.CloseTabCommand.Execute(tab);
                e.Handled = true;
            }
        }
    }

    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedTab = tab;
        }
    }

    private void Tab_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedTab == null)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        var diff = _dragStartPoint - currentPosition;

        // Check if moved enough to start drag
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            // Create drag data
            var dragData = new System.Windows.DataObject("TabViewModel", _draggedTab);
            DragDrop.DoDragDrop((DependencyObject)sender, dragData, System.Windows.DragDropEffects.Move);

            _draggedTab = null;
        }
    }

    private void Tab_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TabViewModel"))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;

        // Visual feedback - highlight drop target
        if (sender is Border border)
        {
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
            border.BorderThickness = new Thickness(2, 2, 2, 0);
        }
    }

    private void Tab_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        // Remove visual feedback
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }
    }

    private void Tab_Drop(object sender, System.Windows.DragEventArgs e)
    {
        // Remove visual feedback
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }

        if (!e.Data.GetDataPresent("TabViewModel"))
        {
            return;
        }

        var droppedTab = e.Data.GetData("TabViewModel") as ITabViewModel;
        if (droppedTab == null)
        {
            return;
        }

        // Get target tab
        if (sender is FrameworkElement element && element.DataContext is ITabViewModel targetTab)
        {
            if (droppedTab == targetTab)
            {
                return; // Dropped on itself
            }

            var oldIndex = _viewModel.Tabs.IndexOf(droppedTab);
            var newIndex = _viewModel.Tabs.IndexOf(targetTab);

            if (oldIndex >= 0 && newIndex >= 0)
            {
                _viewModel.Tabs.Move(oldIndex, newIndex);
            }
        }

        e.Handled = true;
    }

    #endregion

    #region Tab Overflow and Switcher

    private ScrollViewer? GetTabListScrollViewer()
    {
        if (TabList.Template?.FindName("ScrollViewer", TabList) is ScrollViewer sv)
            return sv;

        // Try to find it manually
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(TabList); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(TabList, i);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
            if (child is Border border && border.Child is ScrollViewer innerSv)
                return innerSv;
        }
        return null;
    }

    private void TabList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverflowButtonsVisibility();
    }

    private void UpdateOverflowButtonsVisibility()
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer == null)
        {
            // No scroll viewer found, show overflow buttons based on tab count
            var hasOverflow = _viewModel.Tabs.Count > 5;
            ScrollLeftButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            ScrollRightButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            TabDropdownButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var hasHorizontalOverflow = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth;
        ScrollLeftButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
        TabDropdownButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - 150);
        }
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + 150);
        }
    }

    private void TabDropdown_Click(object sender, RoutedEventArgs e)
    {
        // Populate and show dropdown
        DropdownTabList.ItemsSource = _viewModel.Tabs;
        DropdownTabList.SelectedItem = _viewModel.SelectedTab;
        DropdownSearchBox.Text = "";
        TabDropdownPopup.IsOpen = true;
        DropdownSearchBox.Focus();
    }

    private void DropdownSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = DropdownSearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            DropdownTabList.ItemsSource = _viewModel.Tabs;
        }
        else
        {
            var filtered = _viewModel.Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            DropdownTabList.ItemsSource = filtered;
        }
    }

    private void DropdownTabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DropdownTabList.SelectedItem is ITabViewModel tab)
        {
            _viewModel.SelectedTab = tab;
            TabDropdownPopup.IsOpen = false;
        }
    }

    #endregion

    #region Tab Switcher (Ctrl+Shift+T)

    private void ShowTabSwitcher()
    {
        // Populate and show switcher
        SwitcherTabList.ItemsSource = _viewModel.Tabs;
        SwitcherTabList.SelectedItem = _viewModel.SelectedTab;
        SwitcherSearchBox.Text = "";
        TabSwitcherPopup.IsOpen = true;
        SwitcherSearchBox.Focus();
    }

    private void SwitcherSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SwitcherSearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            SwitcherTabList.ItemsSource = _viewModel.Tabs;
        }
        else
        {
            var filtered = _viewModel.Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            SwitcherTabList.ItemsSource = filtered;
        }

        // Select first item if any
        if (SwitcherTabList.Items.Count > 0)
        {
            SwitcherTabList.SelectedIndex = 0;
        }
    }

    private void SwitcherSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (SwitcherTabList.SelectedIndex < SwitcherTabList.Items.Count - 1)
            {
                SwitcherTabList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (SwitcherTabList.SelectedIndex > 0)
            {
                SwitcherTabList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectSwitcherItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TabSwitcherPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void SwitcherTabList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectSwitcherItem();
    }

    private void SelectSwitcherItem()
    {
        if (SwitcherTabList.SelectedItem is ITabViewModel tab)
        {
            _viewModel.SelectedTab = tab;
            TabSwitcherPopup.IsOpen = false;
        }
    }

    #endregion

    #region File Preview

    private readonly FilePreviewService _filePreviewService = new();
    private string? _currentPreviewFilePath;
    private bool _isDraggingPreview;
    private System.Windows.Point _previewDragStart;

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        ShowFilePreview(e.FilePath, e.Line);
    }

    private void OpenFilePreviewDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Preview",
            Filter = "All Files (*.*)|*.*|Code Files (*.cs;*.js;*.ts;*.py;*.json;*.xml)|*.cs;*.js;*.ts;*.py;*.json;*.xml|Text Files (*.txt;*.md;*.log)|*.txt;*.md;*.log",
            FilterIndex = 1
        };

        // Set initial directory to current tab's working directory
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            dialog.InitialDirectory = terminalTab.Pair.WorkingDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            ShowFilePreview(dialog.FileName);
        }
    }

    public void ShowFilePreview(string filePath, int? highlightLine = null)
    {
        System.Console.WriteLine($"[FilePreview] ShowFilePreview called for: {filePath}");
        var result = _filePreviewService.LoadFilePreview(filePath, highlightLine);
        if (result == null)
        {
            System.Console.WriteLine("[FilePreview] LoadFilePreview returned null");
            return;
        }
        System.Console.WriteLine($"[FilePreview] Result: IsSuccess={result.IsSuccess}, Error={result.Error}");

        _currentPreviewFilePath = result.FilePath;

        if (result.IsSuccess)
        {
            FilePreviewTitle.Text = result.FileName;
            FilePreviewContent.Document = result.Document!;
            FilePreviewInfo.Text = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";

            if (highlightLine.HasValue && result.Document != null)
            {
                ScrollToLine(highlightLine.Value);
            }
        }
        else
        {
            FilePreviewTitle.Text = result.FileName;
            FilePreviewContent.Document = CreateErrorDocument(result.Error!);
            FilePreviewInfo.Text = $"Error • {FormatFileSize(result.FileSize)}";
        }

        // Center the popup on the window
        var windowPos = PointToScreen(new System.Windows.Point(0, 0));
        FilePreviewPopup.HorizontalOffset = windowPos.X + (ActualWidth - 900) / 2;
        FilePreviewPopup.VerticalOffset = windowPos.Y + (ActualHeight - 600) / 2;

        FilePreviewPopup.IsOpen = true;
    }

    private void FilePreviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPreview = true;
        _previewDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void FilePreviewHeader_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingPreview) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _previewDragStart;

        FilePreviewPopup.HorizontalOffset += diff.X;
        FilePreviewPopup.VerticalOffset += diff.Y;

        _previewDragStart = currentPos;
        e.Handled = true;
    }

    private void FilePreviewHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingPreview)
        {
            _isDraggingPreview = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void FilePreviewResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newWidth = FilePreviewBorder.Width + e.HorizontalChange;
        var newHeight = FilePreviewBorder.Height + e.VerticalChange;

        // Respect min constraints only - no max limit
        if (newWidth >= FilePreviewBorder.MinWidth)
        {
            FilePreviewBorder.Width = newWidth;
        }
        if (newHeight >= FilePreviewBorder.MinHeight)
        {
            FilePreviewBorder.Height = newHeight;
        }
    }

    private void ScrollToLine(int lineNumber)
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var scrollViewer = FilePreviewContent.Parent as ScrollViewer;
            if (scrollViewer != null && lineNumber > 20)
            {
                var approximateOffset = (lineNumber - 10) * 18;
                scrollViewer.ScrollToVerticalOffset(approximateOffset);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static System.Windows.Documents.FlowDocument CreateErrorDocument(string error)
    {
        var document = new System.Windows.Documents.FlowDocument
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };

        var paragraph = new System.Windows.Documents.Paragraph();
        paragraph.Inlines.Add(new System.Windows.Documents.Run(error)
        {
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF1, 0x48, 0x48))
        });
        document.Blocks.Add(paragraph);

        return document;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private void FilePreviewClose_Click(object sender, RoutedEventArgs e)
    {
        FilePreviewPopup.IsOpen = false;
    }

    private void FilePreviewOpenInEditor_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPreviewFilePath) && System.IO.File.Exists(_currentPreviewFilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _currentPreviewFilePath,
                    UseShellExecute = true
                });
                FilePreviewPopup.IsOpen = false;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[MainWindow] Failed to open file in editor: {ex.Message}");
            }
        }
    }

    #endregion

    #region Help Popup

    private void OnHelpRequested(object? sender, EventArgs e)
    {
        HelpPopup.IsOpen = true;
    }

    private void HelpClose_Click(object sender, RoutedEventArgs e)
    {
        HelpPopup.IsOpen = false;
    }

    #endregion
}
