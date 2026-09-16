using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.ViewModels;
using TerminalHost.Domain;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Help popup (F1).
/// Provides all keyboard shortcuts, tips, and usage information.
/// </summary>
public partial class HelpViewModel : BasePanelViewModel
{
    public override string PanelId => "help";
    public override string PanelTitle => "Help";
    public override string PanelIcon => "❓";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    public string VersionString => _mainViewModel.VersionString;

    #region Keyboard Shortcuts

    /// <summary>
    /// Built-in keyboard shortcuts, sourced from ShortcutConflictService (single source of truth).
    /// </summary>
    public List<ShortcutSection> ShortcutSections => ShortcutConflictService.BuiltInShortcutSections;

    /// <summary>
    /// Default quick command shortcuts (these are user-configurable in Settings).
    /// </summary>
    public List<ShortcutSection> QuickCommandSections { get; } =
    [
        new("Default Quick Commands (configurable in Settings)",
        [
            new("Ctrl+Shift+C", "Commit - send 'commit' to Claude Code"),
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
        new("Crash logs:", "%APPDATA%\\TerminalHost\\logs\\"),
    ];

    #endregion
}
