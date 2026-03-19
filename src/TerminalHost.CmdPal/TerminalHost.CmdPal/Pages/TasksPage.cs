// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Commands;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Pages;

/// <summary>
/// ListPage that shows all tasks from TerminalHost, grouped by status.
/// Tasks from Claude Code show a "Claude" tag.
/// Clicking a task focuses the associated repo tab.
/// </summary>
internal sealed partial class TasksPage : ListPage
{
    private readonly ApiClient _api;
    private IListItem[] _items = [];

    public TasksPage(ApiClient api)
    {
        _api = api;
        Icon = new IconInfo("\uE9D5"); // TaskList
        Title = "TerminalHost Tasks";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        RefreshAsync().GetAwaiter().GetResult();
        return _items;
    }

    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            var result = await _api.GetTasksAsync();

            if (result == null || result.Tasks.Count == 0)
            {
                _items =
                [
                    new ListItem(new NoOpCommand())
                    {
                        Title = _api.IsAvailable
                            ? "No tasks"
                            : "TerminalHost is not running",
                        Subtitle = _api.IsAvailable
                            ? "Tasks will appear when Claude Code or manual tasks are active"
                            : "Start TerminalHost and enable the REST API",
                        Icon = new IconInfo("\uE946"), // Info
                    }
                ];
            }
            else
            {
                // Sort: in_progress first, then pending, then completed
                var sorted = result.Tasks
                    .OrderBy(t => t.Status switch
                    {
                        "in_progress" => 0,
                        "pending" => 1,
                        "completed" => 2,
                        _ => 3,
                    })
                    .ThenByDescending(t => t.Priority);

                _items = sorted.Select(t =>
                {
                    var projectPath = t.ProjectPaths.FirstOrDefault();
                    ICommand command = projectPath != null
                        ? new SwitchTabCommand(projectPath)
                        : new NoOpCommand();

                    var item = new ListItem(command)
                    {
                        Title = t.Title,
                        Subtitle = BuildSubtitle(t),
                        Icon = GetStatusIcon(t.Status),
                    };

                    var tags = new List<Tag> { new(FormatStatus(t.Status)) };

                    if (t.Claude != null)
                        tags.Add(new Tag("Claude"));

                    foreach (var tag in t.Tags.Take(3))
                        tags.Add(new Tag(tag));

                    item.Tags = tags.ToArray();

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
                    Title = "Failed to load tasks",
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

    private static string BuildSubtitle(ApiModels.TaskInfo t)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(t.ElapsedTime))
            parts.Add(t.ElapsedTime);

        if (!string.IsNullOrEmpty(t.LinkedBranch))
            parts.Add(t.LinkedBranch);

        if (!string.IsNullOrEmpty(t.Description))
        {
            var desc = t.Description.Length > 80
                ? t.Description[..77] + "..."
                : t.Description;
            parts.Add(desc);
        }

        return string.Join(" | ", parts);
    }

    private static string FormatStatus(string status) => status switch
    {
        "in_progress" => "In Progress",
        "pending" => "Pending",
        "completed" => "Completed",
        "deleted" => "Deleted",
        _ => status,
    };

    private static IconInfo GetStatusIcon(string status) => status switch
    {
        "in_progress" => new IconInfo("\uE9F5"),  // ProgressRing
        "pending" => new IconInfo("\uE823"),        // Clock
        "completed" => new IconInfo("\uE930"),      // Completed
        _ => new IconInfo("\uE9D5"),                // TaskList
    };
}
