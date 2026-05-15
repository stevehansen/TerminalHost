using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Status Overlay category provider — toggle the floating activity overlay,
/// spawn additional overlay instances, close all overlays.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2d.
/// </summary>
internal sealed class StatusOverlayCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public StatusOverlayCommandProvider(MainViewModel vm)
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
                Id = "toggle-status-overlay",
                Name = "Toggle Status Overlay",
                Description = "Show or hide the floating status overlay",
                Shortcut = "Ctrl+Shift+Y",
                Icon = "🔔",
                Category = "Application",
                IntroducedOn = new DateOnly(2026, 2, 25),
                Execute = () => _vm.StatusOverlayService?.Toggle()
            },
            new() {
                Id = "new-status-overlay",
                Name = "New Status Overlay",
                Description = "Create an additional floating status overlay instance",
                Icon = "🔔",
                Category = "Application",
                IntroducedOn = new DateOnly(2026, 2, 25),
                Execute = () => _vm.StatusOverlayService?.CreateOverlay()
            },
            new() {
                Id = "close-all-status-overlays",
                Name = "Close All Status Overlays",
                Description = "Close all floating status overlay windows",
                Icon = "🔔",
                Category = "Application",
                IntroducedOn = new DateOnly(2026, 2, 25),
                Execute = () => _vm.StatusOverlayService?.CloseAll(),
                CanExecute = () => _vm.StatusOverlayService?.OverlayCount > 0
            }
        ];
    }
}
