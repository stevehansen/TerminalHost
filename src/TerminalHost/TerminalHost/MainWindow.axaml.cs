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
    private readonly GitBranchViewModel _gitBranchViewModel;
    private readonly GitFilesViewModel _gitFilesViewModel;
    private readonly ScratchPadViewModel _scratchPadViewModel;
    private readonly FileViewerViewModel _fileViewerViewModel;
    private readonly DetectedLinksViewModel _detectedLinksViewModel;
    private readonly TaskPanelViewModel _taskPanelViewModel;
    private readonly IFilePickerService _filePickerService;

    public MainWindow(
        MainViewModel mainViewModel,
        IConfigurationService configService,
        GitBranchViewModel gitBranchViewModel,
        GitFilesViewModel gitFilesViewModel,
        ScratchPadViewModel scratchPadViewModel,
        FileViewerViewModel fileViewerViewModel,
        DetectedLinksViewModel detectedLinksViewModel,
        TaskPanelViewModel taskPanelViewModel,
        IFilePickerService filePickerService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _configService = configService;
        _gitBranchViewModel = gitBranchViewModel;
        _gitFilesViewModel = gitFilesViewModel;
        _scratchPadViewModel = scratchPadViewModel;
        _fileViewerViewModel = fileViewerViewModel;
        _detectedLinksViewModel = detectedLinksViewModel;
        _taskPanelViewModel = taskPanelViewModel;
        _filePickerService = filePickerService;

        DataContext = _mainViewModel;

        // Set popup DataContexts
        GitBranchPopup.DataContext = _gitBranchViewModel;
        GitFilesPopup.DataContext = _gitFilesViewModel;
        ScratchPadPopup.DataContext = _scratchPadViewModel;
        FileViewerPopup.DataContext = _fileViewerViewModel;
        DetectedLinksPopup.DataContext = _detectedLinksViewModel;
        TaskPanelPopup.DataContext = _taskPanelViewModel;

        // Wire up MainViewModel events
        // Note: ScratchPadViewModel and TaskPanelViewModel subscribe to their events internally
        _mainViewModel.GitChangesRequested += OnGitChangesRequested;
        _mainViewModel.FilePreviewRequested += OnFilePreviewRequested;
        _mainViewModel.FilePopOutRequested += OnFilePopOutRequested;
        _mainViewModel.SetupRequested += OnSetupRequested;

        // Wire up file viewer detach event
        _fileViewerViewModel.DetachRequested += OnFileViewerDetachRequested;

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
        fileMenu.Menu.Add(new NativeMenuItem("New Terminal")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta)
        });
        fileMenu.Menu.Add(new NativeMenuItemSeparator());
        fileMenu.Menu.Add(new NativeMenuItem("Close")
        {
            Gesture = new KeyGesture(Key.W, KeyModifiers.Meta)
        });
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
        viewMenu.Menu.Add(new NativeMenuItem("Toggle Full Screen")
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)
        });
        menu.Add(viewMenu);

        // Window menu
        var windowMenu = new NativeMenuItem("Window") { Menu = new NativeMenu() };
        windowMenu.Menu.Add(new NativeMenuItem("Minimize")
        {
            Gesture = new KeyGesture(Key.M, KeyModifiers.Meta)
        });
        menu.Add(windowMenu);

        // Help menu
        var helpMenu = new NativeMenuItem("Help") { Menu = new NativeMenu() };
        helpMenu.Menu.Add(new NativeMenuItem("Keyboard Shortcuts")
        {
            Gesture = new KeyGesture(Key.F1)
        });
        menu.Add(helpMenu);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _mainViewModel.Initialize();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Save window state
        var config = _configService.Load();
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

        // Handle Cmd/Ctrl+PageDown for next tab
        if (e.Key == Key.PageDown && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.CycleTabCommand.CanExecute(true))
                _mainViewModel.CycleTabCommand.Execute(true);
            e.Handled = true;
            return;
        }

        // Handle Cmd/Ctrl+PageUp for previous tab
        if (e.Key == Key.PageUp && e.KeyModifiers == primaryModifier)
        {
            if (_mainViewModel.CycleTabCommand.CanExecute(false))
                _mainViewModel.CycleTabCommand.Execute(false);
            e.Handled = true;
            return;
        }
    }

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
        if (_scratchPadViewModel.CloseCommand.CanExecute(null))
            _scratchPadViewModel.CloseCommand.Execute(null);
        _fileViewerViewModel.Close();
        if (_detectedLinksViewModel.CloseCommand.CanExecute(null))
            _detectedLinksViewModel.CloseCommand.Execute(null);
        if (_taskPanelViewModel.CloseCommand.CanExecute(null))
            _taskPanelViewModel.CloseCommand.Execute(null);
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
}
