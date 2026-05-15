using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Timeline category provider — Timeline Mode plus incoming hook debug log.
/// (Avalonia exposes a smaller subset than WPF: no install/uninstall/popout yet.)
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2d.
/// </summary>
internal sealed class TimelineCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public TimelineCommandProvider(MainViewModel vm)
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
                Id = "timeline",
                Name = "Timeline Mode",
                Description = "Visual timeline of AI-assisted development sessions",
                Shortcut = "Cmd+Shift+I",
                Icon = "⏱️",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 26),
                Execute = () => _vm.OpenTimelineCommand.Execute(null)
            },
            new() {
                Id = "timeline-hook-debug",
                Name = "Timeline: Hook Debug Log",
                Description = "Show incoming hook events from API and named pipe (troubleshoot container/session tracking)",
                Icon = "🔍",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 27),
                Execute = () => _vm.OpenTimelineHookDebug()
            }
        ];
    }
}
