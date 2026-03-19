// Copyright (c) TerminalHost. All rights reserved.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TerminalHost.CmdPal.Helpers;

namespace TerminalHost.CmdPal.Pages;

/// <summary>
/// ContentPage that displays git status as rendered markdown.
/// Shows branch, status summary, changed files table, and recent commits.
/// </summary>
internal sealed partial class GitStatusPage : ContentPage
{
    private readonly ApiClient _api;
    private int _repoIndex = -1;

    public GitStatusPage(ApiClient api, int repoIndex = -1)
    {
        _api = api;
        _repoIndex = repoIndex;
        Icon = new IconInfo("\uE8CB"); // RepoBranch
        Title = "Git Status";
        Name = "View";
    }

    public override IContent[] GetContent()
    {
        try
        {
            // Always fetch the current active tab
            var status = _api.GetStatusAsync().GetAwaiter().GetResult();
            if (status == null)
                return [new MarkdownContent("*TerminalHost is not running or the REST API is disabled.*")];

            var repoIndex = _repoIndex >= 0 ? _repoIndex : status.ActiveTabIndex;

            var git = _api.GetRepoGitAsync(repoIndex).GetAwaiter().GetResult();
            if (git == null)
                return [new MarkdownContent("*Could not load git status. Is the tab still open?*")];

            // Also get repo info for the title
            var repos = _api.GetReposAsync().GetAwaiter().GetResult();
            var repo = repos?.Repos.Find(r => r.Index == repoIndex);
            var repoName = repo?.Title ?? $"Tab {_repoIndex}";

            return [new MarkdownContent(BuildMarkdown(repoName, git))];
        }
        catch
        {
            return [new MarkdownContent("*Failed to load git status.*")];
        }
    }

    private static string BuildMarkdown(string repoName, ApiModels.GitDetailInfo git)
    {
        var sb = new System.Text.StringBuilder();

        // Header
        sb.AppendLine($"# {repoName}");
        sb.AppendLine();
        sb.AppendLine($"**Branch:** `{git.Branch ?? "(detached)"}`");

        // Status summary line
        var statusParts = new List<string>();
        if (git.Ahead > 0) statusParts.Add($"\u2191{git.Ahead} ahead");
        if (git.Behind > 0) statusParts.Add($"\u2193{git.Behind} behind");
        if (git.ChangedFiles > 0)
        {
            var staged = git.StagedFiles > 0 ? $" ({git.StagedFiles} staged)" : "";
            statusParts.Add($"{git.ChangedFiles} changed{staged}");
        }
        else
        {
            statusParts.Add("clean");
        }
        if (git.UntrackedFiles > 0)
            statusParts.Add($"{git.UntrackedFiles} untracked");
        if (git.StashCount > 0)
            statusParts.Add($"{git.StashCount} stash");

        sb.AppendLine($"**Status:** {string.Join(" | ", statusParts)}");
        sb.AppendLine();

        // Changed files table
        if (git.Files.Count > 0)
        {
            sb.AppendLine("## Changed Files");
            sb.AppendLine();
            sb.AppendLine("| Status | Staged | File |");
            sb.AppendLine("|--------|--------|------|");

            var maxFiles = Math.Min(git.Files.Count, 50);
            for (int i = 0; i < maxFiles; i++)
            {
                var f = git.Files[i];
                var staged = f.IsStaged ? "\u2705" : "";
                var path = f.OldPath != null ? $"{f.OldPath} \u2192 {f.Path}" : f.Path;
                sb.AppendLine($"| {f.Status} | {staged} | `{path}` |");
            }

            if (git.Files.Count > 50)
                sb.AppendLine($"\n*...and {git.Files.Count - 50} more files*");

            sb.AppendLine();
        }

        // Recent commits
        if (git.RecentCommits.Count > 0)
        {
            sb.AppendLine("## Recent Commits");
            sb.AppendLine();

            foreach (var c in git.RecentCommits.Take(10))
            {
                var hash = c.Hash.Length > 7 ? c.Hash[..7] : c.Hash;
                var age = FormatAge(c.Date);
                sb.AppendLine($"- `{hash}` {c.Message} — *{c.Author}, {age}*");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatAge(DateTime date)
    {
        var ago = DateTime.UtcNow - date.ToUniversalTime();

        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        if (ago.TotalDays < 7) return $"{(int)ago.TotalDays}d ago";
        return date.ToString("yyyy-MM-dd");
    }
}
