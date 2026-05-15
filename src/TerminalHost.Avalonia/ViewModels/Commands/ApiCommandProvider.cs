using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// API category provider — REST API server commands.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2b.
/// </summary>
internal sealed class ApiCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public ApiCommandProvider(MainViewModel vm)
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
                Id = "api-start",
                Name = "API: Start Server",
                Description = "Start the REST API server",
                Icon = "🌐",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 24),
                Execute = () => _ = _vm.StartApiServerAsync(),
                CanExecute = () => _vm._apiServer != null && !_vm._apiServer.IsRunning
            },
            new() {
                Id = "api-stop",
                Name = "API: Stop Server",
                Description = "Stop the REST API server",
                Icon = "🌐",
                Category = "API",
                IntroducedOn = new DateOnly(2026, 2, 24),
                Execute = () => _ = _vm.StopApiServerAsync(),
                CanExecute = () => _vm._apiServer != null && _vm._apiServer.IsRunning
            }
        ];
    }
}
