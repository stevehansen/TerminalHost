using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Run category provider — project runner start/stop/restart/toggle/open-url.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2c.
/// </summary>
internal sealed class RunCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public RunCommandProvider(MainViewModel vm)
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
                Id = "run-start",
                Name = "Run: Start",
                Description = "Start the project",
                Shortcut = "F5",
                Icon = "▶",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab && tab.CanRun) tab.StartRunCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel { CanRun: true }
            },
            new() {
                Id = "run-stop",
                Name = "Run: Stop",
                Description = "Stop the running project",
                Shortcut = "Shift+F5",
                Icon = "⏹",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab && tab.CanStop) tab.StopRunCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel { CanStop: true }
            },
            new() {
                Id = "run-restart",
                Name = "Run: Restart",
                Description = "Restart the running project",
                Icon = "🔄",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.RestartRunCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel { RunState: RunState.Running }
            },
            new() {
                Id = "run-toggle-terminal",
                Name = "Run: Toggle Terminal",
                Description = "Show/hide run terminal panel",
                Icon = "📺",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.ToggleRunTerminalCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "run-open-url",
                Name = "Run: Open URL",
                Description = "Open detected localhost URL in browser",
                Icon = "🌐",
                Category = "Run",
                IntroducedOn = new DateOnly(2025, 12, 12),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab && !string.IsNullOrEmpty(tab.DetectedRunUrl)) _vm.RunUrlDetectionService.OpenInBrowser(tab.DetectedRunUrl); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel { HasDetectedRunUrl: true }
            }
        ];
    }
}
