using TerminalHost.Core.Domain;
using TerminalHost.Core.Workspace;

namespace TerminalHost.ViewModels;

/// <summary>
/// File category provider — preview/edit files, open in Explorer, plus the
/// file-related panel toggles (file explorer, in-file search, markdown preview).
/// Split out of <see cref="MainViewModelStaticCommandProvider"/> in Step 2e.
/// </summary>
internal sealed class FileCommandProvider : ICommandProvider
{
    private readonly MainViewModel _vm;
    // UI-thread-only; cache is not thread-safe.
    private IReadOnlyList<PaletteCommand>? _cached;

    public FileCommandProvider(MainViewModel vm)
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
                Id = "file-preview",
                Name = "Preview File",
                Description = "Open file preview",
                Shortcut = "Ctrl+O",
                Icon = "\U0001F441",
                Category = "File",
                IntroducedOn = new DateOnly(2025, 12, 19),
                Execute = () => _vm.RequestFilePreview("", 0, 0)
            },
            new() {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                IntroducedOn = new DateOnly(2025, 12, 19),
                Execute = () => { /* Needs to be improved */ }
            },
            new() {
                Id = "open-explorer",
                Name = "Open in Explorer",
                Description = "Open folder in file explorer",
                Shortcut = "Ctrl+E",
                Icon = "\U0001F4C2",
                Category = "File",
                IntroducedOn = new DateOnly(2025, 12, 11),
                Execute = () => _vm.OpenInExplorerCommand.Execute(null),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "file-explorer",
                Name = "File Explorer",
                Description = "Toggle file explorer panel",
                Shortcut = "Ctrl+Shift+F",
                Icon = "\U0001F4C1",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 22),
                Execute = () => { if (_vm.SelectedTab is TerminalPairTabViewModel tab) tab.ToggleExplorerCommand.Execute(null); },
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "file-search",
                Name = "Search in Files",
                Description = "Search across files",
                Shortcut = "Ctrl+F3",
                Icon = "\U0001F50D",
                Category = "Tools",
                IntroducedOn = new DateOnly(2026, 2, 7),
                Execute = () => _vm.RequestSearch(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            },
            new() {
                Id = "markdown-preview",
                Name = "Markdown Preview",
                Description = "Preview markdown files",
                Shortcut = "Ctrl+M",
                Icon = "\U0001F4C4",
                Category = "Tools",
                IntroducedOn = new DateOnly(2025, 12, 18),
                Execute = () => _vm.RequestMarkdownPreview(),
                CanExecute = () => _vm.SelectedTab is TerminalPairTabViewModel
            }
        ];
    }
}
