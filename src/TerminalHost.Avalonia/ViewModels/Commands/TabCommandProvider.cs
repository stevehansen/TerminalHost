using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Tab/Project category provider — open new projects, close/duplicate/move
/// tabs, close-other/close-to-right, and the tab switcher popup.
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
                Id = "move-tab-to-front",
                Name = "Move Tab to Front",
                Description = "Move current tab to the beginning",
                Icon = "⏮",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (_vm.SelectedTab != null) _vm.MoveTabToFrontCommand.Execute(_vm.SelectedTab); },
                CanExecute = () => _vm.SelectedTab != null && _vm.Tabs.IndexOf(_vm.SelectedTab) > 0
            },
            new() {
                Id = "move-tab-to-end",
                Name = "Move Tab to End",
                Description = "Move current tab to the end",
                Icon = "⏭",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (_vm.SelectedTab != null) _vm.MoveTabToEndCommand.Execute(_vm.SelectedTab); },
                CanExecute = () => _vm.SelectedTab != null && _vm.Tabs.IndexOf(_vm.SelectedTab) < _vm.Tabs.Count - 1
            },
            new() {
                Id = "close-other-tabs",
                Name = "Close Other Tabs",
                Description = "Close all tabs except current",
                Icon = "\U0001F5D1",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (_vm.SelectedTab != null) _vm.CloseOtherTabsCommand.Execute(_vm.SelectedTab); },
                CanExecute = () => _vm.SelectedTab != null && _vm.Tabs.Count > 1
            },
            new() {
                Id = "close-tabs-to-right",
                Name = "Close Tabs to Right",
                Description = "Close all tabs to the right of current",
                Icon = "➡️",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { if (_vm.SelectedTab != null) _vm.CloseTabsToRightCommand.Execute(_vm.SelectedTab); },
                CanExecute = () => _vm.SelectedTab != null && _vm.Tabs.IndexOf(_vm.SelectedTab) < _vm.Tabs.Count - 1
            },
            new() {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "\U0001F50D",
                Category = "Tab",
                IntroducedOn = new DateOnly(2025, 12, 13),
                Execute = () => { _vm.IsTabSwitcherOpen = true; _vm.SwitcherSearchText = ""; }
            }
        ];
    }
}
