using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Panel category provider — side/center panels under the Tools category
/// (scratch pad, statistics, Claude tasks, sessions tree, test runner,
/// memory intake/browser, debug log).
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
                Id = "claude-tasks",
                Name = "Claude Tasks",
                Description = "View Claude Code task activity",
                Shortcut = "Ctrl+Shift+K",
                Icon = "\U0001F916",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 1, 27),
                Execute = () => _vm.RequestClaudeTasks(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "sessions-tree",
                Name = "Toggle Sessions Panel",
                Description = "Toggle the global Sessions panel (active Claude Code sessions and subagents with live activity and context usage)",
                Shortcut = "Ctrl+Shift+A",
                Icon = "\U0001F9E0",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 22),
                Execute = () => _vm.RequestSessionsTree(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "test-runner",
                Name = "Run Tests",
                Description = "Run project tests",
                Shortcut = "F6",
                Icon = "\U0001F9EA",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 5),
                Execute = () => _vm.RequestTestRunner(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "memory-intake",
                Name = "Memory: Run Intake",
                Description = "Ingest CLAUDE.md, README, and other project sources into memory",
                Icon = "\U0001F9E0",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 4, 7),
                Execute = () =>
                {
                    if (_vm.SelectedTab is TerminalPairTabViewModel tab)
                    {
                        var eidet = App.Current.Services.GetService<IEidetService>();
                        if (eidet != null)
                            _ = eidet.RunIntakeAsync(tab.Pair.WorkingDirectory);
                    }
                },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "memory-browser",
                Name = "Memory Browser",
                Description = "Browse and manage memory entries",
                Shortcut = "Ctrl+Shift+M",
                Icon = "\U0001F9E0",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 4, 7),
                Execute = () => _vm.RequestMemoryBrowser(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "debug-log",
                Name = "Debug Log",
                Description = "Show diagnostic log for MCP, Memory, and Ollama",
                Icon = "\U0001F41B",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 4, 8),
                Execute = () => _vm.RequestDebugLog(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "debug-dump-panel-state",
                Name = "Debug: Dump Panel State",
                Description = "Copy a snapshot of the panel router (surfaces, registrations, active map) to the clipboard. For diagnosing dock/popup/window desync.",
                Icon = "\U0001F50D",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 5, 24),
                Execute = () => _vm.DumpPanelDiagnostics()
            }
        ];
    }
}
