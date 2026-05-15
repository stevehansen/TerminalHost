using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Spark Canvas category provider — real-time force-directed visualization
/// of AI agent sessions, plus standalone window and JSONL loader.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2d.
/// </summary>
internal sealed class SparkCanvasCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public SparkCanvasCommandProvider(MainViewModel vm)
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
                Id = "spark-canvas",
                Name = "Spark: Open Canvas",
                Description = "Open real-time force-directed AI session visualization",
                Shortcut = "Ctrl+Shift+J",
                Icon = "✨",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 27),
                Execute = () => _vm.OpenSparkCanvasCommand.Execute(null),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "spark-canvas-window",
                Name = "Spark: Open Canvas (Window)",
                Description = "Open Spark Canvas in a standalone window",
                Icon = "✨",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 27),
                Execute = () => _vm.OpenSparkCanvasWindow()
            },
            new() {
                Id = "spark-load-jsonl",
                Name = "Spark: Load JSONL File",
                Description = "Open a .jsonl transcript file in Spark Canvas for visualization",
                Icon = "✨",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 3, 30),
                Execute = () => _vm.OpenSparkCanvasAndLoadJsonl()
            }
        ];
    }
}
