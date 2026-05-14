using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Seed provider that returns the full static palette block previously hand-rolled
/// in <see cref="MainViewModel.InitializeCommandPalette"/>. In Step 2b this block
/// will be split into per-feature providers (Git, Container, AI, etc.); for Step 2a
/// the block stays intact and is sourced from MainViewModel so the lambdas keep
/// their existing closure semantics without bulk access-modifier churn.
/// </summary>
internal sealed class MainViewModelStaticCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    private IReadOnlyList<PaletteCommand>? _cached;

    public MainViewModelStaticCommandProvider(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public IEnumerable<PaletteCommand> GetCommands(ICommandContext ctx)
    {
        return _cached ??= _vm.BuildStaticPaletteCommands();
    }
}
