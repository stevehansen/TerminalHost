using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Catch-all provider hosting the static palette block in MainViewModel that
/// has not yet been extracted into a per-feature provider. Step 2b peeled off
/// Container, API, and Voice; Step 2c peeled off Git, Run, AI, and GitHub.
/// Remaining categories (Timeline, SparkCanvas, Markdown, Channel, StatusOverlay,
/// Layout, Panel toggles, Settings toggles, What's New, core Tab/File/Terminal/
/// Help/Tools) may be split in later sub-steps of issue #48.
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
