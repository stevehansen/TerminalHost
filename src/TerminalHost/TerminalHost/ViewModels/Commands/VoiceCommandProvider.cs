using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Voice category provider — voice command toggle + enable/disable.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2b.
/// </summary>
internal sealed class VoiceCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public VoiceCommandProvider(MainViewModel vm)
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
                Id = "toggle-voice",
                Name = "Toggle Voice Commands",
                NameProvider = () => _vm.VoiceBar.IsVisible ? "Stop Voice Listening" : "Start Voice Listening",
                Description = "Control your terminal with voice (F4)",
                Shortcut = "F4",
                Icon = "🎙",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => _vm.ToggleVoiceListening()
            },
            new() {
                Id = "toggle-voice-enabled",
                Name = "Toggle Voice Commands Enabled",
                NameProvider = () => _vm._cachedSettings.Voice.Enabled ? "Disable Voice Commands" : "Enable Voice Commands",
                Description = "Enable or disable voice command feature",
                Icon = "🎙",
                Category = "Settings",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => {
                    var config = _vm._configService.Load();
                    config.Settings.Voice.Enabled = !config.Settings.Voice.Enabled;
                    _vm._configService.Save(config);
                    _vm._toastService.Show(config.Settings.Voice.Enabled ? "Voice commands enabled" : "Voice commands disabled", ToastType.Info);
                }
            }
        ];
    }
}
