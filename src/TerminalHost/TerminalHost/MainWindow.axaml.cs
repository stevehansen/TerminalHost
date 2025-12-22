using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Controls;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost;

public partial class MainWindow : Window
{
    private readonly ITerminalControlFactory _terminalFactory;
    private readonly IClipboardService _clipboardService;
    private readonly IStatisticsService _statisticsService;
    private MacTerminalControl? _currentTerminal;
    private TerminalSession? _currentSession;

    public MainWindow()
    {
        InitializeComponent();

        // Get services from DI
        _terminalFactory = App.Current.Services.GetRequiredService<ITerminalControlFactory>();
        _clipboardService = App.Current.Services.GetRequiredService<IClipboardService>();
        _statisticsService = App.Current.Services.GetRequiredService<IStatisticsService>();

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
        // Window opened - initialization can happen here
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Clean up terminal on close
        _currentTerminal?.Dispose();
    }

    private async void NewTerminalButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Clean up existing terminal if any
            if (_currentTerminal != null)
            {
                var container = this.FindControl<Grid>("TerminalContainer");
                container?.Children.Remove(_currentTerminal);
                _currentTerminal.Dispose();
                _currentTerminal = null;
            }

            // Hide welcome panel
            var welcomePanel = this.FindControl<StackPanel>("WelcomePanel");
            if (welcomePanel != null)
            {
                welcomePanel.IsVisible = false;
            }

            // Create default profile for the terminal
            var profile = new Profile
            {
                Name = "Default Shell",
                WorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Command = string.Empty // Will use default shell
            };

            // Create a session for the terminal
            _currentSession = new TerminalSession(profile, _statisticsService, _clipboardService, "Shell");

            // Create terminal using factory
            var terminalControl = await _terminalFactory.CreateTerminalControlAsync(_currentSession);

            if (terminalControl is MacTerminalControl macTerminal)
            {
                _currentTerminal = macTerminal;

                // Add to container
                var container = this.FindControl<Grid>("TerminalContainer");
                if (container != null)
                {
                    container.Children.Add(macTerminal);
                }

                // Focus the terminal
                macTerminal.Focus();
            }
        }
        catch (Exception ex)
        {
            // Show error
            var dialog = App.Current.Services.GetService<IDialogService>();
            dialog?.ShowError($"Failed to create terminal: {ex.Message}", "Error");
        }
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        // Send clear command to terminal (Ctrl+L equivalent)
        _currentTerminal?.WriteToTerminal("\x0C"); // Form feed / clear
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
            // TODO: Show help when implemented
            e.Handled = true;
        }

        // Handle Escape
        if (e.Key == Key.Escape)
        {
            // TODO: Close popups when implemented
            e.Handled = true;
        }

        // Handle Cmd/Ctrl+N for new terminal
        if (e.Key == Key.N && e.KeyModifiers == primaryModifier)
        {
            NewTerminalButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }

        // Handle Cmd/Ctrl+W for close
        if (e.Key == Key.W && e.KeyModifiers == primaryModifier)
        {
            Close();
            e.Handled = true;
        }

        // Handle Cmd/Ctrl+L for clear
        if (e.Key == Key.L && e.KeyModifiers == primaryModifier)
        {
            ClearButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
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
