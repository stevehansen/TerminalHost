using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// Owns the command palette's UI-facing state (open/close, search text, selection, filtered list)
/// and aggregates commands from the static <see cref="ICommandPalette"/> with dynamic profile and
/// Claude slash-command entries. Constructed by MainViewModel with host callbacks so the static
/// provider set and the dynamic feeds share one filter + MRU pipeline.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject, IDisposable
{
    private readonly ICommandPalette _palette;
    private readonly IProfileRegistry _profileRegistry;
    private readonly IClaudeCommandService _claudeCommandService;
    private readonly IConfigurationService _configService;
    private readonly IDispatcherService _dispatcher;
    private readonly Func<string?> _currentWorkingDirectory;
    private readonly Action<Profile> _openProfileTab;
    private readonly Action<ClaudeCommand> _executeClaudeCommand;
    private readonly ObservableCollection<PaletteCommand> _filtered = [];
    private readonly EventHandler _claudeChanged;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private PaletteCommand? _selected;

    public ReadOnlyObservableCollection<PaletteCommand> Filtered { get; }

    public CommandPaletteViewModel(
        ICommandPalette palette,
        IProfileRegistry profileRegistry,
        IClaudeCommandService claudeCommandService,
        IConfigurationService configService,
        IDispatcherService dispatcher,
        Func<string?> currentWorkingDirectory,
        Action<Profile> openProfileTab,
        Action<ClaudeCommand> executeClaudeCommand)
    {
        _palette = palette;
        _profileRegistry = profileRegistry;
        _claudeCommandService = claudeCommandService;
        _configService = configService;
        _dispatcher = dispatcher;
        _currentWorkingDirectory = currentWorkingDirectory;
        _openProfileTab = openProfileTab;
        _executeClaudeCommand = executeClaudeCommand;

        Filtered = new ReadOnlyObservableCollection<PaletteCommand>(_filtered);

        // FileSystemWatcher raises CommandsChanged on the thread pool; refilter on the UI thread.
        _claudeChanged = (_, _) => _dispatcher.BeginInvoke(Refilter);
        _claudeCommandService.CommandsChanged += _claudeChanged;
    }

    [RelayCommand]
    private void ExecuteSelected()
    {
        if (Selected is null) return;
        RecordMru(Selected.Id);
        IsOpen = false;
        Selected.Execute();
    }

    partial void OnSearchTextChanged(string value) => Refilter();

    partial void OnIsOpenChanged(bool value)
    {
        if (!value) return;
        SearchText = "";
        Refilter();
        Selected = _filtered.FirstOrDefault();
    }

    // Currently dead code: MainViewModel is a singleton with the app lifetime
    // and does not implement IDisposable. Kept for shape correctness so the
    // CommandsChanged subscription gets a matching tear-down if the host
    // ever owns the sub-VM's disposal.
    public void Dispose()
    {
        _claudeCommandService.CommandsChanged -= _claudeChanged;
    }

    /// <summary>
    /// Static + profile-launch + Claude-command aggregation, sorted by MRU then alphabetically.
    /// Ported verbatim from the historical MainViewModel.FilterPaletteCommands pipeline.
    /// </summary>
    private void Refilter()
    {
        _filtered.Clear();
        var searchText = SearchText?.ToLower() ?? "";
        var allCommands = new List<PaletteCommand>();

        allCommands.AddRange(_palette.Filter(searchText));

        foreach (var profile in _profileRegistry.Profiles)
        {
            var profileName = $"Launch: {profile.Name}";
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                               profileName.ToLower().Contains(searchText) ||
                               "profile".Contains(searchText) ||
                               "launch".Contains(searchText);

            if (matchesSearch)
            {
                var capturedProfile = profile;
                allCommands.Add(new PaletteCommand
                {
                    Id = $"launch-profile-{profile.Id}",
                    Name = profileName,
                    Description = profile.Command,
                    Shortcut = profile.Shortcut ?? "",
                    Icon = profile.Icon ?? "▶",
                    Category = "Profile",
                    Execute = () => _openProfileTab(capturedProfile),
                });
            }
        }

        var currentWorkingDir = _currentWorkingDirectory();
        var claudeCommands = _claudeCommandService.GetAllCommands(currentWorkingDir);

        foreach (var cmd in claudeCommands)
        {
            var commandName = $"Claude: /{cmd.FullName}";
            var matchesSearch = string.IsNullOrEmpty(searchText) ||
                               commandName.ToLower().Contains(searchText) ||
                               (cmd.Description?.ToLower().Contains(searchText) ?? false) ||
                               (cmd.PluginName?.ToLower().Contains(searchText) ?? false) ||
                               "claude".Contains(searchText) ||
                               "plugin".Contains(searchText);

            if (matchesSearch)
            {
                var capturedCmd = cmd;
                var category = cmd.Source switch
                {
                    ClaudeCommandSource.Global => "Claude (Global)",
                    ClaudeCommandSource.Project => "Claude (Project)",
                    ClaudeCommandSource.Plugin => $"Claude (Plugin: {cmd.PluginName})",
                    _ => "Claude",
                };

                allCommands.Add(new PaletteCommand
                {
                    Id = $"claude-cmd-{cmd.Id}",
                    Name = commandName,
                    Description = cmd.Description ?? cmd.FilePath,
                    Shortcut = cmd.Shortcut ?? "",
                    Icon = "🤖",
                    Category = category,
                    Execute = () => _executeClaudeCommand(capturedCmd),
                });
            }
        }

        var mruList = _configService.Load().CommandPaletteMru;
        var sortedCommands = allCommands
            .OrderBy(c =>
            {
                var mruIndex = mruList.IndexOf(c.Id);
                return mruIndex >= 0 ? mruIndex : int.MaxValue;
            })
            .ThenBy(c => c.Name)
            .ToList();

        foreach (var command in sortedCommands)
            _filtered.Add(command);

        Selected = _filtered.FirstOrDefault();
    }

    /// <summary>
    /// Bumps the executed command to the head of the persisted MRU list (capped at 30).
    /// Ported verbatim from the historical MainViewModel.UpdateCommandMru.
    /// </summary>
    private void RecordMru(string commandId)
    {
        var config = _configService.Load();
        config.CommandPaletteMru.Remove(commandId);
        config.CommandPaletteMru.Insert(0, commandId);
        if (config.CommandPaletteMru.Count > 30)
            config.CommandPaletteMru.RemoveRange(30, config.CommandPaletteMru.Count - 30);
        _configService.Save(config);
    }
}
