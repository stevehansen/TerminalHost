using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly IConfigurationService _configService;

    public MainWindow(MainViewModel mainViewModel, IConfigurationService configService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _configService = configService;
        DataContext = _mainViewModel;

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
        _mainViewModel.IsHelpOpen = false;
        _mainViewModel.IsCommandPaletteOpen = false;
        _mainViewModel.IsTabSwitcherOpen = false;
        _mainViewModel.IsTabDropdownOpen = false;
        _mainViewModel.IsQuickTaskOpen = false;
        _mainViewModel.IsQuickNoteOpen = false;
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
