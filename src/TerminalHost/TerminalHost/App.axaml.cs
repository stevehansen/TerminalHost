using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Services;

namespace TerminalHost;

public partial class App : Application
{
    private IServiceProvider? _services;

    public new static App Current => (App)Application.Current!;
    public IServiceProvider Services => _services!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Configure DI
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            // Create main window
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            // Handle shutdown
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Platform Services
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();

        // Terminal Services
        services.AddSingleton<ITerminalControlFactory, TerminalControlFactory>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Dispose services on shutdown
        (_services?.GetService<IStatisticsService>() as IDisposable)?.Dispose();
    }
}
