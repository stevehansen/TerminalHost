using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// Git category provider — Changes, Branches, History, Stash, Compare,
/// Pull, Push, Reflog, Repository Switcher.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2c.
/// Most Git commands are not yet implemented in Avalonia and remain TODO stubs.
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
                Execute = () => _vm.RequestGitChanges(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-commit",
                Name = "Git Commit",
                Description = "Stage files, write message, and commit from the Changes panel (Alt+G)",
                Icon = "💾",
                Category = "Git",
                IntroducedOn = new DateOnly(2026, 2, 11),
                Execute = () => _vm.RequestGitChanges(),
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
                Execute = () => { /* Needs to be improved */ },
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "git-pull",
                Name = "Git Pull",
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
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
                Execute = () => { }, // TODO: Not yet implemented in Avalonia
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
