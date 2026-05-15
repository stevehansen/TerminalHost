using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Settings toggle provider — quick toggles for sounds, touch mode, system
/// tray, close confirmation, and git auto-fetch.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2e.
/// </summary>
internal sealed class SettingsToggleCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public SettingsToggleCommandProvider(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public IEnumerable<PaletteCommand> GetCommands(ICommandContext ctx)
    {
        return _cached ??= Build();
    }

    private IReadOnlyList<PaletteCommand> Build()
    {
        return
        [
            new() {
                Id = "toggle-sounds",
                Name = "Toggle Sounds",
                NameProvider = () => _vm._cachedSettings.Sounds.Enabled ? "Disable Sounds" : "Enable Sounds",
                Description = "Sound notifications",
                Icon = "\U0001F50A",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _vm._configService.Load();
                    config.Settings.Sounds.Enabled = !config.Settings.Sounds.Enabled;
                    _vm._configService.Save(config);
                    _vm._toastService.Show(config.Settings.Sounds.Enabled ? "Sounds enabled" : "Sounds disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-touch-mode",
                Name = "Toggle Touch Mode",
                NameProvider = () => _vm._cachedSettings.TouchMode ? "Disable Touch Mode" : "Enable Touch Mode",
                Description = "Touch-friendly UI with larger targets",
                Icon = "\U0001F446",
                Category = "Settings",
                IntroducedOn = new DateOnly(2026, 1, 12),
                Execute = () => {
                    var config = _vm._configService.Load();
                    config.Settings.TouchMode = !config.Settings.TouchMode;
                    _vm._configService.Save(config);
                    _vm._toastService.Show(config.Settings.TouchMode ? "Touch Mode enabled" : "Touch Mode disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-system-tray",
                Name = "Toggle System Tray",
                NameProvider = () => _vm._cachedSettings.ShowInSystemTray ? "Disable System Tray" : "Enable System Tray",
                Description = "System tray icon",
                Icon = "\U0001F53D",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _vm._configService.Load();
                    config.Settings.ShowInSystemTray = !config.Settings.ShowInSystemTray;
                    _vm._configService.Save(config);
                    _vm._toastService.Show(config.Settings.ShowInSystemTray ? "System tray enabled" : "System tray disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-confirm-close",
                Name = "Toggle Confirm on Close",
                NameProvider = () => _vm._cachedSettings.ConfirmOnClose ? "Disable Confirm on Close" : "Enable Confirm on Close",
                Description = "Confirm before closing tabs",
                Icon = "⚠",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => {
                    var config = _vm._configService.Load();
                    config.Settings.ConfirmOnClose = !config.Settings.ConfirmOnClose;
                    _vm._configService.Save(config);
                    _vm._toastService.Show(config.Settings.ConfirmOnClose ? "Close confirmation enabled" : "Close confirmation disabled", ToastType.Info);
                }
            },
            new() {
                Id = "toggle-git-auto-fetch",
                Name = "Toggle Git Auto-Fetch",
                NameProvider = () => _vm._cachedSettings.GitAutoFetch ? "Disable Git Auto-Fetch" : "Enable Git Auto-Fetch",
                Description = "Automatic fetch from remotes",
                Icon = "\U0001F504",
                Category = "Settings",
                IntroducedOn = new DateOnly(2026, 1, 7),
                Execute = () => {
                    var config = _vm._configService.Load();
                    config.Settings.GitAutoFetch = !config.Settings.GitAutoFetch;
                    _vm._configService.Save(config);
                    _vm._toastService.Show(config.Settings.GitAutoFetch ? "Git auto-fetch enabled" : "Git auto-fetch disabled", ToastType.Info);
                }
            }
        ];
    }
}
