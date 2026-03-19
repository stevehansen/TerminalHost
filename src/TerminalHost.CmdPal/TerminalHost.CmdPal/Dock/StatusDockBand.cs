// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Dock;

/// <summary>
/// A dock band item that shows live TerminalHost status.
/// Polls the REST API every 5 seconds and updates Title/Subtitle dynamically.
/// Clicking opens the WorkspacesPage.
/// </summary>
internal sealed partial class StatusDockBand : ListItem
{
    private readonly ApiClient _api;
    private readonly Timer _pollTimer;

    private static readonly IconInfo BusyIcon = new("\uE9F5");        // ProgressRing
    private static readonly IconInfo WaitingIcon = new("\uE8EE");     // Warning
    private static readonly IconInfo IdleIcon = new("\uE756");        // DeviceLaptopNoPic
    private static readonly IconInfo OfflineIcon = new("\uE871");     // DisconnectDrive

    public StatusDockBand(ApiClient api)
        : base(new OpenWorkspacesCommand())
    {
        _api = api;

        Title = "TerminalHost";
        Subtitle = "Loading...";
        Icon = IdleIcon;

        // Poll every 5 seconds
        _pollTimer = new Timer(OnPoll, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private async void OnPoll(object? state)
    {
        try
        {
            var repos = await _api.GetReposAsync();

            if (repos == null || repos.Repos.Count == 0)
            {
                Title = "TerminalHost";
                Subtitle = _api.IsAvailable ? "No open tabs" : "Not connected";
                Icon = _api.IsAvailable ? IdleIcon : OfflineIcon;
                return;
            }

            // Find the active tab
            var active = repos.Repos.Find(r => r.IsActive) ?? repos.Repos[0];
            var parts = new List<string>();

            // Git branch
            if (active.Git?.Branch != null)
                parts.Add(active.Git.Branch);

            // Changed files count
            if (active.Git != null && active.Git.ChangedFiles > 0)
                parts.Add($"{active.Git.ChangedFiles} changed");
            else if (active.Git != null)
                parts.Add("clean");

            // Activity state
            var actState = active.ActivityIndicator?.State ?? "idle";
            if (actState == "busy")
            {
                parts.Add("busy");
                Icon = BusyIcon;
            }
            else if (actState == "waiting")
            {
                parts.Add("waiting");
                Icon = WaitingIcon;
            }
            else
            {
                Icon = IdleIcon;
            }

            Title = active.Title;
            Subtitle = string.Join(" | ", parts);
        }
        catch
        {
            Title = "TerminalHost";
            Subtitle = "Not connected";
            Icon = OfflineIcon;
        }
    }
}

/// <summary>
/// Command that opens the WorkspacesPage when the dock band is clicked.
/// This is a placeholder — the actual page navigation happens via CmdPal
/// when the user clicks the dock band item.
/// </summary>
internal sealed partial class OpenWorkspacesCommand : InvokableCommand
{
    public OpenWorkspacesCommand()
    {
        Name = "Open Workspaces";
        Id = "com.terminalhost.cmdpal.openworkspaces";
    }

    public override ICommandResult Invoke()
    {
        // Clicking the dock band focuses TerminalHost
        HostCli.FocusWindow();
        return CommandResult.Dismiss();
    }
}
