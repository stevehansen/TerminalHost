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

        // Handle F1 for help (placeholder)
        if (e.Key == Key.F1)
        {
            // TODO: Show help when implemented in Stage 7
            e.Handled = true;
        }

        // Handle Escape
        if (e.Key == Key.Escape)
        {
            // TODO: Close popups when implemented
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
