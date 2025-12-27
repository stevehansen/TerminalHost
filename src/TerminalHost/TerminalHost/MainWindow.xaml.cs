using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Windows.Interfaces;
using TerminalHost.Windows.Platform;

namespace TerminalHost;

/// <summary>
/// Core window logic, constructor, and keyboard shortcuts.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IConfigurationService _configService;
    private readonly IProfileRegistry _profileRegistry;
    private readonly ISystemTrayService? _systemTrayService;
    private readonly ScratchPadViewModel _scratchPadViewModel;
    private readonly GitBranchViewModel _gitBranchViewModel;
    private readonly GitStashViewModel _gitStashViewModel;
    private readonly ReflogViewModel _reflogViewModel;
    private readonly ManageWorktreesViewModel _manageWorktreesViewModel;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly GitFilesViewModel _gitFilesViewModel;
    private readonly CommitHistoryViewModel _commitHistoryViewModel;
    private readonly FileHistoryViewModel _fileHistoryViewModel;
    private readonly FileBlameViewModel _fileBlameViewModel;
    private readonly FileViewerViewModel _fileViewerViewModel;
    private readonly RepositorySwitcherViewModel _repositorySwitcherViewModel;
    private readonly TestResultsViewModel _testResultsViewModel;
    private readonly PrReviewViewModel _prReviewViewModel;
    private readonly MarkdownPreviewViewModel _markdownPreviewViewModel;
    private readonly SearchAcrossFilesViewModel _searchAcrossFilesViewModel;
    private readonly IDialogService _dialogService;
    private readonly IFileSystem _fileSystem;
    private readonly IToastService _toastService;
    private bool _isExiting;
    private Services.PanelWindowManager? _panelWindowManager;
    private Views.ToastWindow? _toastWindow;

    public MainWindow(MainViewModel viewModel, IConfigurationService configService, IProfileRegistry profileRegistry, ScratchPadViewModel scratchPadViewModel, GitBranchViewModel gitBranchViewModel, GitStashViewModel gitStashViewModel, ReflogViewModel reflogViewModel, ManageWorktreesViewModel manageWorktreesViewModel, DetectedLinksViewModel detectedLinksViewModel, GitFilesViewModel gitFilesViewModel, CommitHistoryViewModel commitHistoryViewModel, FileHistoryViewModel fileHistoryViewModel, FileBlameViewModel fileBlameViewModel, FileViewerViewModel fileViewerViewModel, RepositorySwitcherViewModel repositorySwitcherViewModel, TestResultsViewModel testResultsViewModel, PrReviewViewModel prReviewViewModel, MarkdownPreviewViewModel markdownPreviewViewModel, SearchAcrossFilesViewModel searchAcrossFilesViewModel, IFileSystem fileSystem, IToastService toastService, ISystemTrayService? systemTrayService = null, IDialogService dialogService = null!)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        _profileRegistry = profileRegistry;
        _systemTrayService = systemTrayService;
        _scratchPadViewModel = scratchPadViewModel;
        _gitBranchViewModel = gitBranchViewModel;
        _gitStashViewModel = gitStashViewModel;
        _reflogViewModel = reflogViewModel;
        _manageWorktreesViewModel = manageWorktreesViewModel;
        _detectedLinksViewModel = detectedLinksViewModel;
        _gitFilesViewModel = gitFilesViewModel;
        _commitHistoryViewModel = commitHistoryViewModel;
        _fileHistoryViewModel = fileHistoryViewModel;
        _fileBlameViewModel = fileBlameViewModel;
        _fileViewerViewModel = fileViewerViewModel;
        _repositorySwitcherViewModel = repositorySwitcherViewModel;
        _testResultsViewModel = testResultsViewModel;
        _prReviewViewModel = prReviewViewModel;
        _markdownPreviewViewModel = markdownPreviewViewModel;
        _searchAcrossFilesViewModel = searchAcrossFilesViewModel;
        _dialogService = dialogService;
        _fileSystem = fileSystem;
        _toastService = toastService;
        DataContext = viewModel;
        GitBranchViewControl.DataContext = gitBranchViewModel;
        GitStashViewControl.DataContext = gitStashViewModel;
        ReflogViewControl.DataContext = reflogViewModel;
        ManageWorktreesViewControl.DataContext = manageWorktreesViewModel;
        DetectedLinksViewControl.DataContext = detectedLinksViewModel;
        FileViewerPopupControl.DataContext = fileViewerViewModel;
        RepositorySwitcherViewControl.DataContext = repositorySwitcherViewModel;
        TestResultsViewControl.DataContext = testResultsViewModel;
        PrReviewViewControl.DataContext = prReviewViewModel;

        // Git Files, Commit History, and Scratch Pad use panel system only (no popup views in XAML, like Markdown Preview)

        // Subscribe to panel show events (single handler for all panels)
        _markdownPreviewViewModel.ShowRequested += OnPanelShowRequested;
        _gitFilesViewModel.ShowRequested += OnPanelShowRequested;
        _commitHistoryViewModel.ShowRequested += OnPanelShowRequested;
        _fileHistoryViewModel.ShowRequested += OnPanelShowRequested;
        _fileBlameViewModel.ShowRequested += OnPanelShowRequested;
        _scratchPadViewModel.ShowRequested += OnPanelShowRequested;
        _searchAcrossFilesViewModel.ShowRequested += OnPanelShowRequested;

        // Subscribe to ManageWorktrees events
        if (_viewModel.WorkspaceSidebar != null)
        {
            _viewModel.WorkspaceSidebar.ManageWorktreesRequested += OnManageWorktreesRequested;
        }
        _manageWorktreesViewModel.OpenWorktreeRequested += OnManageWorktreesOpenWorktree;
        _manageWorktreesViewModel.CreateWorktreeRequested += OnManageWorktreesCreateWorktree;

        RestoreWindowState();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += OnSourceInitialized;

        // Subscribe to view model property changes to sync column widths
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Subscribe to config reload events to update tray setting
        _viewModel.ConfigReloaded += OnConfigReloaded;

        // Subscribe to file preview/edit events
        _viewModel.FilePreviewRequested += OnFilePreviewRequested;
        _detectedLinksViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _gitFilesViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _gitFilesViewModel.FileEditRequested += OnFileEditRequested;
        _fileViewerViewModel.DetachRequested += OnFileViewerDetachRequested;
        _fileViewerViewModel.FileHistoryRequested += OnFileHistoryRequested;
        _fileViewerViewModel.FileBlameRequested += OnFileBlameRequested;
        _viewModel.FileHistoryRequested += OnFileHistoryRequestedFromExplorer;
        _viewModel.FileBlameRequested += OnFileBlameRequestedFromExplorer;

        // Subscribe to help events
        _viewModel.GitChangesRequested += OnGitChangesRequested;
        _viewModel.ScratchPadRequested += OnScratchPadRequested;
        _viewModel.SetupRequested += OnSetupRequested;
        _viewModel.PrReviewRequested += OnPrReviewRequested;
        _viewModel.DashboardPrReviewRequested += OnDashboardPrReviewRequested;
        _viewModel.MarkdownPreviewRequested += OnMarkdownPreviewRequested;

        // Subscribe to run terminal events
        _viewModel.RunTerminalRequested += OnRunTerminalRequested;
    }

    #region Window State and Lifecycle

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        DarkModeHelper.EnableDarkMode(this);
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

    private void StartRunTerminal(TerminalPairTabViewModel tab, RunConfiguration config)
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
            _dialogService.ShowError($"Failed to start run terminal:\n{ex.Message}", "Run Error"); // Use injected IDialogService
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
            _dialogService.ShowError($"Failed to stop run terminal:\n{ex.Message}", "Run Error"); // Use injected IDialogService
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

        // Initialize panel window manager
        _panelWindowManager = new Services.PanelWindowManager(this);

        // Create and show toast window (must be after main window is shown for Owner to work)
        _toastWindow = new Views.ToastWindow();
        _toastWindow.Initialize(this, _toastService);
        _toastWindow.Show();
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
            if (_gitStashViewModel.IsOpen)
            {
                _gitStashViewModel.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (_reflogViewModel.IsOpen)
            {
                _reflogViewModel.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (_manageWorktreesViewModel.IsOpen)
            {
                _manageWorktreesViewModel.IsOpen = false;
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
            if (_fileViewerViewModel.IsOpen)
            {
                _fileViewerViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_viewModel.IsHelpOpen)
            {
                _viewModel.IsHelpOpen = false;
                e.Handled = true;
                return;
            }
        }

        // F1: Toggle help popup
        if (e.Key == Key.F1)
        {
            _viewModel.IsHelpOpen = !_viewModel.IsHelpOpen;
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

        // F6: Run tests
        if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None)
        {
            await _testResultsViewModel.RunAllTestsAsync();
            e.Handled = true;
            return;
        }

        // Handle Tab and Shift+Tab specially for terminals - prevent WPF from stealing focus
        if (e.Key == Key.Tab && IsFocusInTerminal())
        {
            // Send Tab character to the terminal manually since we're blocking WPF navigation
            var tabChar = Keyboard.Modifiers == ModifierKeys.Shift ? "\x1b[Z" : "\t"; // Shift+Tab sends escape sequence
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.GetFocusedSession()?.SendText(tabChar, appendNewline: false);
            }
            else if (_viewModel.SelectedTab is ProfileTerminalTabViewModel profileTab)
            {
                profileTab.Session?.SendText(tabChar, appendNewline: false);
            }
            e.Handled = true;
            return;
        }

        // Handle Ctrl+V for paste into terminal
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && IsFocusInTerminal())
        {
            // Paste clipboard content to the focused terminal
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text) && _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
                {
                    terminalTab.GetFocusedSession()?.SendText(text, appendNewline: false);
                }
                else if (!string.IsNullOrEmpty(text) && _viewModel.SelectedTab is ProfileTerminalTabViewModel profileTab)
                {
                    profileTab.Session?.SendText(text, appendNewline: false);
                }
            }
            e.Handled = true;
            return;
        }

        // Handle Ctrl+C for copy from terminal (only if there's a selection, otherwise let it pass through for SIGINT)
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && IsFocusInTerminal())
        {
            // Try to copy selected text - if successful, handle the event; otherwise let Ctrl+C pass through
            bool copied = false;
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                copied = terminalTab.GetFocusedSession()?.CopySelectionToClipboard() ?? false;
            }
            else if (_viewModel.SelectedTab is ProfileTerminalTabViewModel profileTab)
            {
                copied = profileTab.Session?.CopySelectionToClipboard() ?? false;
            }

            if (copied)
            {
                e.Handled = true;
                return;
            }
            // If no selection was copied, don't handle - let Ctrl+C pass through to terminal for interrupt
        }

        // Only handle shortcuts with modifiers - let other unmodified keys pass through to terminal
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            return; // Don't intercept unmodified keys
        }

        // Ctrl+PageDown: Next tab
        if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.CycleTabCommand.Execute(true);
            e.Handled = true;
        }
        // Ctrl+PageUp: Previous tab
        else if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.CycleTabCommand.Execute(false);
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
            _viewModel.SwitcherSearchText = "";
            _viewModel.IsTabSwitcherOpen = true;
            e.Handled = true;
        }
        // Ctrl+O: Open file viewer (preview mode)
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CenterFileViewerPopup();
            var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                ? terminalTab.Pair.WorkingDirectory
                : string.Empty;
            _fileViewerViewModel.OpenDialogCommand.Execute(initialDir);
            e.Handled = true;
        }
        // Ctrl+Shift+E: Open file viewer (edit mode)
        else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CenterFileViewerPopup();
            var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                ? terminalTab.Pair.WorkingDirectory
                : string.Empty;
            // Open dialog and switch to edit mode if a file is selected
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select File to Edit",
                Filter = "All Files (*.*)|*.*",
                InitialDirectory = initialDir
            };
            if (dialog.ShowDialog() == true)
            {
                _fileViewerViewModel.Open(dialog.FileName, FileViewerMode.Edit);
            }
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
        // Ctrl+Shift+F: Toggle file explorer
        else if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.ToggleExplorerCommand.Execute(null);
            }
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
                _dialogService.ShowInfo("Please select a project tab first.", "Git Changes"); // Use injected IDialogService
            }
        }
        // Ctrl+H: Open commit history
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var windowPos = PointToScreen(new Point(0, 0));
                _commitHistoryViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _commitHistoryViewModel.Width) / 2;
                _commitHistoryViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _commitHistoryViewModel.Height) / 2;
                await _commitHistoryViewModel.OpenAsync(terminalTab);
            }
            else
            {
                _dialogService.ShowInfo("Please select a project tab first.", "Commit History");
            }
        }
        // Ctrl+F3: Open search across files
        else if (e.Key == Key.F3 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var windowPos = PointToScreen(new Point(0, 0));
                _searchAcrossFilesViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _searchAcrossFilesViewModel.Width) / 2;
                _searchAcrossFilesViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _searchAcrossFilesViewModel.Height) / 2;
                await _searchAcrossFilesViewModel.OpenAsync(terminalTab);
            }
            else
            {
                _dialogService.ShowInfo("Please select a project tab first.", "Search");
            }
        }
        // Ctrl+B: Open git branch switcher
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await _gitBranchViewModel.OpenAsync();
            e.Handled = true;
        }
        // Ctrl+Shift+B: Open file blame (if file selected in explorer)
        else if (e.Key == Key.B && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                var explorerVm = terminalTab.ExplorerViewModel;
                if (explorerVm?.SelectedNode != null && !explorerVm.SelectedNode.IsDirectory)
                {
                    OpenFileBlame(terminalTab.Pair.WorkingDirectory, explorerVm.SelectedNode.FullPath);
                }
            }
            e.Handled = true;
        }
        // Ctrl+Shift+S: Open git stash manager
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            await _gitStashViewModel.OpenAsync();
            e.Handled = true;
        }
        // Ctrl+Shift+G: Open git reflog
        else if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                _reflogViewModel.SetTerminalTab(terminalTab);
                await _reflogViewModel.LoadAsync();
                _reflogViewModel.IsOpen = true;
            }
            e.Handled = true;
        }
        // Ctrl+Shift+O: Open repository switcher
        else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            await _repositorySwitcherViewModel.OpenAsync();
            e.Handled = true;
        }
        // Ctrl+Shift+H: Open GitHub Dashboard
        else if (e.Key == Key.H && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            await _viewModel.OpenDashboardCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        // Ctrl+Shift+R: Open PR Review Mode
        else if (e.Key == Key.R && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
            if (currentTab != null)
            {
                await _prReviewViewModel.OpenAsync(currentTab.WorkingDirectory);
            }
            e.Handled = true;
        }
        // Ctrl+Shift+I: Open Timeline Mode
        else if (e.Key == Key.I && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _viewModel.OpenTimelineCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+M: Open Markdown Preview
        else if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await OpenMarkdownPreviewAsync();
            e.Handled = true;
        }
        // Ctrl+L: Toggle layout mode (Tabs/WorkspaceSidebar)
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.ToggleLayoutModeCommand.Execute(null);
            e.Handled = true;
        }
        // Check quick command shortcuts
        else if (TryExecuteQuickCommandShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
        // Check profile launch shortcuts
        else if (TryExecuteProfileShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
        // Check Claude command shortcuts
        else if (TryExecuteClaudeCommandShortcut(e.Key, Keyboard.Modifiers))
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

    private bool TryExecuteProfileShortcut(Key key, ModifierKeys modifiers)
    {
        foreach (var profile in _profileRegistry.Profiles)
        {
            if (string.IsNullOrEmpty(profile.Shortcut)) continue;

            if (TryParseShortcut(profile.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _viewModel.OpenProfileTab(profile);
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryExecuteClaudeCommandShortcut(Key key, ModifierKeys modifiers)
    {
        // Get all Claude commands for the current project
        var claudeCommands = _viewModel.GetClaudeCommandsForCurrentProject();

        foreach (var command in claudeCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _viewModel.ExecuteClaudeCommand(command);
                    return true;
                }
            }
        }
        return false;
    }

    #endregion

    #region Generic Panel Handlers

    /// <summary>
    /// Generic handler for all panel ShowRequested events.
    /// Routes to appropriate display mode based on panel's DisplayState.
    /// </summary>
    private void OnPanelShowRequested(object? sender, EventArgs e)
    {
        if (sender is not IPanelableViewModel panel) return;

        switch (panel.DisplayState)
        {
            case PanelDisplayState.Panel:
                // Show in docked panel
                ShowPanelInTab(panel);
                break;

            case PanelDisplayState.Popup:
                // Show as floating popup
                ShowPanelAsPopup(panel);
                break;

            case PanelDisplayState.Window:
                // Show in window
                _panelWindowManager?.ShowWindow(panel, OnPanelWindowDockRequested);
                break;
        }
    }

    /// <summary>
    /// Shows a panel in the current tab's docked panel area.
    /// </summary>
    private void ShowPanelInTab(IPanelableViewModel panel)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel currentTab)
        {
            currentTab.SetPanel(panel);
            currentTab.ShowPanel(panel);
        }
    }

    /// <summary>
    /// Shows a panel as a floating popup.
    /// </summary>
    private void ShowPanelAsPopup(IPanelableViewModel panel)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel currentTab)
        {
            currentTab.SetPanel(panel);
            currentTab.ShowPanelAsPopup(panel);
        }
    }

    /// <summary>
    /// Generic handler for panel dock requests from windows.
    /// </summary>
    private void OnPanelWindowDockRequested(IPanelableViewModel panel)
    {
        _panelWindowManager?.CloseWindow(panel.PanelId);
        panel.DisplayState = PanelDisplayState.Panel;
        ShowPanelInTab(panel);
    }

    #endregion

    #region Git Files Panel

    private async void OnGitChangesRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null)
        {
            _dialogService.ShowInfo("Please select a project tab first.", "Git Changes");
            return;
        }

        // Ensure the tab has the panel reference
        currentTab.SetPanel(_gitFilesViewModel);

        // If already open, use toggle behavior
        if (_gitFilesViewModel.IsOpen)
        {
            // If in window state, close the window
            if (_gitFilesViewModel.DisplayState == PanelDisplayState.Window)
            {
                _panelWindowManager?.CloseWindow(_gitFilesViewModel.PanelId);
                _gitFilesViewModel.IsOpen = false;
                return;
            }

            // If in popup state, close the popup
            if (_gitFilesViewModel.DisplayState == PanelDisplayState.Popup)
            {
                _gitFilesViewModel.IsOpen = false;
                return;
            }

            // Otherwise, toggle the docked panel (handles focus/visibility)
            currentTab.TogglePanel(_gitFilesViewModel);
            return;
        }

        // Not open yet - use default DisplayState (Popup)
        await _gitFilesViewModel.OpenAsync(currentTab);
    }

    #endregion

    #region Scratch Pad Panel

    private void OnScratchPadRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;

        // Ensure the tab has the panel reference
        if (currentTab != null)
        {
            currentTab.SetPanel(_scratchPadViewModel);
        }

        // If already open, use toggle behavior
        if (_scratchPadViewModel.IsOpen)
        {
            // If in window state, close the window
            if (_scratchPadViewModel.DisplayState == PanelDisplayState.Window)
            {
                _panelWindowManager?.CloseWindow(_scratchPadViewModel.PanelId);
                _scratchPadViewModel.IsOpen = false;
                return;
            }

            // Otherwise, toggle the panel (handles focus/visibility)
            currentTab?.TogglePanel(_scratchPadViewModel);
            return;
        }

        // Not open yet - set display state to Panel for docked display
        _scratchPadViewModel.DisplayState = PanelDisplayState.Panel;
        _scratchPadViewModel.Open();
    }

    #endregion

    #region Setup Window

    private void OnSetupRequested(object? sender, EventArgs e)
    {
        var setupViewModel = new SetupViewModel();
        var setupWindow = new Views.SetupWindow(setupViewModel, isStartupMode: false)
        {
            Owner = this
        };
        setupWindow.ShowDialog();
    }

    private async void OnPrReviewRequested(object? sender, EventArgs e)
    {
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab != null)
        {
            await _prReviewViewModel.OpenAsync(currentTab.WorkingDirectory);
        }
    }

    private async void OnDashboardPrReviewRequested(object? sender, PrReviewRequestedEventArgs e)
    {
        // Open PR Review Mode for the specific PR from the Dashboard
        await _prReviewViewModel.OpenForPrAsync(e.WorkingDirectory, e.PullRequest);
    }

    private async void OnMarkdownPreviewRequested(object? sender, EventArgs e)
    {
        await OpenMarkdownPreviewAsync();
    }

    private async Task OpenMarkdownPreviewAsync()
    {
        // Get current tab
        var currentTab = _viewModel.SelectedTab as TerminalPairTabViewModel;
        if (currentTab == null) return;

        // Ensure the tab has the panel reference
        currentTab.SetPanel(_markdownPreviewViewModel);

        // If preview is already open, use toggle behavior
        if (_markdownPreviewViewModel.IsOpen)
        {
            // If in window state, close the window
            if (_markdownPreviewViewModel.DisplayState == PanelDisplayState.Window)
            {
                _panelWindowManager?.CloseWindow(_markdownPreviewViewModel.PanelId);
                _markdownPreviewViewModel.OnWindowClosed();
                return;
            }

            // Otherwise, toggle the panel (handles focus/visibility)
            currentTab.TogglePanel(_markdownPreviewViewModel);
            return;
        }

        // Not open yet - need to find a markdown file to open
        var workingDir = currentTab.WorkingDirectory;
        if (string.IsNullOrEmpty(workingDir)) return;

        // Try to find a markdown file in the project
        var mdFiles = new[] { "README.md", "readme.md", "README.MD", "CHANGELOG.md", "CONTRIBUTING.md" };
        string? filePath = null;

        foreach (var mdFile in mdFiles)
        {
            var path = System.IO.Path.Combine(workingDir, mdFile);
            if (_fileSystem.FileExists(path))
            {
                filePath = path;
                break;
            }
        }

        if (filePath != null)
        {
            // Set display state to Panel for docked display
            _markdownPreviewViewModel.DisplayState = PanelDisplayState.Panel;
            await _markdownPreviewViewModel.OpenAsync(filePath);
            currentTab.ShowPanel(_markdownPreviewViewModel);
        }
        else
        {
            // Open file picker to select a markdown file
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                InitialDirectory = workingDir
            };

            if (dialog.ShowDialog() == true)
            {
                // Set display state to Panel for docked display
                _markdownPreviewViewModel.DisplayState = PanelDisplayState.Panel;
                await _markdownPreviewViewModel.OpenAsync(dialog.FileName);
                currentTab.ShowPanel(_markdownPreviewViewModel);
            }
        }
    }

    #endregion

    #region File Operation Handlers

    private void CenterFileViewerPopup()
    {
        var windowPos = PointToScreen(new Point(0, 0));
        _fileViewerViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _fileViewerViewModel.Width) / 2;
        _fileViewerViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _fileViewerViewModel.Height) / 2;
    }

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        CenterFileViewerPopup();
        var mode = e.OpenInEditMode ? FileViewerMode.Edit : FileViewerMode.Preview;
        _fileViewerViewModel.Open(e.FilePath, mode, e.Line);
    }

    private void OnFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        CenterFileViewerPopup();
        _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Edit);
    }

    private void OnFileViewerDetachRequested(object? sender, EventArgs e)
    {
        // Close the popup and open in a new window
        var filePath = _fileViewerViewModel.FilePath;
        var mode = _fileViewerViewModel.Mode;

        _fileViewerViewModel.IsOpen = false;

        if (!string.IsNullOrEmpty(filePath))
        {
            // Create a new FileViewerViewModel for the detached window
            var detachedViewModel = new FileViewerViewModel(
                App.Current.Services.GetRequiredService<IFilePreviewService>(),
                App.Current.Services.GetRequiredService<IFileEditService>(),
                App.Current.Services.GetRequiredService<IFileSystem>(),
                App.Current.Services.GetRequiredService<IDialogService>(),
                App.Current.Services.GetRequiredService<IMarkdownService>(),
                App.Current.Services.GetRequiredService<ITimerService>());
            detachedViewModel.IsDetached = true;
            detachedViewModel.Open(filePath, mode);

            var window = new Views.FileViewerWindow { DataContext = detachedViewModel };
            window.Show();
        }
    }

    private void OnFileHistoryRequested(object? sender, string filePath)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileHistory(terminalTab.Pair.WorkingDirectory, filePath);
        }
    }

    private void OnFileHistoryRequestedFromExplorer(object? sender, FileHistoryRequestedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileHistory(terminalTab.Pair.WorkingDirectory, e.FilePath);
        }
    }

    private void OnFileBlameRequested(object? sender, string filePath)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileBlame(terminalTab.Pair.WorkingDirectory, filePath);
        }
    }

    private void OnFileBlameRequestedFromExplorer(object? sender, FileBlameRequestedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            OpenFileBlame(terminalTab.Pair.WorkingDirectory, e.FilePath);
        }
    }

    private async void OpenFileHistory(string workingDirectory, string filePath)
    {
        var windowPos = PointToScreen(new Point(0, 0));
        _fileHistoryViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _fileHistoryViewModel.Width) / 2;
        _fileHistoryViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _fileHistoryViewModel.Height) / 2;
        await _fileHistoryViewModel.OpenAsync(workingDirectory, filePath);
    }

    private async void OpenFileBlame(string workingDirectory, string filePath)
    {
        var windowPos = PointToScreen(new Point(0, 0));
        _fileBlameViewModel.HorizontalOffset = windowPos.X + (ActualWidth - _fileBlameViewModel.Width) / 2;
        _fileBlameViewModel.VerticalOffset = windowPos.Y + (ActualHeight - _fileBlameViewModel.Height) / 2;
        await _fileBlameViewModel.OpenAsync(workingDirectory, filePath);
    }

    #endregion

    #region Manage Worktrees

    private async void OnManageWorktreesRequested(object? sender, (string RepoPath, string RepoName) args)
    {
        await _manageWorktreesViewModel.OpenAsync(args.RepoPath, args.RepoName);
    }

    private void OnManageWorktreesOpenWorktree(object? sender, string worktreePath)
    {
        // Open the worktree as a new tab
        _viewModel.OpenProjectTab(worktreePath);
    }

    private async void OnManageWorktreesCreateWorktree(object? sender, string repoPath)
    {
        // Trigger the create worktree flow from WorkspaceSidebar
        if (_viewModel.WorkspaceSidebar != null)
        {
            var workspace = _viewModel.WorkspaceSidebar.Workspaces
                .FirstOrDefault(w => string.Equals(w.Path, repoPath, StringComparison.OrdinalIgnoreCase))
                ?? _viewModel.WorkspaceSidebar.Playgrounds
                .FirstOrDefault(w => string.Equals(w.Path, repoPath, StringComparison.OrdinalIgnoreCase));

            if (workspace != null)
            {
                await _viewModel.WorkspaceSidebar.CreateWorktreeCommand.ExecuteAsync(workspace);
            }
        }
    }

    #endregion

    #region Test Terminal (Empty State)

    private void TestTerminal_GotFocus(object sender, RoutedEventArgs e)
    {
    }

    private void TestTerminal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        TestTerminal.Focus();
    }

    #endregion

    #region Terminal Focus Detection

    /// <summary>
    /// Checks if the currently focused element is inside a terminal control.
    /// This uses multiple detection methods because EasyTerminalControl uses native HWND hosting.
    /// </summary>
    private bool IsFocusInTerminal()
    {
        // Method 1: Check if the selected tab is a terminal tab and no WPF input control has focus
        var focused = Keyboard.FocusedElement;

        // If a TextBox, ComboBox, or other WPF input control has focus, Tab should work normally there
        if (focused is System.Windows.Controls.TextBox ||
            focused is System.Windows.Controls.ComboBox ||
            focused is System.Windows.Controls.ListBox ||
            focused is System.Windows.Controls.ListView)
        {
            return false;
        }

        // If we're in a terminal tab, assume terminal has focus (HWND hosting doesn't report to WPF)
        if (_viewModel.SelectedTab is TerminalPairTabViewModel || _viewModel.SelectedTab is ProfileTerminalTabViewModel)
        {
            // Method 2: Walk up the visual tree from focused element to find EasyTerminalControl
            var focusedDep = focused as DependencyObject;
            while (focusedDep != null)
            {
                if (focusedDep is EasyWindowsTerminalControl.EasyTerminalControl)
                    return true;

                focusedDep = System.Windows.Media.VisualTreeHelper.GetParent(focusedDep);
            }

            // Method 3: If no WPF element really has focus, the HWND terminal likely has it
            // Check if focus is on the window itself or no specific element
            if (focused == null || focused == this)
            {
                return true;
            }

            // Method 4: Check if focus is on the ContentPresenter holding the terminal
            if (focused is System.Windows.Controls.ContentPresenter ||
                focused is System.Windows.Controls.ContentControl ||
                focused is System.Windows.Controls.Grid ||
                focused is System.Windows.Controls.UserControl)
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}