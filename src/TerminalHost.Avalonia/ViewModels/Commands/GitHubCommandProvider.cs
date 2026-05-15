using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// GitHub category provider — Dashboard, PR Review.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2c.
/// </summary>
internal sealed class GitHubCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public GitHubCommandProvider(MainViewModel vm)
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
                Id = "dashboard",
                Name = "Dashboard",
                Description = "View GitHub PRs, issues, and CI status",
                Shortcut = "Ctrl+Shift+H",
                Icon = "🏠",
                Category = "GitHub",
                IntroducedOn = new DateOnly(2025, 12, 18),
                Execute = () => _vm.OpenDashboardCommand.Execute(null)
            },
            new() {
                Id = "pr-review",
                Name = "PR Review Mode",
                Description = "Review the current branch's pull request",
                Shortcut = "Ctrl+Shift+R",
                Icon = "📝",
                Category = "GitHub",
                IntroducedOn = new DateOnly(2025, 12, 18),
                Execute = () => _vm.RequestPrReview(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
