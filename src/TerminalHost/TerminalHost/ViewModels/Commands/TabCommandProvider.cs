using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Tab/Project category provider — open new projects, close/duplicate/reload
/// tabs, and the tab switcher popup.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2e.
/// </summary>
internal sealed class TabCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public TabCommandProvider(MainViewModel vm)
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
                Id = "new-project",
                Name = "New Project",
                Description = "Open folder as new project",
                Shortcut = "Ctrl+N",
                Icon = "\U0001F4C1",
                Category = "Project",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenNewProjectCommand.Execute(null)
            },
            new() {
                Id = "close-tab",
                Name = "Close Tab",
                Description = "Close current tab",
                Shortcut = "Ctrl+W",
                Icon = "✕",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (_vm.SelectedTab != null) _vm.CloseTabCommand.Execute(_vm.SelectedTab); }
            },
            new() {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "\U0001F50D",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => _vm.OpenTabSwitcherCommand.Execute(null)
            },
            new() {
                Id = "duplicate-tab",
                Name = "Duplicate Tab",
                Description = "Open new tab for same directory",
                Icon = "\U0001F4CB",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) _vm.DuplicateTabCommand.Execute(tab); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "tab-reload",
                Name = "Reload Tab",
                Description = "Close and reopen the current project tab (applies pending changes like container toggle)",
                Icon = "\U0001F504",
                Category = "Tab",
                IntroducedOn = new DateOnly(2026, 3, 21),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) _vm.ReloadTerminalTab(tab); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
