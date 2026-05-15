using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// AI workflow category provider — AI-assisted commands across git, PRs,
/// markdown, CI, and more. Each command is routed through the AI panel.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2c.
/// </summary>
internal sealed class AiCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public AiCommandProvider(MainViewModel vm)
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
                Id = "ai-explain-blame",
                Name = "Explain blame line (AI)",
                Description = "AI explains why a blame line was changed",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("explain-blame"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-summarize-file-history",
                Name = "Summarize file history (AI)",
                Description = "AI summarizes a file's commit history",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("summarize-file-history"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-explain-commit",
                Name = "Explain commit (AI)",
                Description = "AI explains what a commit does and why",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("explain-commit"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-explain-reflog",
                Name = "Explain recent git operations (AI)",
                Description = "AI explains recent reflog entries",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("explain-reflog"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-generate-stash-name",
                Name = "Generate stash name (AI)",
                Description = "AI generates a descriptive stash name",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("generate-stash-name"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-assess-merge-risk",
                Name = "Assess merge risk (AI)",
                Description = "AI assesses risk of merging compared branches",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("assess-merge-risk"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-suggest-version",
                Name = "Suggest next version (AI)",
                Description = "AI suggests next semantic version based on tags and commits",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("suggest-version"),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "ai-analyze-ci-failure",
                Name = "Analyze CI failure (AI)",
                Description = "AI analyzes a failed CI check",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("analyze-ci-failure")
            },
            new() {
                Id = "ai-prioritize-prs",
                Name = "Prioritize PRs for review (AI)",
                Description = "AI prioritizes open PRs by review urgency",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("prioritize-prs")
            },
            new() {
                Id = "ai-improve-markdown",
                Name = "Improve markdown (AI)",
                Description = "AI suggests improvements to open markdown file",
                Icon = "✨",
                Category = "AI",
                IntroducedOn = new DateOnly(2026, 2, 23),
                Execute = () => _vm.RequestAiPanelCommand("improve-markdown")
            }
        ];
    }
}
