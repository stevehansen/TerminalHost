using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Help popup (F1).
/// Provides all keyboard shortcuts, tips, and usage information.
/// </summary>
public partial class HelpViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;

    public HelpViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    #region Keyboard Shortcuts

    public List<ShortcutSection> ShortcutSections { get; } =
    [
        new("Tab Navigation",
        [
            new("Ctrl+PageDown", "Next tab"),
            new("Ctrl+PageUp", "Previous tab"),
            new("Ctrl+1-9", "Jump to tab 1-9"),
            new("Ctrl+Shift+T", "Open tab switcher (search tabs)"),
            new("Ctrl+W", "Close current tab"),
            new("Middle-click tab", "Close tab"),
        ]),

        new("Terminal",
        [
            new("Ctrl+`", "Switch between Custom/Shell terminal"),
            new("Links button", "Click to view detected URLs and file paths"),
        ]),

        new("Layout",
        [
            new("Ctrl+L", "Toggle layout mode (Tabs/Sidebar)"),
        ]),

        new("File Operations",
        [
            new("Ctrl+N", "Open new project (folder picker)"),
            new("Ctrl+E", "Open current folder in Explorer"),
            new("Ctrl+O", "Open file preview dialog"),
            new("Ctrl+Shift+E", "Open file editor"),
            new("Ctrl+Shift+F", "Toggle file explorer panel"),
            new("Ctrl+F3", "Search across files"),
        ]),

        new("Application",
        [
            new("Ctrl+,", "Open settings editor"),
            new("Ctrl+P", "Open settings (Profiles)"),
            new("Ctrl+Shift+P", "Open command palette"),
            new("Ctrl+Shift+N", "Open scratch pad (notes)"),
            new("Ctrl+T", "Open task panel"),
            new("Ctrl+Shift+Q", "Quick add task"),
            new("Ctrl+Shift+M", "Quick add note"),
            new("Ctrl+G", "Open git changes panel"),
            new("Ctrl+H", "Open commit history"),
            new("Ctrl+B", "Open git branch switcher"),
            new("Ctrl+Shift+S", "Open git stash manager"),
            new("Ctrl+Shift+G", "Open git reflog"),
            new("Ctrl+Shift+B", "View file blame"),
            new("Ctrl+Shift+O", "Repository quick access"),
            new("Ctrl+Shift+H", "GitHub Dashboard"),
            new("Ctrl+Shift+R", "PR Review Mode"),
            new("F1", "Show this help window"),
            new("F6", "Run tests"),
            new("Ctrl+M", "Markdown preview"),
        ]),

        new("Project Runner",
        [
            new("F5", "Start/Stop project run"),
            new("Shift+F5", "Force stop project run"),
        ]),
    ];

    public List<ShortcutSection> QuickCommandSections { get; } =
    [
        new("Default Quick Commands (configurable in Settings)",
        [
            new("Ctrl+Shift+C", "Commit - send 'commit' to Claude Code"),
            new("Ctrl+Shift+D", "Pull - run 'git pull --rebase' in Shell"),
            new("Ctrl+Shift+U", "Push - run 'git push' in Shell"),
            new("Ctrl+Shift+L", "Launch IDE - run 'dev' in Shell"),
            new("Ctrl+Shift+V", "Review PR - Claude Code prompt"),
        ]),
    ];

    #endregion

    #region Tips

    public List<string> Tips { get; } =
    [
        "Drag tabs to reorder them",
        "Use the splitter between terminals to adjust the split ratio (saved per directory)",
        "File preview supports syntax highlighting for: JSON, C#, JavaScript/TypeScript, Python, XML, Markdown, CSV/TSV",
        "Configure custom link patterns in Settings to make ticket IDs clickable",
    ];

    #endregion

    #region Command Line Usage

    public List<CommandLineExample> CommandLineExamples { get; } =
    [
        new("host", "Open/focus app"),
        new("host .", "Open project from current directory"),
        new("host P:\\MyProject", "Open specific project"),
        new("host -w P:\\Path", "Using named argument"),
        new("host -multi", "Allow multiple instances"),
        new("host -data path", "Override config path"),
    ];

    #endregion

    #region Important Paths

    public List<ImportantPath> ImportantPaths { get; } =
    [
        new("Config file:", "%APPDATA%\\TerminalHost\\config.json"),
    ];

    #endregion

    #region Commands

    [RelayCommand]
    private void Close()
    {
        _mainViewModel.IsHelpOpen = false;
    }

    #endregion
}
