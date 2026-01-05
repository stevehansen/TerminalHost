using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using ITimerService = TerminalHost.Services.ITimerService;

namespace TerminalHost.Views;

public partial class SetupWindow : Window
{
    private readonly IClipboardService _clipboardService;
    private readonly ITimerService _timerService;

    /// <summary>
    /// When true, shows "Continue to App" button (startup mode).
    /// When false, only shows "Close" button (in-app mode).
    /// </summary>
    public bool IsStartupMode { get; set; } = true;

    public SetupWindow(SetupViewModel viewModel, IClipboardService clipboardService, ITimerService timerService, bool isStartupMode = true)
    {
        InitializeComponent();
        DataContext = viewModel;
        IsStartupMode = isStartupMode;

        _clipboardService = clipboardService;
        _timerService = timerService;

        Opened += async (s, e) =>
        {
            // Hide Continue button if not in startup mode
            ContinueButton.IsVisible = IsStartupMode;

            if (DataContext is SetupViewModel vm)
            {
                await vm.CheckAllDependenciesCommand.ExecuteAsync(null);
            }
        };
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CopyInstallCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string command)
        {
            try
            {
                await _clipboardService.SetTextAsync(command);

                // Briefly change button text to indicate success
                var originalContent = button.Content;
                button.Content = "✓ Copied!";

                var timer = _timerService.CreateTimer(TimeSpan.FromSeconds(1.5), () =>
                {
                    button.Content = originalContent;
                });
                timer.Start();

                // Dispose timer after it fires once
                await Task.Delay(2000);
                timer.Dispose();
            }
            catch
            {
                // Clipboard operation failed - silently ignore
            }
        }
    }
}