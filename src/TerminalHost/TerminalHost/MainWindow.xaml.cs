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

    public MainWindow(MainViewModel viewModel, ConfigurationService configService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        DataContext = viewModel;

        RestoreWindowState();

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        // Subscribe to view model property changes to sync column widths
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        SaveWindowState();
        _viewModel.Shutdown();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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
        // Ctrl+E: Open in Explorer
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenInExplorerCommand.Execute(null);
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
}
