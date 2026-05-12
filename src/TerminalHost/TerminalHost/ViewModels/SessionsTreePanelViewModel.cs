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
/// no force simulation — just a tree bound directly to ISessionActivityService.
/// </summary>
public partial class SessionsTreePanelViewModel : BasePanelViewModel, IDisposable
{
    private readonly ISessionActivityService? _sessionActivityService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IAppTimer? _refreshTimer;

    public override string PanelId => "sessionsTree";
    public override string PanelTitle => "Sessions";
    public override string PanelIcon => "\U0001F9E0"; // 🧠
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

    public SessionsTreePanelViewModel(
        ISessionActivityService? sessionActivityService,
        IDispatcherService dispatcherService,
        ITimerService timerService)
    {
        _sessionActivityService = sessionActivityService;
        _dispatcherService = dispatcherService;

        DisplayState = PanelDisplayState.Panel;
        PreferredSide = PanelSide.Right;
        Width = 420;
        Height = 600;

        if (_sessionActivityService != null)
        {
            _sessionActivityService.ActivityEventProcessed += OnActivityEvent;
            _sessionActivityService.LifecycleChanged += OnLifecycleChanged;
        }

        // Periodic refresh keeps elapsed/activity strings live even while no events
        // arrive (e.g., a tool is still running but produces no hook traffic).
        _refreshTimer = timerService.CreateTimer(TimeSpan.FromSeconds(2), () => _dispatcherService.BeginInvoke(Refresh));
        _refreshTimer.Start();

        Refresh();
    }

    private void OnActivityEvent(object? sender, ActivityEvent e)
    {
        _dispatcherService.BeginInvoke(Refresh);
    }

    private void OnLifecycleChanged(object? sender, (string SessionId, SessionLifecycle NewState) e)
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
        var states = _sessionActivityService?.GetAllStates() ?? Array.Empty<SessionActivityState>();

