using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// Right-side dockable panel that lists active Claude Code sessions and their
/// subagents in a tree. Each row shows live state: spinner while working,
/// a short description of the current activity, and a context-token usage bar.
///
/// Intentionally simple compared to Spark Canvas: no rendering, no WebView2,
/// no force simulation — just a tree bound to <see cref="ISessionLifecycleCoordinator"/>.
/// </summary>
public partial class SessionsTreePanelViewModel : BasePanelViewModel, IDisposable
{
    private readonly ISessionLifecycleCoordinator? _coord;
    private readonly IDispatcherService _dispatcherService;

    public override string PanelId => "sessionsTree";
    public override string PanelTitle => "Sessions";
    public override string PanelIcon => "⚡"; // ⚡
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "↻", // ↻
            Tooltip = "Refresh",
            Command = RefreshCommand
        }
    ];

    public override string? StatusText
    {
        get
        {
            var active = Sessions.Count(s => s.IsBusy);
            return Sessions.Count == 0
                ? "No active sessions"
                : active > 0
                    ? $"{active} working · {Sessions.Count} session{(Sessions.Count == 1 ? "" : "s")}"
                    : $"{Sessions.Count} session{(Sessions.Count == 1 ? "" : "s")}";
        }
    }

    /// <summary>
    /// Top-level rows in the tree (one per session). Subagents hang underneath.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SessionTreeNode> _sessions = new();

    public bool IsEmpty => Sessions.Count == 0;

    // timerService kept on the ctor for DI compatibility with existing wiring;
    // refresh ticks now flow through ISessionLifecycleCoordinator.SessionsChanged
    // (which also fires from the inactivity-clock sweep), so a local timer is no
    // longer needed.
    public SessionsTreePanelViewModel(
        ISessionLifecycleCoordinator? sessionCoordinator,
        IDispatcherService dispatcherService,
        ITimerService timerService)
    {
        _coord = sessionCoordinator;
        _dispatcherService = dispatcherService;

        DisplayState = PanelDisplayState.Panel;
        PreferredSide = PanelSide.Right;
        Width = 420;
        Height = 600;

        if (_coord != null)
            _coord.SessionsChanged += OnSessionsChanged;

        Refresh();
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        _dispatcherService.BeginInvoke(Refresh);
    }

    /// <summary>
    /// Rebuilds (or updates in-place) the tree from current activity state.
    /// Nodes are matched by Id so TreeView expansion is preserved across updates.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        // GetSessionsForDisplay dedupes per workspace; sort alphabetically by folder
        // name so row positions stay stable across the 2s refresh tick (issue #72).
        var ordered = (_coord?.GetSessionsForDisplay() ?? (IReadOnlyList<SessionView>)Array.Empty<SessionView>())
            .OrderBy(v => string.IsNullOrEmpty(v.ActivityState.WorkingDirectory)
                ? v.SessionId
                : Path.GetFileName(v.ActivityState.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Update existing rows by Id; remove any that are gone; append new ones.
        var existing = Sessions.ToDictionary(s => s.Id);
        var seenIds = new HashSet<string>();

        for (int i = 0; i < ordered.Count; i++)
        {
            var state = ordered[i].ActivityState;
            seenIds.Add(state.SessionId);

            if (!existing.TryGetValue(state.SessionId, out var node))
            {
                node = new SessionTreeNode { Id = state.SessionId, IsSession = true };
                Sessions.Insert(Math.Min(i, Sessions.Count), node);
            }
            else if (Sessions.IndexOf(node) != i)
            {
                // Keep ordering consistent with the sorted snapshot.
                Sessions.Remove(node);
                Sessions.Insert(Math.Min(i, Sessions.Count), node);
            }

            UpdateSessionNode(node, state);
        }

        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!seenIds.Contains(Sessions[i].Id))
                Sessions.RemoveAt(i);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Updates a session row from its main agent and reconciles its subagent children.
    /// </summary>
    private static void UpdateSessionNode(SessionTreeNode node, SessionActivityState state)
    {
        var now = DateTime.UtcNow;
        var main = state.MainAgent;
        var dirName = string.IsNullOrEmpty(state.WorkingDirectory)
            ? null
            : Path.GetFileName(state.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        node.Title = dirName ?? state.SessionId[..Math.Min(8, state.SessionId.Length)];
        node.WorkingDirectory = state.WorkingDirectory;

        var subtitleParts = new List<string>();
        if (!string.IsNullOrEmpty(state.GitBranch))
            subtitleParts.Add(state.GitBranch);
        if (main?.Model is { Length: > 0 } model)
            subtitleParts.Add(ShortModelName(model));
        node.Subtitle = subtitleParts.Count > 0 ? string.Join("  ·  ", subtitleParts) : null;

        ApplyAgentLiveState(node, state, main, now);

        // Reconcile subagent children (exclude the main agent — already represented by this row).
        // Done subagents are hidden to keep the tree focused on what's running right now.
        // Hide subagents whose stamps stopped advancing — independent of SubagentStop reliability.
        var subs = state.Agents.Values
            .Where(a => !a.IsMain
                && a.CompleteTime is null
                && a.State is not (AgentState.Complete or AgentState.Error)
                && a.LastActivityEventTime is { } lastActivity
                && (now - lastActivity) < TimeSpan.FromSeconds(60))
            .OrderBy(a => a.SpawnTime)
            .ToList();

        var existingChildren = node.Children.ToDictionary(c => c.Id);
        var seen = new HashSet<string>();

        for (int i = 0; i < subs.Count; i++)
        {
            var agent = subs[i];
            seen.Add(agent.Id);

            if (!existingChildren.TryGetValue(agent.Id, out var child))
            {
                child = new SessionTreeNode { Id = agent.Id, IsSession = false };
                node.Children.Insert(Math.Min(i, node.Children.Count), child);
            }
            else if (node.Children.IndexOf(child) != i)
            {
                node.Children.Remove(child);
                node.Children.Insert(Math.Min(i, node.Children.Count), child);
            }

            child.Title = string.IsNullOrEmpty(agent.Name) ? "subagent" : agent.Name;
            child.WorkingDirectory = state.WorkingDirectory;
            child.Subtitle = !string.IsNullOrWhiteSpace(agent.Task)
                ? Truncate(agent.Task!, 80)
                : agent.Model is { Length: > 0 } subModel
                    ? ShortModelName(subModel)
                    : null;

            ApplyAgentLiveState(child, state, agent, now);
        }

        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(node.Children[i].Id))
                node.Children.RemoveAt(i);
        }
    }

    /// <summary>
    /// Mirrors derived display state onto a row's spinner/icon/activity/usage fields.
    /// Display is computed at read time from per-agent event timestamps via
    /// <see cref="SessionActivityState.DeriveAgentDisplayState"/> /
    /// <see cref="SessionActivityState.DeriveParentDisplay"/>; the legacy
    /// AgentState/Lifecycle fields are no longer consulted for the display decision.
    /// </summary>
    private static void ApplyAgentLiveState(SessionTreeNode node, SessionActivityState state, AgentInstance? agent, DateTime now)
    {
        if (agent == null)
        {
            node.IsBusy = false;
            node.StateIcon = "?";
            node.Activity = "No agent";
            node.UsageTokens = 0;
            node.UsagePercent = 0;
            node.UsageText = null;
            return;
        }

        var displayState = agent.IsMain
            ? state.DeriveParentDisplay(now)
            : state.DeriveAgentDisplayState(agent, now);

        node.IsBusy = displayState == AgentDisplayState.Working;
        node.StateIcon = MapStateIcon(displayState, agent);
        node.Activity = DescribeActivity(displayState, state, agent);

        if (node.IsSession)
        {
            var tokens = agent.LatestContextTokens > 0
                ? agent.LatestContextTokens
                : agent.Context?.Total ?? 0;

            // Assume the main Claude Code session is always on the 1M context beta.
            // The [1m] beta opt-in isn't reflected in the model name we receive
            // (it stays "claude-opus-4-7"), so model-based detection isn't reliable.
            const int max = 1_000_000;
            var pct = max > 0 ? Math.Min(100.0, tokens * 100.0 / max) : 0;

            node.MaxTokens = max;
            node.UsageTokens = tokens;
            node.UsagePercent = pct;
            node.UsageText = tokens > 0
                ? $"{FormatTokens(tokens)} / {FormatTokens(max)}  ·  {pct:0}%"
                : null!;
        }
        else
        {
            // Subagent usage is unreliable in the core service today (parent's assistant-message
            // tokens get heuristically attributed to the most recently spawned subagent).
            // Hide usage on subagent rows until per-subagent tracking is fixed.
            node.MaxTokens = 0;
            node.UsageTokens = 0;
            node.UsagePercent = 0;
            node.UsageText = null;
        }
    }

    private static string MapStateIcon(AgentDisplayState displayState, AgentInstance agent) =>
        displayState switch
        {
            AgentDisplayState.WaitingPermission => "⚠",
            AgentDisplayState.Working => "·",
            // Preserve error-icon on terminated subagents that errored.
            AgentDisplayState.Done when agent.State == AgentState.Error => "⚠",
            AgentDisplayState.Done => "✓",
            AgentDisplayState.TimedOut => "✓",
            _ => "·"
        };

    private static string DescribeActivity(AgentDisplayState displayState, SessionActivityState state, AgentInstance agent) =>
        displayState switch
        {
            AgentDisplayState.WaitingPermission => "Waiting for permission",
            AgentDisplayState.Done => "Done",
            AgentDisplayState.TimedOut => "Timed out",
            AgentDisplayState.Working => DescribeWorkingActivity(state, agent),
            _ => ""
        };

    private static string DescribeWorkingActivity(SessionActivityState state, AgentInstance agent) =>
        agent.State switch
        {
            AgentState.ToolCalling when agent.CurrentToolUseId != null
                && state.ToolCalls.TryGetValue(agent.CurrentToolUseId, out var tc)
                    => string.IsNullOrEmpty(tc.InputSummary)
                        ? tc.ToolName
                        : $"{tc.ToolName}: {Truncate(tc.InputSummary!, 60)}",
            AgentState.ToolCalling => "Running tool",
            AgentState.WaitingPermission => "Waiting for permission",
            AgentState.Thinking => "Thinking…",
            _ => "Working…"
        };

    private static string ShortModelName(string model)
    {
        // "claude-opus-4-7" → "opus-4-7"; keep [1m] suffix if present.
        var m = model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? model[7..] : model;
        return m;
    }

    private static string FormatTokens(int n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}k",
        _ => n.ToString()
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    public void Open()
    {
        IsOpen = true;
        Refresh();
        RequestShow();
    }

    /// <summary>
    /// Raised when the user activates a session row (double-click or context menu).
    /// Carries the session's working directory; the host wires this to
    /// MainViewModel.OpenProjectTab, which opens a new tab or focuses an existing one.
    /// </summary>
    public event EventHandler<string>? OpenProjectRequested;

    [RelayCommand]
    private void OpenWorkspace(SessionTreeNode? node)
    {
        if (node?.WorkingDirectory is { Length: > 0 } path)
            OpenProjectRequested?.Invoke(this, path);
    }

    public void Dispose()
    {
        if (_coord != null)
            _coord.SessionsChanged -= OnSessionsChanged;
    }
}
