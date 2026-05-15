using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Timeline category provider — Timeline Mode plus session-tracking hook
/// install/uninstall, popout window, and incoming hook debug log.
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
                Shortcut = "Ctrl+Shift+I",
                Icon = "⏱️",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 26),
                Execute = () => _vm.OpenTimelineCommand.Execute(null)
            },
            new() {
                Id = "timeline-install-hooks",
                Name = "Timeline: Install Session Tracking Hooks",
                Description = "Install hooks into ~/.claude/settings.json to track Claude Code sessions",
                Icon = "🔗",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _vm.InstallTimelineHooks()
            },
            new() {
                Id = "timeline-popout",
                Name = "Timeline: Pop Out to Window",
                Description = "Open timeline in a standalone window for multi-monitor use",
                Icon = "⧉",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 25),
                Execute = () => _vm.OpenTimelinePopout()
            },
            new() {
                Id = "timeline-uninstall-hooks",
                Name = "Timeline: Uninstall Session Tracking Hooks",
                Description = "Remove TerminalHost hooks from ~/.claude/settings.json",
                Icon = "🔗",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 24),
                Execute = () => _vm.UninstallTimelineHooks()
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
