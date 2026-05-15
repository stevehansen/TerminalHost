using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Layout category provider — app layout mode (Tabs vs Workspace Sidebar)
/// and per-tab terminal layout (custom full / horizontal / vertical split).
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2d.
/// </summary>
internal sealed class LayoutCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public LayoutCommandProvider(MainViewModel vm)
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
                Id = "toggle-layout-mode",
                Name = "Toggle Layout Mode",
                Description = "Switch between Tabs and Workspace Sidebar layout",
                Shortcut = "Ctrl+L",
                Icon = "📐",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () => _vm.ToggleLayoutModeCommand.Execute(null)
            },
            new() {
                Id = "toggle-sidebar",
                Name = "Toggle Sidebar",
                Description = "Collapse/expand the workspace sidebar",
                Shortcut = "Ctrl+Shift+L",
                Icon = "📎",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
                CanExecute = () => _vm.LayoutMode == AppLayoutMode.WorkspaceSidebar
            },
            new() {
                Id = "switch-to-tabs",
                Name = "Switch to Tabs Layout",
                Description = "Use traditional tab bar layout",
                Icon = "🗂",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () =>
                {
                    _vm.LayoutMode = AppLayoutMode.Tabs;
                    var config = _vm._configService.Load();
                    config.Settings.LayoutMode = _vm.LayoutMode;
                    _vm._configService.Save(config);
                },
                CanExecute = () => _vm.LayoutMode != AppLayoutMode.Tabs
            },
            new() {
                Id = "switch-to-sidebar",
                Name = "Switch to Sidebar Layout",
                Description = "Use workspace sidebar layout",
                Icon = "📂",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 25),
                Execute = () =>
                {
                    _vm.LayoutMode = AppLayoutMode.WorkspaceSidebar;
                    var config = _vm._configService.Load();
                    config.Settings.LayoutMode = _vm.LayoutMode;
                    _vm._configService.Save(config);
                },
                CanExecute = () => _vm.LayoutMode != AppLayoutMode.WorkspaceSidebar
            },
            new() {
                Id = "layout-custom-full",
                Name = "Layout: Custom Full",
                Description = "Show only custom terminal",
                Icon = "🖥",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.SetCustomFullLayoutCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "layout-horizontal-split",
                Name = "Layout: Horizontal Split",
                Description = "Side-by-side terminals",
                Icon = "⬜",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.SetHorizontalSplitLayoutCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "layout-vertical-split",
                Name = "Layout: Vertical Split",
                Description = "Top-bottom terminals",
                Icon = "⬛",
                Category = "Layout",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.SetVerticalSplitLayoutCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
