using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Git category provider — Changes, Branches, History, Stash, Compare,
/// Pull, Push, Reflog, Repository Switcher.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2c.
/// </summary>
internal sealed class GitCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public GitCommandProvider(MainViewModel vm)
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
                Id = "git-changes",
                Name = "Git Changes",
                Description = "View modified files and diffs",
                Shortcut = "Alt+G",
                Icon = "📋",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.OpenUnifiedGitPanel(GitPanelTab.Changes),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-commit",
                Name = "Git Commit",
                Description = "Stage files, write message, and commit from the Changes panel (Alt+G)",
                Icon = "💾",
                Category = "Git",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => _vm.OpenUnifiedGitPanel(GitPanelTab.Changes),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-branches",
                Name = "Git Branches",
                Description = "Switch, create, or delete branches",
                Shortcut = "Ctrl+B",
                Icon = "🌿",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.OpenUnifiedGitPanel(GitPanelTab.Branches),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-history",
                Name = "Git History",
                Description = "View commit history",
                Shortcut = "Ctrl+H",
                Icon = "📜",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.OpenUnifiedGitPanel(GitPanelTab.History),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-stash",
                Name = "Git Stash",
                Description = "Manage stashed changes",
                Shortcut = "Ctrl+Shift+S",
                Icon = "📦",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.OpenUnifiedGitPanel(GitPanelTab.Stash),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-compare",
                Name = "Git Compare Branches",
                Description = "Compare two branches",
                Shortcut = "Ctrl+Alt+B",
                Icon = "🔀",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.OpenUnifiedGitPanel(GitPanelTab.Comparison),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-pull",
                Name = "Git Pull",
                NameProvider = () => {
                    var behind = (_vm.SelectedTab as TerminalPairTabViewModel)?.GitStatus?.BehindCount ?? 0;
                    return behind > 0 ? $"Git Pull (↓{behind})" : "Git Pull";
                },
                Description = "Pull with auto-stash and rebase",
                Shortcut = "Ctrl+Shift+D",
                Icon = "⬇",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.GitPullCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-push",
                Name = "Git Push",
                NameProvider = () => {
                    var ahead = (_vm.SelectedTab as TerminalPairTabViewModel)?.GitStatus?.AheadCount ?? 0;
                    return ahead > 0 ? $"Git Push (↑{ahead})" : "Git Push";
                },
                Description = "Push to remote",
                Shortcut = "Ctrl+Shift+U",
                Icon = "⬆",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.GitPushCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-reflog",
                Name = "Git Reflog",
                Description = "View reference log",
                Shortcut = "Ctrl+Shift+G",
                Icon = "📋",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.RequestReflog(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-repository-switcher",
                Name = "Switch Repository",
                Description = "Open repository switcher",
                Shortcut = "Ctrl+Shift+O",
                Icon = "🔄",
                Category = "Git",
                IntroducedOn = new DateOnly(2025, 12, 29),
                Execute = () => _vm.RequestRepositorySwitcher(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
