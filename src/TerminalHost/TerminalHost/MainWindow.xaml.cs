using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost;

/// <summary>
/// Core window logic, constructor, and keyboard shortcuts.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigurationService _configService;
    private readonly SystemTrayService? _systemTrayService;
    private readonly ScratchPadViewModel _scratchPadViewModel;
    private readonly GitBranchViewModel _gitBranchViewModel;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly GitFilesViewModel _gitFilesViewModel;
    private readonly FileEditViewModel _fileEditViewModel;
    private bool _isExiting;

    // Drag-and-drop tab reordering
    private Point _dragStartPoint;
    private ITabViewModel? _draggedTab;

    public MainWindow(MainViewModel viewModel, ConfigurationService configService, ScratchPadViewModel scratchPadViewModel, GitBranchViewModel gitBranchViewModel, DetectedLinksViewModel detectedLinksViewModel, GitFilesViewModel gitFilesViewModel, FileEditViewModel fileEditViewModel, SystemTrayService? systemTrayService = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _systemTrayService = systemTrayService;
        _scratchPadViewModel = scratchPadViewModel;
        _gitBranchViewModel = gitBranchViewModel;
        _detectedLinksViewModel = detectedLinksViewModel;
        _gitFilesViewModel = gitFilesViewModel;
        _fileEditViewModel = fileEditViewModel;
        DataContext = viewModel;
        ScratchPadViewControl.DataContext = scratchPadViewModel;
        GitBranchViewControl.DataContext = gitBranchViewModel;
        DetectedLinksViewControl.DataContext = detectedLinksViewModel;
        GitFilesViewControl.DataContext = gitFilesViewModel;
        FileEditViewControl.DataContext = fileEditViewModel;

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
        _detectedLinksViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _gitFilesViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _gitFilesViewModel.FileEditRequested += OnFileEditRequested;

        // Subscribe to help events
        _viewModel.HelpRequested += OnHelpRequested;
        _viewModel.GitChangesRequested += OnGitChangesRequested;

        // Subscribe to run terminal events
        _viewModel.RunTerminalRequested += OnRunTerminalRequested;
    }

    #region Window State and Lifecycle

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

    private void RunSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new run split ratio
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid to get actual column widths
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 5)
            {
                // Main terminals are columns 0-2, run terminal is column 4
                var mainWidth = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
                var runWidth = grid.ColumnDefinitions[4].ActualWidth;
                terminalTab.UpdateRunSplitRatioFromColumnWidths(mainWidth, runWidth);
            }
        }
    }

    private void OpenDetectedRunUrl_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && !string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
        {
            _viewModel.RunUrlDetectionService.OpenInBrowser(terminalTab.DetectedRunUrl);
        }
    }

    private void OnRunTerminalRequested(object? sender, RunTerminalRequestedEventArgs e)
    {
        if (e.IsStop)
        {
            StopRunTerminal(e.Tab);
        }
        else
        {
            StartRunTerminal(e.Tab, e.Configuration);
        }
    }

    private void StartRunTerminal(TerminalPairTabViewModel tab, Domain.RunConfiguration config)
    {
        try
        {
            // Use cmd.exe directly with the run command inline for faster startup
            // This avoids PowerShell profile loading delays
            var runCommand = config.Command;
            var workingDir = tab.Pair.WorkingDirectory;

            // Create a profile that runs the command directly via cmd.exe
            // The command is embedded in the startup so it runs immediately
            var runProfile = new Profile
            {
                Id = "run",
                Name = "Run",
                Command = $"cmd.exe /K cd /d \"{workingDir}\" && {runCommand}",
                WorkingDir = "",  // Already handled in command
                Icon = "▶"
            };

            // Create the run terminal if it doesn't exist
            var runSession = tab.Pair.CreateRunTerminal(runProfile);

            // Create the terminal control
            var runControl = _viewModel.TerminalFactory.CreateTerminalControl(runSession);
            tab.SetRunTerminalControl(runControl);

            // Track the session
            _viewModel.SessionManager.TrackSession(runSession);

            // Subscribe to link click events
            runSession.LinkClicked += (s, text) => HandleRunLinkClick(text, tab.Pair.WorkingDirectory);

            // Mark as started (command runs automatically via startup)
            tab.OnRunStarted();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] Error starting run terminal: {ex.Message}");
            tab.OnRunStopped();
        }
    }

    private void StopRunTerminal(TerminalPairTabViewModel tab)
    {
        try
        {
            if (tab.Pair.RunTerminal == null)
            {
                tab.OnRunStopped();
                return;
            }

            // Send Ctrl+C to stop the process
            tab.Pair.RunTerminal.SendText("\x03", appendNewline: false);

            // Mark as stopped after a short delay to allow the process to terminate
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tab.OnRunStopped();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] Error stopping run terminal: {ex.Message}");
            tab.OnRunStopped();
        }
    }

    private void HandleRunLinkClick(string recentOutput, string workingDirectory)
    {
        // For now, just use the same link handling as other terminals
        if (string.IsNullOrEmpty(recentOutput)) return;

        var link = _viewModel.LinkDetectionService.DetectLink(recentOutput, workingDirectory);
        if (link != null)
        {
            _viewModel.LinkDetectionService.OpenLink(link);
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

    private void OnClosing(object? sender, CancelEventArgs e)
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

        Application.Current.Shutdown();
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

    #endregion

    #region Keyboard Shortcuts

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Escape: Close popups if open
        if (e.Key == Key.Escape)
        {
            if (_gitBranchViewModel.IsOpen)
            {
                _gitBranchViewModel.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (_scratchPadViewModel.IsOpen)
            {
                _scratchPadViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_detectedLinksViewModel.IsOpen)
            {
                _detectedLinksViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_gitFilesViewModel.IsOpen)
            {
                _gitFilesViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_fileEditViewModel.IsOpen)
            {
                _fileEditViewModel.CloseCommand.Execute(null);
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

        // F5: Start/Stop run
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ToggleRunCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Shift+F5: Force stop run
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab && terminalTab.CanStop)
            {
                terminalTab.StopRunCommand.Execute(null);
            }
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
            CenterFileEditPopup();
            var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                ? terminalTab.Pair.WorkingDirectory
                : string.Empty;
            _fileEditViewModel.OpenDialogCommand.Execute(initialDir);
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
            _viewModel.OpenScratchPadCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+G: Open git files panel
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var windowPos = PointToScreen(new Point(0, 0));
                _gitFilesViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _gitFilesViewModel.Width) / 2;
                _gitFilesViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _gitFilesViewModel.Height) / 2;
                await _gitFilesViewModel.OpenAsync(terminalTab);
            }
            else
            {
                MessageBox.Show(
                    "Please select a project tab first.",
                    "Git Changes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        // Ctrl+B: Open git branch switcher
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await _gitBranchViewModel.OpenAsync();
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

    #endregion

    #region Git Files Popup

    private async void OnGitChangesRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            var windowPos = PointToScreen(new Point(0, 0));
            _gitFilesViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _gitFilesViewModel.Width) / 2;
            _gitFilesViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _gitFilesViewModel.Height) / 2;
            await _gitFilesViewModel.OpenAsync(terminalTab);
        }
        else
        {
            MessageBox.Show(
                "Please select a project tab first.",
                "Git Changes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    #region File Operation Handlers

    private void CenterFileEditPopup()
    {
        var windowPos = PointToScreen(new Point(0, 0));
        _fileEditViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _fileEditViewModel.Width) / 2;
        _fileEditViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _fileEditViewModel.Height) / 2;
    }

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        ShowFilePreview(e.FilePath, e.Line);
    }

    private void OnFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        CenterFileEditPopup();
        _fileEditViewModel.Open(e.FilePath);
    }

    #endregion

    #region Test Terminal (Empty State)

    private void TestTerminal_GotFocus(object sender, RoutedEventArgs e)
    {
        Console.WriteLine("[MainWindow] TestTerminal got focus");
    }

    private void TestTerminal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Console.WriteLine("[MainWindow] TestTerminal mouse down - focusing");
        TestTerminal.Focus();
    }

    #endregion
}
