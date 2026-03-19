// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Commands;
using TerminalHost.CmdPal.Dock;
using TerminalHost.CmdPal.Helpers;
using TerminalHost.CmdPal.Pages;

namespace TerminalHost.CmdPal;

/// <summary>
/// Main command provider for the TerminalHost extension.
/// Supplies top-level commands visible in CmdPal search and dock bands
/// for the persistent toolbar.
/// </summary>
public sealed partial class TerminalHostCommandsProvider : CommandProvider
{
    private readonly ApiClient _api = new();
    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[]? _dockBands;

    internal static readonly IconInfo ExtensionIcon = new("\uE756"); // DeviceLaptopNoPic

    public TerminalHostCommandsProvider()
    {
        DisplayName = "TerminalHost";
        Id = "com.terminalhost.cmdpal";

        // Top-level commands shown in CmdPal search
        var workspacesPage = new WorkspacesPage(_api);
        var gitStatusPage = new GitStatusPage(_api);
        var tasksPage = new TasksPage(_api);
        var focusCommand = new FocusWindowCommand();
        var openProjectCommand = new OpenProjectCommand();

        _commands =
        [
            new CommandItem(workspacesPage)
            {
                Title = "TerminalHost: Workspaces",
                Subtitle = "Switch between open project tabs",
                Icon = new IconInfo("\uE8FC"), // AppList
            },
            new CommandItem(gitStatusPage)
            {
                Title = "TerminalHost: Git Status",
                Subtitle = "View git branch, changes, and recent commits",
                Icon = new IconInfo("\uE8CB"), // RepoBranch
            },
            new CommandItem(tasksPage)
            {
                Title = "TerminalHost: Tasks",
                Subtitle = "View active tasks and Claude Code activity",
                Icon = new IconInfo("\uE9D5"), // TaskList
            },
            new CommandItem(focusCommand)
            {
                Title = "TerminalHost: Focus Window",
                Subtitle = "Bring TerminalHost to the foreground",
                Icon = new IconInfo("\uE8A7"), // OpenInNewWindow
            },
            new CommandItem(openProjectCommand)
            {
                Title = "TerminalHost: Open Project",
                Subtitle = "Open a folder in TerminalHost",
                Icon = new IconInfo("\uE8DA"), // OpenFolderHorizontal
            },
        ];

        // Build dock band
        _dockBands = BuildDockBands();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => _dockBands;

    private ICommandItem[]? BuildDockBands()
    {
        var dockBand = new StatusDockBand(_api);
        var wrapped = new WrappedDockItem(
            [dockBand],
            "com.terminalhost.cmdpal.dock.status",
            "TerminalHost");

        return [wrapped];
    }
}
