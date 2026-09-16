using System.IO;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// App category provider — top-level shell commands: switch terminal, open
/// settings/profiles/setup, help, crash log folder, and What's New.
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2e.
/// </summary>
internal sealed class AppCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public AppCommandProvider(MainViewModel vm)
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
                Id = "switch-terminal",
                Name = "Switch Terminal",
                Description = "Toggle between custom and shell",
                Shortcut = "Ctrl+`",
                Icon = "⇄",
                Category = "Terminal",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.SwitchActiveTerminalCommand.Execute(null),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "settings",
                Name = "Settings",
                Description = "Open settings editor",
                Shortcut = "Ctrl+,",
                Icon = "⚙️",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenSettingsCommand.Execute(null)
            },
            new() {
                Id = "profiles",
                Name = "Settings: Profiles",
                Description = "Open settings and manage terminal profiles",
                Shortcut = "Ctrl+P",
                Icon = "\U0001F464",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenProfilesCommand.Execute(null)
            },
            new() {
                Id = "setup",
                Name = "Setup",
                Description = "Check dependencies and setup",
                Icon = "\U0001F527",
                Category = "Settings",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenSetupCommand.Execute(null)
            },
            new() {
                Id = "help",
                Name = "Help",
                Description = "Show keyboard shortcuts",
                Shortcut = "F1",
                Icon = "❓",
                Category = "Help",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenHelpCommand.Execute(null)
            },
            new() {
                Id = "open-crash-log-folder",
                Name = "Open Crash Log Folder",
                Description = "Open the folder with app crash reports",
                Icon = "\U0001FA7A",
                Category = "Help",
                IntroducedOn = new DateOnly(2026, 2, 13),
                Execute = () =>
                {
                    var crashLogDirectory = MainViewModel.GetCrashLogDirectoryPath();
                    Directory.CreateDirectory(crashLogDirectory);
                    _vm._processService.OpenFolder(crashLogDirectory);
                }
            },
            new() {
                Id = "whats-new",
                Name = "What's New",
                Description = "View recently added features",
                Shortcut = "Ctrl+F1",
                Icon = "✨",
                Category = "Help",
                IntroducedOn = new DateOnly(2026, 2, 10),
                Execute = () => _vm.RequestWhatsNew()
            }
        ];
    }
}
