using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Panel category provider — Tools category panels (task panel, Claude tasks,
/// sessions tree, memory browser, debug log, quick task/note, scratch pad,
/// statistics, test runner stub).
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2e.
/// </summary>
internal sealed class PanelCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public PanelCommandProvider(MainViewModel vm)
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
                Id = "task-panel",
                Name = "Tasks",
                Description = "Open task management panel",
                Shortcut = "Ctrl+T",
                Icon = "\U0001F4CB",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenTaskPanelCommand.Execute(null)
            },
            new() {
                Id = "claude-tasks-panel",
                Name = "Claude Tasks",
                Description = "Monitor Claude Code task activity",
                Shortcut = "Ctrl+Shift+K",
                Icon = "\U0001F916",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 1, 27),
                Execute = () => _vm.OpenClaudeTasksPanelCommand.Execute(null)
            },
            new() {
                Id = "sessions-tree",
                Name = "Sessions",
                Description = "View active Claude Code sessions and subagents",
                Icon = "\U0001F9E0",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 13),
                Execute = () => _vm.OpenSessionsTreeCommand.Execute(null)
            },
            new() {
                Id = "memory-browser",
                Name = "Memory Browser",
                Description = "Browse Eidet long-term memory for this repo",
                Icon = "\U0001F9E0",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 13),
                Execute = () => _vm.OpenMemoryBrowserCommand.Execute(null)
            },
            new() {
                Id = "debug-log",
                Name = "Debug Log",
                Description = "Show diagnostic messages from MCP, Memory, and other subsystems",
                Icon = "\U0001F41B",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 13),
                Execute = () => _vm.OpenDebugLogCommand.Execute(null)
            },
            new() {
                Id = "quick-task",
                Name = "Quick Task",
                Description = "Quickly add a new task",
                Shortcut = "Ctrl+Shift+Q",
                Icon = "+",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenQuickTaskCommand.Execute(null)
            },
            new() {
                Id = "quick-note",
                Name = "Quick Note",
                Description = "Capture a quick note",
                Shortcut = "Ctrl+Shift+M",
                Icon = "\U0001F4DD",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenQuickNoteCommand.Execute(null)
            },
            new() {
                Id = "scratch-pad",
                Name = "Scratch Pad",
                Description = "Open notes panel",
                Shortcut = "Ctrl+Shift+N",
                Icon = "\U0001F4DD",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 14),
                Execute = () => _vm.OpenScratchPadCommand.Execute(null)
            },
            new() {
                Id = "statistics",
                Name = "Statistics",
                Description = "View usage statistics",
                Icon = "\U0001F4CA",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => _vm.OpenStatisticsCommand.Execute(null)
            },
            new() {
                Id = "test-runner",
                Name = "Run Tests",
                Description = "Run project tests",
                Shortcut = "F6",
                Icon = "\U0001F9EA",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 5),
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
