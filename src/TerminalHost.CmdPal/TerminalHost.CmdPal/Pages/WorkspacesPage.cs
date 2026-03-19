// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Commands;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Pages;

/// <summary>
/// ListPage that shows all open TerminalHost tabs/workspaces.
/// Each item shows the project name, git branch, changed file count, and activity state.
/// Clicking an item focuses that tab via host.exe.
/// </summary>
internal sealed partial class WorkspacesPage : ListPage
{
    private readonly ApiClient _api;
    private IListItem[] _items = [];

    public WorkspacesPage(ApiClient api)
    {
        _api = api;
        Icon = new IconInfo("\uE8FC"); // AppList
        Title = "TerminalHost Workspaces";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        // Fetch synchronously so items are available immediately
        RefreshAsync().GetAwaiter().GetResult();
        return _items;
    }

    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            var repos = await _api.GetReposAsync();

            if (repos == null || repos.Repos.Count == 0)
            {
                _items =
                [
                    new ListItem(new NoOpCommand())
                    {
                        Title = _api.IsAvailable
                            ? "No open tabs"
                            : "TerminalHost is not running",
                        Subtitle = _api.IsAvailable
                            ? "Open a project in TerminalHost to see it here"
                            : "Start TerminalHost and enable the REST API in Settings",
                        Icon = new IconInfo("\uE946"), // Info
                    }
                ];
            }
            else
            {
                _items = repos.Repos.Select(r =>
                {
                    var item = new ListItem(new SwitchTabCommand(r.WorkingDirectory))
                    {
                        Title = r.Title,
                        Subtitle = BuildSubtitle(r),
                        Icon = GetActivityIcon(r.ActivityIndicator),
                    };

                    if (r.IsActive)
                        item.Tags = [new Tag("Active")];

                    return (IListItem)item;
                }).ToArray();
            }
        }
        catch
        {
            _items =
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Failed to load workspaces",
                    Subtitle = "Check that TerminalHost is running with the REST API enabled",
                    Icon = new IconInfo("\uE783"), // Error
                }
            ];
        }
        finally
        {
            IsLoading = false;
            RaiseItemsChanged();
        }
    }

    private static string BuildSubtitle(ApiModels.RepoInfo r)
    {
        var parts = new List<string>();

        if (r.Git?.Branch != null)
            parts.Add(r.Git.Branch);

        if (r.Git != null)
        {
            if (r.Git.ChangedFiles > 0)
            {
                var staged = r.Git.StagedFiles > 0 ? $" ({r.Git.StagedFiles} staged)" : "";
                parts.Add($"{r.Git.ChangedFiles} changed{staged}");
            }
            else
            {
                parts.Add("clean");
            }

            if (r.Git.Ahead > 0)
                parts.Add($"\u2191{r.Git.Ahead}");
            if (r.Git.Behind > 0)
                parts.Add($"\u2193{r.Git.Behind}");
        }

        var state = r.ActivityIndicator?.State;
        if (state == "busy") parts.Add("busy");
        else if (state == "waiting") parts.Add("waiting for input");
        else if (state == "done") parts.Add("done");

        return string.Join(" | ", parts);
    }

    private static IconInfo GetActivityIcon(ApiModels.ActivityIndicator? indicator)
    {
        return indicator?.State switch
        {
            "busy" => new IconInfo("\uE9F5"),     // ProgressRing
            "waiting" => new IconInfo("\uE8EE"),   // Warning / pause
            "done" => new IconInfo("\uE930"),       // Completed
            _ => new IconInfo("\uE756"),            // DeviceLaptopNoPic
        };
    }
}

/// <summary>
/// A command that does nothing — used for informational list items.
/// </summary>
internal sealed partial class NoOpCommand : InvokableCommand
{
    public NoOpCommand()
    {
        Name = "No Action";
    }

    public override ICommandResult Invoke()
    {
        return CommandResult.KeepOpen();
    }
}
