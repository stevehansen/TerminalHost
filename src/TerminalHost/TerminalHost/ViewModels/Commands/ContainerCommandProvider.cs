using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Container category provider — Docker workspace isolation commands.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2b.
/// </summary>
internal sealed class ContainerCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public ContainerCommandProvider(MainViewModel vm)
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
                Id = "container-toggle",
                Name = "Container: Toggle for Current Workspace",
                Description = "Enable or disable Docker container isolation for the active workspace",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _vm.ToggleContainerForCurrentWorkspace(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-rebuild-image",
                Name = "Container: Rebuild Image",
                Description = "Rebuild the Docker workspace image from Dockerfile",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _ = _vm.RebuildContainerImageAsync()
            },
            new() {
                Id = "container-stop",
                Name = "Container: Stop Current",
                Description = "Stop the Docker container for the active workspace",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _ = _vm.StopCurrentContainerAsync(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-remove",
                Name = "Container: Remove Current",
                Description = "Remove the Docker container for the active workspace",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _ = _vm.RemoveCurrentContainerAsync(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-recreate",
                Name = "Container: Recreate Current",
                Description = "Remove and recreate the container (applies settings changes)",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 23),
                Execute = () => _ = _vm.RecreateCurrentContainerAsync(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "container-list",
                Name = "Container: List All",
                Description = "Show all TerminalHost Docker containers",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _ = _vm.ListContainersAsync()
            },
            new() {
                Id = "container-clean",
                Name = "Container: Clean Stopped",
                Description = "Remove all stopped Docker containers",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _ = _vm.CleanStoppedContainersAsync()
            },
            new() {
                Id = "container-check-docker",
                Name = "Container: Check Docker Status",
                Description = "Verify Docker Desktop is available and running",
                Icon = "🐳",
                Category = "Container",
                IntroducedOn = new DateOnly(2026, 3, 19),
                Execute = () => _ = _vm.CheckDockerStatusAsync()
            }
        ];
    }
}