        // Dedupe per working directory: when a session is resumed, the old session
        // ID stays in the service (no Stop hook ever arrived) and we'd otherwise show
        // both the stale and the new entry. Keep the most-recently-active state per dir.
        var ordered = states
            .GroupBy(s => string.IsNullOrEmpty(s.WorkingDirectory) ? s.SessionId : s.WorkingDirectory,
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(s => s.IsActive)
                .ThenByDescending(s => s.LastActivityTime ?? s.StartTime)
                .First())
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.LastActivityTime ?? s.StartTime)
            .ToList();

        // Update existing rows by Id; remove any that are gone; append new ones.
        var existing = Sessions.ToDictionary(s => s.Id);
        var seenIds = new HashSet<string>();

        for (int i = 0; i < ordered.Count; i++)
        {
            var state = ordered[i];
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
        var main = state.MainAgent;
        var dirName = string.IsNullOrEmpty(state.WorkingDirectory)
            ? null
            : Path.GetFileName(state.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        node.Title = dirName ?? state.SessionId[..Math.Min(8, state.SessionId.Length)];

        var subtitleParts = new List<string>();
        if (!string.IsNullOrEmpty(state.GitBranch))
            subtitleParts.Add(state.GitBranch);
        if (main?.Model is { Length: > 0 } model)
            subtitleParts.Add(ShortModelName(model));
        node.Subtitle = subtitleParts.Count > 0 ? string.Join("  ·  ", subtitleParts) : null;

        ApplyAgentLiveState(node, state, main);

        // Reconcile subagent children (exclude the main agent — already represented by this row).
        // Done subagents are hidden to keep the tree focused on what's running right now.
        var subs = state.Agents.Values
            .Where(a => !a.IsMain && !IsAgentDone(state, a))
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
            child.Subtitle = !string.IsNullOrWhiteSpace(agent.Task)
                ? Truncate(agent.Task!, 80)
                : agent.Model is { Length: > 0 } subModel
                    ? ShortModelName(subModel)
                    : null;

            ApplyAgentLiveState(child, state, agent);
        }

        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(node.Children[i].Id))
                node.Children.RemoveAt(i);
        }
    }

    /// <summary>
    /// Mirrors AgentInstance state onto a row's spinner/icon/activity/usage fields.
    /// "Active" is treated as inconclusive: SessionActivityState resets agents to
    /// State=Active after each tool call, so without an explicit AgentComplete event
    /// every prior subagent would otherwise stay "working" forever. Real completion
    /// is inferred from (in priority order):
    ///   1. CompleteTime being set
    ///   2. AgentState being a terminal value (Complete/Error)
    ///   3. The session lifecycle being terminal
    ///   4. For subagents: the spawning Task/Agent ToolCall no longer Running
    ///      (the subagent's Id IS the parent tool_use_id, so we can look it up).
    /// </summary>
    private static void ApplyAgentLiveState(SessionTreeNode node, SessionActivityState state, AgentInstance? agent)
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

        var sessionEnded = (state.Lifecycle is SessionLifecycle.Completed or SessionLifecycle.Failed
                or SessionLifecycle.TimedOut or SessionLifecycle.Abandoned)
            && (state.LastActivityTime.HasValue
                ? DateTime.UtcNow - state.LastActivityTime.Value > TimeSpan.FromSeconds(30)
                : true);

        var isDone = IsAgentDone(state, agent);

        node.IsBusy = !isDone && agent.IsActive;
        node.StateIcon = isDone
            ? (agent.State == AgentState.Error || state.Lifecycle == SessionLifecycle.Failed ? "⚠" : "✓")
            : agent.State switch
            {
                AgentState.Idle => "·",
                _ => "·"
            };
        node.Activity = DescribeActivity(state, agent, sessionEnded, isDone);

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

    /// <summary>
    /// True when an agent should be treated as no longer running. SessionActivityState
    /// resets agents to State=Active between tool calls, so AgentState alone can't tell
    /// us — we also check CompleteTime, the session lifecycle, and (for subagents)
    /// whether the spawning Task/Agent tool call has ended.
    /// </summary>
    private static bool IsAgentDone(SessionActivityState state, AgentInstance agent)
    {
        if (agent.CompleteTime != null) return true;
        if (agent.State is AgentState.Complete or AgentState.Error) return true;
        // Claude Code's Stop hook flips Lifecycle to Completed between assistant turns
        // inside a still-active session, and the service never flips it back. Treat the
        // session as "really ended" only if it's been quiet for a while.
        if (state.Lifecycle is SessionLifecycle.Completed or SessionLifecycle.Failed
            or SessionLifecycle.TimedOut or SessionLifecycle.Abandoned)
        {
            var idle = state.LastActivityTime.HasValue
                ? DateTime.UtcNow - state.LastActivityTime.Value
                : TimeSpan.MaxValue;
            if (idle > TimeSpan.FromSeconds(30)) return true;
        }
        if (!agent.IsMain && state.ToolCalls.TryGetValue(agent.Id, out var spawnTc)
            && spawnTc.State != ToolCallState.Running) return true;
        return false;
    }

    private static string DescribeActivity(SessionActivityState state, AgentInstance agent, bool sessionEnded, bool isDone)
    {
        if (sessionEnded)
        {
            return state.Lifecycle switch
            {
                SessionLifecycle.Completed => "Done",
                SessionLifecycle.Failed => "Failed",
                SessionLifecycle.TimedOut => "Timed out",
                SessionLifecycle.Abandoned => "Abandoned",
                _ => "Idle"
            };
        }

        // Subagent's parent Task call ended but its own AgentState is still Active
        // (no AgentComplete event arrived) — treat as done.
        if (isDone)
            return agent.State == AgentState.Error ? "Error" : "Done";

        return agent.State switch
        {
            AgentState.ToolCalling when agent.CurrentToolUseId != null
                && state.ToolCalls.TryGetValue(agent.CurrentToolUseId, out var tc)
                    => string.IsNullOrEmpty(tc.InputSummary)
                        ? tc.ToolName
                        : $"{tc.ToolName}: {Truncate(tc.InputSummary!, 60)}",
            AgentState.ToolCalling => "Running tool",
            AgentState.Thinking => "Thinking…",
            AgentState.WaitingPermission => "Waiting for permission",
            AgentState.Active => "Working…",
            AgentState.Idle => "Idle",
            AgentState.Complete => "Done",
            AgentState.Error => "Error",
            _ => ""
        };
    }

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

    public void Dispose()
    {
        if (_sessionActivityService != null)
        {
            _sessionActivityService.ActivityEventProcessed -= OnActivityEvent;
            _sessionActivityService.LifecycleChanged -= OnLifecycleChanged;
        }
        _refreshTimer?.Dispose();
    }
}
