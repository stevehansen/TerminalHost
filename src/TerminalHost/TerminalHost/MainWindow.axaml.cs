using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private readonly GitBranchViewModel _gitBranchViewModel;
    private readonly GitFilesViewModel _gitFilesViewModel;
    private readonly CommitHistoryViewModel _commitHistoryViewModel;
    private readonly GitStashViewModel _gitStashViewModel;
    private readonly ScratchPadViewModel _scratchPadViewModel;
    private readonly FileViewerViewModel _fileViewerViewModel;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly TaskPanelViewModel _taskPanelViewModel;
    private readonly SearchAcrossFilesViewModel _searchAcrossFilesViewModel;
    private readonly FileHistoryViewModel _fileHistoryViewModel;
    private readonly FileBlameViewModel _fileBlameViewModel;
    private readonly ReflogViewModel _reflogViewModel;
    private readonly WorkspaceSidebarViewModel _workspaceSidebarViewModel;
    private readonly IFilePickerService _filePickerService;

    public MainWindow(
        MainViewModel mainViewModel,
        IConfigurationService configService,
        IDialogService dialogService,
        GitBranchViewModel gitBranchViewModel,
        GitFilesViewModel gitFilesViewModel,
        CommitHistoryViewModel commitHistoryViewModel,
        GitStashViewModel gitStashViewModel,
        ScratchPadViewModel scratchPadViewModel,
        FileViewerViewModel fileViewerViewModel,
        DetectedLinksViewModel detectedLinksViewModel,
        TaskPanelViewModel taskPanelViewModel,
        SearchAcrossFilesViewModel searchAcrossFilesViewModel,
        FileHistoryViewModel fileHistoryViewModel,
        FileBlameViewModel fileBlameViewModel,
        ReflogViewModel reflogViewModel,
        WorkspaceSidebarViewModel workspaceSidebarViewModel,
        IFilePickerService filePickerService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _configService = configService;
        _dialogService = dialogService;
        _gitBranchViewModel = gitBranchViewModel;
        _gitFilesViewModel = gitFilesViewModel;
        _commitHistoryViewModel = commitHistoryViewModel;
        _gitStashViewModel = gitStashViewModel;
        _scratchPadViewModel = scratchPadViewModel;
        _fileViewerViewModel = fileViewerViewModel;
        _detectedLinksViewModel = detectedLinksViewModel;
        _taskPanelViewModel = taskPanelViewModel;
        _searchAcrossFilesViewModel = searchAcrossFilesViewModel;
        _fileHistoryViewModel = fileHistoryViewModel;
        _fileBlameViewModel = fileBlameViewModel;
        _reflogViewModel = reflogViewModel;
        _workspaceSidebarViewModel = workspaceSidebarViewModel;
        _filePickerService = filePickerService;

        // Wire up sidebar view model bidirectional reference
        _mainViewModel.SidebarViewModel = _workspaceSidebarViewModel;
        _workspaceSidebarViewModel.MainViewModel = _mainViewModel;

        DataContext = _mainViewModel;

        // Set sidebar DataContext
        WorkspaceSidebar.DataContext = _workspaceSidebarViewModel;

        // Set popup DataContexts
        GitBranchPopup.DataContext = _gitBranchViewModel;
        GitFilesPopup.DataContext = _gitFilesViewModel;
        CommitHistoryPopup.DataContext = _commitHistoryViewModel;
        GitStashPopup.DataContext = _gitStashViewModel;
        ScratchPadPopup.DataContext = _scratchPadViewModel;
        FileViewerPopup.DataContext = _fileViewerViewModel;
        DetectedLinksPopup.DataContext = _detectedLinksViewModel;
        TaskPanelPopup.DataContext = _taskPanelViewModel;
        SearchAcrossFilesPopup.DataContext = _searchAcrossFilesViewModel;
        FileHistoryPopup.DataContext = _fileHistoryViewModel;
        FileBlamePopup.DataContext = _fileBlameViewModel;
        ReflogPopup.DataContext = _reflogViewModel;

        // Wire up MainViewModel events
        // Note: ScratchPadViewModel and TaskPanelViewModel subscribe to their events internally
        _mainViewModel.GitChangesRequested += OnGitChangesRequested;
        _mainViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _mainViewModel.FilePopOutRequested += OnFilePopOutRequested;
        _mainViewModel.SetupRequested += OnSetupRequested;
        _mainViewModel.FileHistoryRequested += OnFileHistoryRequested;
        _mainViewModel.FileBlameRequested += OnFileBlameRequested;

        // Wire up GitFilesViewModel events for file preview/edit from Git Changes popup
        _gitFilesViewModel.FilePreviewRequested += OnGitFilesFilePreviewRequested;
        _gitFilesViewModel.FileEditRequested += OnGitFilesFileEditRequested;

        // Wire up file viewer detach event
        _fileViewerViewModel.DetachRequested += OnFileViewerDetachRequested;

        // Wire up search across files events
        _searchAcrossFilesViewModel.FilePreviewRequested += OnSearchFilePreviewRequested;
        _searchAcrossFilesViewModel.FileEditRequested += OnSearchFileEditRequested;

        // Event handlers
        Opened += OnOpened;
        Closing += OnClosing;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Set up macOS native menu
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetupMacOSMenu();
        }
    }

    private void SetupMacOSMenu()
    {
        var menu = NativeMenu.GetMenu(this);
        if (menu == null)
        {
            menu = new NativeMenu();
            NativeMenu.SetMenu(this, menu);
        }

        // File menu
        var fileMenu = new NativeMenuItem("File") { Menu = new NativeMenu() };

        var newProjectItem = new NativeMenuItem("New Project...")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta)
        };
        newProjectItem.Click += (_, _) => _mainViewModel.OpenNewProjectCommand.Execute(null);
        fileMenu.Menu.Add(newProjectItem);

        fileMenu.Menu.Add(new NativeMenuItemSeparator());

        var closeTabItem = new NativeMenuItem("Close Tab")
        {
            Gesture = new KeyGesture(Key.W, KeyModifiers.Meta)
        };
        closeTabItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab != null)
                _mainViewModel.CloseTabCommand.Execute(_mainViewModel.SelectedTab);
        };
        fileMenu.Menu.Add(closeTabItem);

        menu.Add(fileMenu);

        // Edit menu
        var editMenu = new NativeMenuItem("Edit") { Menu = new NativeMenu() };
        editMenu.Menu.Add(new NativeMenuItem("Copy")
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Meta)
        });
        editMenu.Menu.Add(new NativeMenuItem("Paste")
        {
            Gesture = new KeyGesture(Key.V, KeyModifiers.Meta)
        });
        editMenu.Menu.Add(new NativeMenuItemSeparator());
        editMenu.Menu.Add(new NativeMenuItem("Select All")
        {
            Gesture = new KeyGesture(Key.A, KeyModifiers.Meta)
        });
        menu.Add(editMenu);

        // View menu
        var viewMenu = new NativeMenuItem("View") { Menu = new NativeMenu() };

        var settingsItem = new NativeMenuItem("Settings...")
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta)
        };
        settingsItem.Click += (_, _) => _mainViewModel.OpenSettingsCommand.Execute(null);
        viewMenu.Menu.Add(settingsItem);

        var statisticsItem = new NativeMenuItem("Statistics");
        statisticsItem.Click += (_, _) => _mainViewModel.OpenStatisticsCommand.Execute(null);
        viewMenu.Menu.Add(statisticsItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var commandPaletteItem = new NativeMenuItem("Command Palette...")
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        commandPaletteItem.Click += (_, _) => _mainViewModel.IsCommandPaletteOpen = true;
        viewMenu.Menu.Add(commandPaletteItem);

        var tabSwitcherItem = new NativeMenuItem("Tab Switcher...")
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        tabSwitcherItem.Click += (_, _) => _mainViewModel.IsTabSwitcherOpen = true;
        viewMenu.Menu.Add(tabSwitcherItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var gitBranchItem = new NativeMenuItem("Git Branches...")
        {
            Gesture = new KeyGesture(Key.B, KeyModifiers.Meta)
        };
        gitBranchItem.Click += (_, _) => _ = _gitBranchViewModel.OpenCommand.ExecuteAsync(null);
        viewMenu.Menu.Add(gitBranchItem);

        var gitChangesItem = new NativeMenuItem("Git Changes...")
        {
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta)
        };
        gitChangesItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
                _ = _gitFilesViewModel.OpenCommand.ExecuteAsync(terminalTab);
        };
        viewMenu.Menu.Add(gitChangesItem);

        var commitHistoryItem = new NativeMenuItem("Commit History...")
        {
            Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        commitHistoryItem.Click += (_, _) => _ = _commitHistoryViewModel.OpenCommand.ExecuteAsync(null);
        viewMenu.Menu.Add(commitHistoryItem);

        var gitStashItem = new NativeMenuItem("Git Stash...")
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        gitStashItem.Click += (_, _) => _ = _gitStashViewModel.OpenCommand.ExecuteAsync(null);
        viewMenu.Menu.Add(gitStashItem);

        var gitReflogItem = new NativeMenuItem("Git Reflog...")
        {
            Gesture = new KeyGesture(Key.G, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        gitReflogItem.Click += (_, _) => _ = _reflogViewModel.OpenCommand.ExecuteAsync(null);
        viewMenu.Menu.Add(gitReflogItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var searchFilesItem = new NativeMenuItem("Search in Files...")
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Meta)
        };
        searchFilesItem.Click += (_, _) =>
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
                _searchAcrossFilesViewModel.OpenCommand.Execute(terminalTab);
        };
        viewMenu.Menu.Add(searchFilesItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var scratchPadItem = new NativeMenuItem("Scratch Pad")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta | KeyModifiers.Shift)
        };
        scratchPadItem.Click += (_, _) => _scratchPadViewModel.Open();
        viewMenu.Menu.Add(scratchPadItem);

        var taskPanelItem = new NativeMenuItem("Task Panel")
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Meta)
        };
        taskPanelItem.Click += (_, _) => _taskPanelViewModel.Open();
        viewMenu.Menu.Add(taskPanelItem);

        viewMenu.Menu.Add(new NativeMenuItemSeparator());

        var toggleFullScreenItem = new NativeMenuItem("Toggle Full Screen")
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)
        };
        toggleFullScreenItem.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        };
        viewMenu.Menu.Add(toggleFullScreenItem);

        menu.Add(viewMenu);

        // Window menu
        var windowMenu = new NativeMenuItem("Window") { Menu = new NativeMenu() };

        var minimizeItem = new NativeMenuItem("Minimize")
        {
            Gesture = new KeyGesture(Key.M, KeyModifiers.Meta)
        };
        minimizeItem.Click += (_, _) => WindowState = WindowState.Minimized;
        windowMenu.Menu.Add(minimizeItem);

        var nextTabItem = new NativeMenuItem("Next Tab")
        {
            Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control)
        };
        nextTabItem.Click += (_, _) => _mainViewModel.CycleTabCommand.Execute(true);
        windowMenu.Menu.Add(nextTabItem);

        var prevTabItem = new NativeMenuItem("Previous Tab")
        {
            Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift)
        };
        prevTabItem.Click += (_, _) => _mainViewModel.CycleTabCommand.Execute(false);
        windowMenu.Menu.Add(prevTabItem);

        menu.Add(windowMenu);

        // Help menu
        var helpMenu = new NativeMenuItem("Help") { Menu = new NativeMenu() };

        var keyboardShortcutsItem = new NativeMenuItem("Keyboard Shortcuts")
        {
            Gesture = new KeyGesture(Key.F1)
        };
        keyboardShortcutsItem.Click += (_, _) => _mainViewModel.IsHelpOpen = true;
        helpMenu.Menu.Add(keyboardShortcutsItem);

        menu.Add(helpMenu);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _mainViewModel.Initialize();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var config = _configService.Load();

        // Check if we need to confirm close
        if (config.Settings.ConfirmOnClose)
        {
            // Check if any terminals are still running
            var hasRunningTerminals = _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>()
                .Any(t => t.Pair.CustomTerminal.IsProcessRunning() || t.Pair.ShellTerminal.IsProcessRunning());

            if (!hasRunningTerminals)
            {
                hasRunningTerminals = _mainViewModel.Tabs.OfType<ProfileTerminalTabViewModel>()
                    .Any(t => t.Session.IsProcessRunning());
            }

            if (hasRunningTerminals)
            {
                if (!_dialogService.ShowConfirmation(
                    "There are still terminals running. Are you sure you want to close?",
                    "Confirm Close"))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        // Save window state
        config.WindowState = new Domain.WindowStateInfo
        {
            Left = Position.X,
            Top = Position.Y,
            Width = (int)Width,
            Height = (int)Height,
            IsMaximized = WindowState == WindowState.Maximized
        };
        _configService.Save(config);

        // Shutdown view model
        _mainViewModel.Shutdown();
    }

    #region Popup Event Handlers

    private async void OnGitChangesRequested(object? sender, EventArgs e)
    {
        if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            await _gitFilesViewModel.OpenCommand.ExecuteAsync(terminalTab);
        }
    }

    private void OnGitFilesFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Preview);
        }
    }

    private void OnGitFilesFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Edit);
        }
    }

    private void OnSearchFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _searchAcrossFilesViewModel.CloseCommand.Execute(null);
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Preview, e.Line);
        }
    }

    private void OnSearchFileEditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.FilePath))
        {
            _searchAcrossFilesViewModel.CloseCommand.Execute(null);
            _fileViewerViewModel.Open(e.FilePath, FileViewerMode.Edit, e.LineNumber);
        }
    }

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.FilePath))
        {
            // Open file picker if no path provided
            _ = OpenFilePickerAsync(e.OpenInEditMode);
            return;
        }

        var mode = e.OpenInEditMode ? FileViewerMode.Edit : FileViewerMode.Preview;
        _fileViewerViewModel.Open(e.FilePath, mode, e.Line > 0 ? e.Line : null);
    }

    private void OnFilePopOutRequested(object? sender, FileViewerRequestedEventArgs e)
    {
        // Create a new FileViewerWindow for pop-out
        CreatePopOutWindow(e.FilePath, e.Mode == FileViewerMode.Edit);
    }

    private void OnFileHistoryRequested(object? sender, FileHistoryRequestedEventArgs e)
    {
        _ = _fileHistoryViewModel.OpenAsync(e.WorkingDirectory, e.FilePath);
    }

    private void OnFileBlameRequested(object? sender, FileBlameRequestedEventArgs e)
    {
        _ = _fileBlameViewModel.OpenAsync(e.WorkingDirectory, e.FilePath);
    }

    private void OnFileViewerDetachRequested(object? sender, EventArgs e)
    {
        // Pop out the current file from the popup viewer
        if (!string.IsNullOrEmpty(_fileViewerViewModel.FilePath))
        {
            var isEditMode = _fileViewerViewModel.IsEditModeSelected;
            CreatePopOutWindow(_fileViewerViewModel.FilePath, isEditMode);
            _fileViewerViewModel.Close();
        }
    }

    private void CreatePopOutWindow(string filePath, bool editMode)
    {
        // TODO: Create FileViewerWindow when implemented for Avalonia
        // For now, just open in default app
        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                // Silently fail
            }
        }
    }

    private void OnSetupRequested(object? sender, EventArgs e)
    {
        // TODO: Create SetupWindow when implemented for Avalonia
    }

    private async Task OpenFilePickerAsync(bool editMode)
    {
        try
        {
            var initialDir = (_mainViewModel.SelectedTab as TerminalPairTabViewModel)?.WorkingDirectory;
            var filePath = await _filePickerService.PickFileAsync(
                title: "Select File",
                filters: null,
                initialDirectory: initialDir);

            if (!string.IsNullOrEmpty(filePath))
            {
                var mode = editMode ? FileViewerMode.Edit : FileViewerMode.Preview;
                _fileViewerViewModel.Open(filePath, mode);
            }
        }
        catch
        {
            // Silently fail if file picker fails
        }
    }

    #endregion

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Get the platform-appropriate modifier (Meta on macOS, Control otherwise)
        var primaryModifier = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? KeyModifiers.Meta
            : KeyModifiers.Control;

        // Handle F1 for help
        if (e.Key == Key.F1)
        {
            _mainViewModel.IsHelpOpen = !_mainViewModel.IsHelpOpen;
            e.Handled = true;
            return;
        }

        // Handle Escape - close popups
        if (e.Key == Key.Escape)
        {
            CloseAllPopups();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+N for new project
        if (e.Key == Key.N && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.OpenNewProjectCommand.CanExecute(null))
                _mainViewModel.OpenNewProjectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+W for close tab
        if (e.Key == Key.W && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab != null && _mainViewModel.CloseTabCommand.CanExecute(_mainViewModel.SelectedTab))
                _mainViewModel.CloseTabCommand.Execute(_mainViewModel.SelectedTab);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+, for settings
        if (e.Key == Key.OemComma && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.OpenSettingsCommand.CanExecute(null))
                _mainViewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+P for command palette
        if (e.Key == Key.P && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.IsCommandPaletteOpen = !_mainViewModel.IsCommandPaletteOpen;
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+T for tab switcher
        if (e.Key == Key.T && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.IsTabSwitcherOpen = !_mainViewModel.IsTabSwitcherOpen;
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+B for Git Branch switcher
        if (e.Key == Key.B && e.KeyModifiers == primaryModifier)
        {
            _ = _gitBranchViewModel.OpenCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+G for Git Changes panel
        if (e.Key == Key.G && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                _ = _gitFilesViewModel.OpenCommand.ExecuteAsync(terminalTab);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+H for Commit History
        if (e.Key == Key.H && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _ = _commitHistoryViewModel.OpenCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+S for Git Stash
        if (e.Key == Key.S && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _ = _gitStashViewModel.OpenCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+G for Git Reflog
        if (e.Key == Key.G && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _ = _reflogViewModel.OpenCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+L for Layout Mode Toggle
        if (e.Key == Key.L && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _mainViewModel.ToggleLayoutModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+N for Scratch Pad
        if (e.Key == Key.N && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            _scratchPadViewModel.Open();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+T for Task Panel
        if (e.Key == Key.T && e.KeyModifiers == primaryModifier)
        {
            _taskPanelViewModel.Open();
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+O for File Preview
        if (e.Key == Key.O && e.KeyModifiers == primaryModifier)
        {
            // Open file picker for preview
            _ = OpenFilePickerAsync(editMode: false);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+E for File Edit
        if (e.Key == Key.E && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            // Open file picker for edit
            _ = OpenFilePickerAsync(editMode: true);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+Shift+F for File Explorer toggle
        if (e.Key == Key.F && e.KeyModifiers == (primaryModifier | KeyModifiers.Shift))
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                terminalTab.IsExplorerVisible = !terminalTab.IsExplorerVisible;
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+F for Search Across Files
        if (e.Key == Key.F && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
            {
                _searchAcrossFilesViewModel.OpenCommand.Execute(terminalTab);
            }
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+1-9 for tab jumping
        if (e.KeyModifiers == primaryModifier && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < _mainViewModel.Tabs.Count)
            {
                _mainViewModel.SelectedTab = _mainViewModel.Tabs[index];
            }
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Tab for next tab
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
        {
            if (_mainViewModel.CycleTabCommand.CanExecute(true))
                _mainViewModel.CycleTabCommand.Execute(true);
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+Tab for previous tab
        if (e.Key == Key.Tab && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            if (_mainViewModel.CycleTabCommand.CanExecute(false))
                _mainViewModel.CycleTabCommand.Execute(false);
            e.Handled = true;
            return;
        }

        // Check Quick Command shortcuts
        if (TryExecuteQuickCommandShortcut(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }
    }

    #region Quick Command Shortcuts

    private bool TryExecuteQuickCommandShortcut(Key key, KeyModifiers modifiers)
    {
        foreach (var command in _mainViewModel.QuickCommands)
        {
            if (string.IsNullOrEmpty(command.Shortcut)) continue;

            if (TryParseShortcut(command.Shortcut, out var expectedKey, out var expectedModifiers))
            {
                if (key == expectedKey && modifiers == expectedModifiers)
                {
                    _mainViewModel.ExecuteQuickCommandCommand.Execute(command);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryParseShortcut(string shortcut, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;

        var parts = shortcut.Split('+', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        // Parse modifiers and key
        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();
            switch (upperPart)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "CMD":
                case "META":
                    modifiers |= KeyModifiers.Meta;
                    break;
                case "ALT":
                case "OPT":
                case "OPTION":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= KeyModifiers.Shift;
                    break;
                default:
                    // Try to parse as a Key
                    if (System.Enum.TryParse<Key>(part, ignoreCase: true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (part.Length == 1 && char.IsLetter(part[0]))
                    {
                        // Single letter key (A-Z)
                        key = (Key)System.Enum.Parse(typeof(Key), part.ToUpperInvariant());
                    }
                    else if (part.Length == 1 && char.IsDigit(part[0]))
                    {
                        // Number key (0-9) - use D0-D9 for top row
                        key = (Key)System.Enum.Parse(typeof(Key), "D" + part);
                    }
                    break;
            }
        }

        return key != Key.None && modifiers != KeyModifiers.None;
    }

    /// <summary>
    /// Formats a key combination into a shortcut string.
    /// </summary>
    public static string FormatShortcut(Key key, KeyModifiers modifiers)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Cmd");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");

        // Convert key to display string
        var keyStr = key.ToString();
        if (keyStr.StartsWith("D") && keyStr.Length == 2 && char.IsDigit(keyStr[1]))
        {
            // D0-D9 -> 0-9
            keyStr = keyStr[1].ToString();
        }
        parts.Add(keyStr);

        return string.Join("+", parts);
    }

    #endregion

    private void CloseAllPopups()
    {
        // Close MainViewModel popups
        _mainViewModel.IsHelpOpen = false;
        _mainViewModel.IsCommandPaletteOpen = false;
        _mainViewModel.IsTabSwitcherOpen = false;
        _mainViewModel.IsTabDropdownOpen = false;
        _mainViewModel.IsQuickTaskOpen = false;
        _mainViewModel.IsQuickNoteOpen = false;

        // Close ViewModel-managed popups
        _gitBranchViewModel.IsOpen = false;
        if (_gitFilesViewModel.CloseCommand.CanExecute(null))
            _gitFilesViewModel.CloseCommand.Execute(null);
        if (_commitHistoryViewModel.CloseCommand.CanExecute(null))
            _commitHistoryViewModel.CloseCommand.Execute(null);
        if (_gitStashViewModel.CloseCommand.CanExecute(null))
            _gitStashViewModel.CloseCommand.Execute(null);
        if (_scratchPadViewModel.CloseCommand.CanExecute(null))
            _scratchPadViewModel.CloseCommand.Execute(null);
        _fileViewerViewModel.Close();
        if (_detectedLinksViewModel.CloseCommand.CanExecute(null))
            _detectedLinksViewModel.CloseCommand.Execute(null);
        if (_taskPanelViewModel.CloseCommand.CanExecute(null))
            _taskPanelViewModel.CloseCommand.Execute(null);
        if (_searchAcrossFilesViewModel.CloseCommand.CanExecute(null))
            _searchAcrossFilesViewModel.CloseCommand.Execute(null);
        if (_fileHistoryViewModel.CloseCommand.CanExecute(null))
            _fileHistoryViewModel.CloseCommand.Execute(null);
        if (_fileBlameViewModel.CloseCommand.CanExecute(null))
            _fileBlameViewModel.CloseCommand.Execute(null);
        if (_reflogViewModel.CloseCommand.CanExecute(null))
            _reflogViewModel.CloseCommand.Execute(null);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void SidebarSplitter_DragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        // Update the main view model with the new sidebar width
        if (sender is GridSplitter splitter && splitter.Parent is Grid grid)
        {
            // Column 0 is the sidebar
            if (grid.ColumnDefinitions.Count >= 1)
            {
                var sidebarWidth = grid.ColumnDefinitions[0].ActualWidth;
                _mainViewModel.UpdateSidebarWidth(sidebarWidth);
            }
        }
    }
}
