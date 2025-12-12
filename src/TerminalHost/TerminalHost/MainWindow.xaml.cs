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
        _viewModel.ScratchPadRequested += (_, _) => ShowScratchPad();
        _viewModel.GitChangesRequested += (_, _) => ShowGitFiles();
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
            if (GitFilesPopup.IsOpen)
            {
                GitFilesPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (ScratchPadPopup.IsOpen)
            {
                SaveScratchPadContent();
                ScratchPadPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (FileEditPopup.IsOpen)
            {
                CloseFileEdit();
                e.Handled = true;
                return;
            }
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
        // Ctrl+Shift+E: Open file edit dialog
        else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenFileEditDialog();
            e.Handled = true;
        }
        // Ctrl+Shift+P: Open command palette
        else if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowCommandPalette();
            e.Handled = true;
        }
        // Ctrl+Shift+N: Open scratch pad
        else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowScratchPad();
            e.Handled = true;
        }
        // Ctrl+G: Open git files panel
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowGitFiles();
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
            FilePreviewPopup.IsOpen = false;
            ShowFileEdit(_currentPreviewFilePath);
        }
    }

    private void OpenFileEditDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Edit",
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
            ShowFileEdit(dialog.FileName);
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

    #region File Edit

    private readonly FileEditService _fileEditService = new();
    private string? _currentEditFilePath;
    private System.Text.Encoding? _currentEditEncoding;
    private string? _originalContent;
    private bool _isFileModified;
    private bool _isDraggingEdit;
    private System.Windows.Point _editDragStart;

    public void ShowFileEdit(string filePath, int? goToLine = null)
    {
        System.Console.WriteLine($"[FileEdit] ShowFileEdit called for: {filePath}");
        var result = _fileEditService.LoadFile(filePath);

        if (!result.IsSuccess)
        {
            System.Windows.MessageBox.Show(
                result.Error ?? "Unknown error loading file",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _currentEditFilePath = result.FilePath;
        _currentEditEncoding = result.Encoding;
        _originalContent = result.Content;
        _isFileModified = false;

        FileEditTitle.Text = result.FileName;
        FileEditModifiedIndicator.Visibility = Visibility.Collapsed;
        FileEditTextBox.Text = result.Content;
        FileEditInfo.Text = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";

        if (result.IsReadOnly)
        {
            FileEditInfo.Text += " • Read-only";
            FileEditSaveButton.IsEnabled = false;
        }
        else
        {
            FileEditSaveButton.IsEnabled = true;
        }

        UpdateLineNumbers();
        UpdateCursorInfo();

        // Center the popup on the window
        var windowPos = PointToScreen(new System.Windows.Point(0, 0));
        FileEditPopup.HorizontalOffset = windowPos.X + (ActualWidth - 1000) / 2;
        FileEditPopup.VerticalOffset = windowPos.Y + (ActualHeight - 700) / 2;

        FileEditPopup.IsOpen = true;
        FileEditTextBox.Focus();

        // Go to specific line if requested
        if (goToLine.HasValue)
        {
            GoToLine(goToLine.Value);
        }
    }

    private void GoToLine(int lineNumber)
    {
        var text = FileEditTextBox.Text;
        var lines = text.Split('\n');
        var targetLine = Math.Max(0, Math.Min(lineNumber - 1, lines.Length - 1));

        int charIndex = 0;
        for (int i = 0; i < targetLine; i++)
        {
            charIndex += lines[i].Length + 1; // +1 for newline
        }

        FileEditTextBox.CaretIndex = charIndex;
        FileEditTextBox.ScrollToLine(targetLine);
        FileEditTextBox.Focus();
    }

    private void UpdateLineNumbers()
    {
        var lineCount = FileEditTextBox.Text.Split('\n').Length;
        var lineNumbers = new System.Text.StringBuilder();
        for (int i = 1; i <= lineCount; i++)
        {
            lineNumbers.AppendLine(i.ToString());
        }
        FileEditLineNumbers.Text = lineNumbers.ToString().TrimEnd();
    }

    private void UpdateCursorInfo()
    {
        var text = FileEditTextBox.Text;
        var caretIndex = FileEditTextBox.CaretIndex;

        // Calculate line and column
        var textUpToCaret = text.Substring(0, Math.Min(caretIndex, text.Length));
        var line = textUpToCaret.Count(c => c == '\n') + 1;
        var lastNewline = textUpToCaret.LastIndexOf('\n');
        var column = lastNewline < 0 ? caretIndex + 1 : caretIndex - lastNewline;

        FileEditCursorInfo.Text = $"Ln {line}, Col {column}";
    }

    private void FileEditTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineNumbers();

        // Check if content has changed
        _isFileModified = FileEditTextBox.Text != _originalContent;
        FileEditModifiedIndicator.Visibility = _isFileModified ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FileEditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync line number scroll with text editor scroll
        LineNumberScroller.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void FileEditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+S to save
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveCurrentFile();
            e.Handled = true;
        }
        // Ctrl+G to go to line
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowGoToLineDialog();
            e.Handled = true;
        }
        // Update cursor info on navigation keys
        else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            Dispatcher.BeginInvoke(new System.Action(UpdateCursorInfo), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ShowGoToLineDialog()
    {
        var lineCount = FileEditTextBox.Text.Split('\n').Length;
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"Enter line number (1-{lineCount}):",
            "Go to Line",
            "1");

        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out var lineNumber))
        {
            GoToLine(lineNumber);
        }
    }

    private void SaveCurrentFile()
    {
        if (string.IsNullOrEmpty(_currentEditFilePath))
            return;

        var result = _fileEditService.SaveFile(_currentEditFilePath, FileEditTextBox.Text, _currentEditEncoding);

        if (result.Success)
        {
            _originalContent = FileEditTextBox.Text;
            _isFileModified = false;
            FileEditModifiedIndicator.Visibility = Visibility.Collapsed;

            // Update file info
            var fileInfo = new System.IO.FileInfo(_currentEditFilePath);
            var lineCount = FileEditTextBox.Text.Split('\n').Length;
            FileEditInfo.Text = $"{lineCount:N0} lines • {FormatFileSize(fileInfo.Length)} • Saved";
        }
        else
        {
            System.Windows.MessageBox.Show(
                result.Error ?? "Unknown error saving file",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FileEditSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentFile();
    }

    private void FileEditReload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentEditFilePath))
            return;

        if (_isFileModified)
        {
            var result = System.Windows.MessageBox.Show(
                "You have unsaved changes. Reload and lose changes?",
                "Confirm Reload",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        var editResult = _fileEditService.ReloadFile(_currentEditFilePath);
        if (editResult.IsSuccess)
        {
            _originalContent = editResult.Content;
            FileEditTextBox.Text = editResult.Content;
            _isFileModified = false;
            FileEditModifiedIndicator.Visibility = Visibility.Collapsed;
            FileEditInfo.Text = $"{editResult.LineCount:N0} lines • {FormatFileSize(editResult.FileSize)} • Reloaded";
        }
        else
        {
            System.Windows.MessageBox.Show(
                editResult.Error ?? "Unknown error reloading file",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FileEditClose_Click(object sender, RoutedEventArgs e)
    {
        CloseFileEdit();
    }

    private void CloseFileEdit()
    {
        if (_isFileModified)
        {
            var result = System.Windows.MessageBox.Show(
                "You have unsaved changes. Close without saving?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        FileEditPopup.IsOpen = false;
        _currentEditFilePath = null;
        _currentEditEncoding = null;
        _originalContent = null;
        _isFileModified = false;
    }

    private void FileEditHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingEdit = true;
        _editDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void FileEditHeader_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingEdit) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _editDragStart;

        FileEditPopup.HorizontalOffset += diff.X;
        FileEditPopup.VerticalOffset += diff.Y;

        _editDragStart = currentPos;
        e.Handled = true;
    }

    private void FileEditHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingEdit)
        {
            _isDraggingEdit = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void FileEditResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var newWidth = FileEditBorder.Width + e.HorizontalChange;
        var newHeight = FileEditBorder.Height + e.VerticalChange;

        if (newWidth >= FileEditBorder.MinWidth)
        {
            FileEditBorder.Width = newWidth;
        }
        if (newHeight >= FileEditBorder.MinHeight)
        {
            FileEditBorder.Height = newHeight;
        }
    }

    #endregion

    #region Command Palette

    private List<PaletteCommand> _paletteCommands = new();

    private void InitializeCommandPalette()
    {
        _paletteCommands = new List<PaletteCommand>
        {
            // Tab/Project commands
            new PaletteCommand
            {
                Id = "new-project",
                Name = "New Project",
                Description = "Open folder as new project",
                Shortcut = "Ctrl+N",
                Icon = "📁",
                Category = "Project",
                Execute = () => _viewModel.OpenNewProjectCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "close-tab",
                Name = "Close Tab",
                Description = "Close current tab",
                Shortcut = "Ctrl+W",
                Icon = "✕",
                Category = "Tab",
                Execute = () => { if (_viewModel.SelectedTab != null) _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab); }
            },
            new PaletteCommand
            {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "🔍",
                Category = "Tab",
                Execute = ShowTabSwitcher
            },

            // File commands
            new PaletteCommand
            {
                Id = "file-preview",
                Name = "Preview File",
                Description = "Open file preview",
                Shortcut = "Ctrl+O",
                Icon = "👁",
                Category = "File",
                Execute = OpenFilePreviewDialog
            },
            new PaletteCommand
            {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                Execute = OpenFileEditDialog
            },
            new PaletteCommand
            {
                Id = "open-explorer",
                Name = "Open in Explorer",
                Description = "Open folder in file explorer",
                Shortcut = "Ctrl+E",
                Icon = "📂",
                Category = "File",
                Execute = () => _viewModel.OpenInExplorerCommand.Execute(null),
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },

            // Terminal commands
            new PaletteCommand
            {
                Id = "switch-terminal",
                Name = "Switch Terminal",
                Description = "Toggle between custom and shell",
                Shortcut = "Ctrl+`",
                Icon = "⇄",
                Category = "Terminal",
                Execute = () => _viewModel.SwitchActiveTerminalCommand.Execute(null),
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },

            // Settings
            new PaletteCommand
            {
                Id = "settings",
                Name = "Settings",
                Description = "Open settings editor",
                Shortcut = "Ctrl+,",
                Icon = "⚙️",
                Category = "Settings",
                Execute = () => _viewModel.OpenSettingsCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "profiles",
                Name = "Profiles",
                Description = "Manage terminal profiles",
                Shortcut = "Ctrl+P",
                Icon = "👤",
                Category = "Settings",
                Execute = () => _viewModel.OpenProfilesCommand.Execute(null)
            },

            // Help
            new PaletteCommand
            {
                Id = "help",
                Name = "Help",
                Description = "Show keyboard shortcuts",
                Shortcut = "F1",
                Icon = "❓",
                Category = "Help",
                Execute = () => HelpPopup.IsOpen = true
            },

            // Scratch Pad
            new PaletteCommand
            {
                Id = "scratch-pad",
                Name = "Scratch Pad",
                Description = "Open notes panel",
                Shortcut = "Ctrl+Shift+N",
                Icon = "📝",
                Category = "Tools",
                Execute = ShowScratchPad
            },

            // Git
            new PaletteCommand
            {
                Id = "git-changes",
                Name = "Git Changes",
                Description = "View modified files and diffs",
                Shortcut = "Ctrl+G",
                Icon = "📋",
                Category = "Git",
                Execute = ShowGitFiles,
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            }
        };
    }

    private void ShowCommandPalette()
    {
        if (_paletteCommands.Count == 0)
        {
            InitializeCommandPalette();
        }

        // Filter commands based on CanExecute
        var availableCommands = _paletteCommands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .ToList();

        PaletteCommandList.ItemsSource = availableCommands;
        PaletteSearchBox.Text = "";

        if (availableCommands.Any())
        {
            PaletteCommandList.SelectedIndex = 0;
        }

        CommandPalettePopup.IsOpen = true;
        PaletteSearchBox.Focus();
    }

    private void PaletteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = PaletteSearchBox.Text?.ToLower() ?? "";

        var filtered = _paletteCommands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .Where(c =>
                c.Name.ToLower().Contains(searchText) ||
                (c.Description?.ToLower().Contains(searchText) ?? false) ||
                c.Category.ToLower().Contains(searchText))
            .ToList();

        PaletteCommandList.ItemsSource = filtered;

        if (filtered.Any())
        {
            PaletteCommandList.SelectedIndex = 0;
        }
    }

    private void PaletteSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (PaletteCommandList.SelectedIndex < PaletteCommandList.Items.Count - 1)
            {
                PaletteCommandList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteCommandList.SelectedIndex > 0)
            {
                PaletteCommandList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedPaletteCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommandPalettePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void PaletteCommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedPaletteCommand();
    }

    private void ExecuteSelectedPaletteCommand()
    {
        if (PaletteCommandList.SelectedItem is PaletteCommand command)
        {
            CommandPalettePopup.IsOpen = false;
            command.Execute();
        }
    }

    #endregion

    #region Scratch Pad

    private bool _isDraggingScratchPad;
    private System.Windows.Point _scratchPadDragStart;
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
        var windowPos = PointToScreen(new System.Windows.Point(0, 0));
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

    private void ScratchPadHeader_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
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

    private void ScratchPadResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
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

    #endregion

    #region Git Files

    private readonly GitStatusService _gitStatusService = new();
    private List<Domain.GitFileStatus> _gitFiles = new();
    private string? _currentGitWorkingDirectory;
    private Domain.GitFileStatus? _selectedGitFile;
    private bool _isDraggingGitFiles;
    private System.Windows.Point _gitFilesDragStart;

    private async void ShowGitFiles()
    {
        // Get current working directory from selected terminal tab
        if (_viewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            System.Windows.MessageBox.Show(
                "Please select a project tab first.",
                "Git Changes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _currentGitWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        GitFilesTitle.Text = $"Git Changes - {terminalTab.Title}";
        GitFilesInfo.Text = _currentGitWorkingDirectory;

        // Load git files
        await RefreshGitFiles();

        // Center the popup on the window
        var windowPos = PointToScreen(new System.Windows.Point(0, 0));
        GitFilesPopup.HorizontalOffset = windowPos.X + (ActualWidth - 1100) / 2;
        GitFilesPopup.VerticalOffset = windowPos.Y + (ActualHeight - 700) / 2;

        GitFilesPopup.IsOpen = true;
    }

    private async Task RefreshGitFiles()
    {
        if (string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        _gitFiles = await _gitStatusService.GetModifiedFilesAsync(_currentGitWorkingDirectory);

        GitFilesList.ItemsSource = _gitFiles;
        GitFilesCount.Text = _gitFiles.Count == 1
            ? "1 file changed"
            : $"{_gitFiles.Count} files changed";

        GitFilesEmptyState.Visibility = _gitFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Clear selection
        _selectedGitFile = null;
        GitDiffTitle.Text = "Select a file to view diff";
        GitDiffContent.Document = new System.Windows.Documents.FlowDocument();
        UpdateGitFileButtons(false);

        // Auto-select first file if any
        if (_gitFiles.Count > 0)
        {
            GitFilesList.SelectedIndex = 0;
        }
    }

    private void UpdateGitFileButtons(bool hasSelection)
    {
        GitFilePreviewButton.IsEnabled = hasSelection && _selectedGitFile?.Status != Domain.GitFileStatusType.Deleted;
        GitFileEditButton.IsEnabled = hasSelection && _selectedGitFile?.Status != Domain.GitFileStatusType.Deleted;
        GitFileExplorerButton.IsEnabled = hasSelection;
    }

    private async void GitFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GitFilesList.SelectedItem is not Domain.GitFileStatus file)
        {
            _selectedGitFile = null;
            UpdateGitFileButtons(false);
            return;
        }

        _selectedGitFile = file;
        UpdateGitFileButtons(true);

        GitDiffTitle.Text = $"Diff: {file.FilePath}";

        // Load and display diff
        if (string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var diff = await _gitStatusService.GetFileDiffAsync(_currentGitWorkingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            // Use diff highlighter to format
            var highlighter = new Services.SyntaxHighlighting.DiffHighlighter();
            var document = highlighter.CreateHighlightedDocument(diff, null);
            GitDiffContent.Document = document;
        }
        else
        {
            GitDiffContent.Document = CreateInfoDocument("No changes to display");
        }
    }

    private static System.Windows.Documents.FlowDocument CreateInfoDocument(string message)
    {
        var document = new System.Windows.Documents.FlowDocument
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };

        var paragraph = new System.Windows.Documents.Paragraph();
        paragraph.Inlines.Add(new System.Windows.Documents.Run(message));
        document.Blocks.Add(paragraph);

        return document;
    }

    private void GitFilePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGitFile == null || string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var fullPath = System.IO.Path.Combine(_currentGitWorkingDirectory, _selectedGitFile.FilePath);
        if (System.IO.File.Exists(fullPath))
        {
            ShowFilePreview(fullPath);
        }
    }

    private void GitFileEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGitFile == null || string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var fullPath = System.IO.Path.Combine(_currentGitWorkingDirectory, _selectedGitFile.FilePath);
        if (System.IO.File.Exists(fullPath))
        {
            ShowFileEdit(fullPath);
        }
    }

    private void GitFileExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGitFile == null || string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var fullPath = System.IO.Path.Combine(_currentGitWorkingDirectory, _selectedGitFile.FilePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (System.IO.Directory.Exists(directory))
        {
            // Open explorer and select the file if it exists
            if (System.IO.File.Exists(fullPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", directory);
            }
        }
    }

    private async void GitFilesRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshGitFiles();
    }

    private void GitFilesClose_Click(object sender, RoutedEventArgs e)
    {
        GitFilesPopup.IsOpen = false;
    }

    private void GitFilesHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingGitFiles = true;
        _gitFilesDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void GitFilesHeader_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingGitFiles) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _gitFilesDragStart;

        GitFilesPopup.HorizontalOffset += diff.X;
        GitFilesPopup.VerticalOffset += diff.Y;

        _gitFilesDragStart = currentPos;
        e.Handled = true;
    }

    private void GitFilesHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingGitFiles)
        {
            _isDraggingGitFiles = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void GitFilesResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var newWidth = GitFilesBorder.Width + e.HorizontalChange;
        var newHeight = GitFilesBorder.Height + e.VerticalChange;

        if (newWidth >= GitFilesBorder.MinWidth)
        {
            GitFilesBorder.Width = newWidth;
        }
        if (newHeight >= GitFilesBorder.MinHeight)
        {
            GitFilesBorder.Height = newHeight;
        }
    }

    #endregion

    #region Detected Links

    private void DetectedLinksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            // Refresh links before showing
            terminalTab.UpdateDetectedLinks(_viewModel.LinkDetectionService);

            // Bind to view model's detected links
            DetectedLinksList.ItemsSource = terminalTab.DetectedLinks;

            // Show/hide empty state
            DetectedLinksEmptyState.Visibility = terminalTab.DetectedLinks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            DetectedLinksPopup.IsOpen = true;
        }
    }

    private void DetectedLinksRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.UpdateDetectedLinks(_viewModel.LinkDetectionService);

            // Update empty state
            DetectedLinksEmptyState.Visibility = terminalTab.DetectedLinks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void DetectedLinksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection change - could be used for single-click open
    }

    private void DetectedLinksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedDetectedLink();
    }

    private void DetectedLinksList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedDetectedLink();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DetectedLinksPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OpenSelectedDetectedLink()
    {
        if (DetectedLinksList.SelectedItem is DetectedLink link)
        {
            _viewModel.LinkDetectionService.OpenLink(link.Url);
            DetectedLinksPopup.IsOpen = false;
        }
    }

    #endregion
}
