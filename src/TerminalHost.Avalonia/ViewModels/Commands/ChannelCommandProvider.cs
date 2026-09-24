using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Channel category provider — Claude Code channel integration: send a message to the
/// live Claude session, toggle integration globally; plus the Parley toggles (Parley's
/// push delivery also rides on Claude Code channels).
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2d.
/// </summary>
internal sealed class ChannelCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public ChannelCommandProvider(MainViewModel vm)
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
                Id = "channel-send-message",
                Name = "Channel: Send Message to Claude",
                Description = "Send a text message to the Claude Code session via the channel",
                Icon = "📨",
                Category = "Channel",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _vm.SendChannelMessage(),
                CanExecute = () => _vm._apiServer?.IsRunning == true
            },
            new() {
                Id = "channel-toggle",
                Name = "Channel: Toggle Integration",
                Description = "Enable or disable Claude Code channel integration",
                Icon = "🔌",
                Category = "Channel",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _vm.ToggleChannelIntegration()
            },
            new() {
                Id = "parley-toggle",
                Name = "Parley: Toggle Integration",
                Description = "Enable or disable Parley inter-session messaging (MCP registration, session naming, Claude Tasks feed)",
                Icon = "💬",
                Category = "Parley",
                IntroducedOn = new DateOnly(2026, 9, 24),
                Execute = () => _vm.ToggleParleyIntegration()
            },
            new() {
                Id = "parley-toggle-push",
                Name = "Parley: Toggle Push via Channels",
                Description = "Push Parley messages into Claude Code sessions (--dangerously-load-development-channels server:parley)",
                Icon = "📬",
                Category = "Parley",
                IntroducedOn = new DateOnly(2026, 9, 24),
                Execute = () => _vm.ToggleParleyPushViaChannels()
            }
        ];
    }
}
