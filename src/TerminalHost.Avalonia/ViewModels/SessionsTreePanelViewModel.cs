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
/// </summary>
public partial class SessionsTreePanelViewModel : BasePanelViewModel, IDisposable
{
    private readonly ISessionActivityService? _sessionActivityService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IAppTimer? _refreshTimer;

    /// <summary>
    /// Records when each subagent first became "done" (per <see cref="IsAgentDone"/>).
    /// Done subagents are kept visible for <see cref="SubagentGracePeriod"/> after this
    /// timestamp so the tree doesn't flicker every time Claude finishes one Task call
    /// and starts another between turns.
    /// </summary>
    private readonly Dictionary<string, DateTime> _subagentDoneAt = new();
    private static readonly TimeSpan SubagentGracePeriod = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Tracks which <see cref="SessionActivityState.SessionId"/> each row is currently
    /// bound to, keyed by row id (working directory). When Claude rotates SessionId for
    /// the same workspace (resume, /clear, hook race) a brand-new SessionActivityState
    /// is created with LatestContextTokens = 0; without this stickiness the row would
    /// momentarily snap to the new state's zero values before the new state populates.
    /// </summary>
    private readonly Dictionary<string, string> _nodeToSessionId =
        new(StringComparer.OrdinalIgnoreCase);

    public override string PanelId => "sessionsTree";
    public override string PanelTitle => "Sessions";
    public override string PanelIcon => "\U0001F9E0";
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    public override IEnumerable<PanelHeaderCommand>? HeaderCommands =>
    [
        new PanelHeaderCommand
        {
            Icon = "↻",
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

    [RelayCommand]
    private void Refresh()
    {
        var states = _sessionActivityService?.GetAllStates() ?? Array.Empty<SessionActivityState>();

        // Transient-empty guard: if the activity service briefly returns no states
        // during a state-mutation burst, don't wipe the tree. Wait for the next refresh.
        if (states.Count == 0 && Sessions.Count > 0)
            return;

        // Group all states by workspace (or SessionId if no working dir).
        static string NodeIdFor(SessionActivityState s) =>
            string.IsNullOrEmpty(s.WorkingDirectory) ? s.SessionId : s.WorkingDirectory;

        // Sticky pick: for each workspace, prefer the SessionActivityState we were
        // already showing (by SessionId). Only switch when that state is gone from the
        // service. This stops the row from snapping to a freshly-created state with
        // LatestContextTokens=0 every time Claude rotates SessionId in the same workspace.
        var ordered = states
            .GroupBy(NodeIdFor, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                if (_nodeToSessionId.TryGetValue(g.Key, out var previousSid))
                {
                    var sticky = g.FirstOrDefault(s => string.Equals(s.SessionId, previousSid, StringComparison.Ordinal));
                    if (sticky != null) return sticky;
                }
                return g
                    .OrderByDescending(s => s.LastActivityTime ?? s.StartTime)
                    .First();
            })
            .OrderByDescending(s => s.LastActivityTime ?? s.StartTime)
            .ToList();

        var presentIds = ordered.Select(NodeIdFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!presentIds.Contains(Sessions[i].Id))
            {
                _nodeToSessionId.Remove(Sessions[i].Id);
                Sessions.RemoveAt(i);
            }
        }

        var existing = Sessions.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var state in ordered)
        {
            var id = NodeIdFor(state);
            if (!existing.TryGetValue(id, out var node))
            {
                node = new SessionTreeNode { Id = id, IsSession = true };
                Sessions.Add(node);
            }

            // Remember which SessionActivityState this node is currently bound to,
            // so the sticky picker above keeps using it on subsequent refreshes.
            _nodeToSessionId[id] = state.SessionId;

            UpdateSessionNode(node, state);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusText));
    }

    private void UpdateSessionNode(SessionTreeNode node, SessionActivityState state)
    {
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

        ApplyAgentLiveState(node, state, main);

        // Keep done subagents visible for SubagentGracePeriod so the tree doesn't
        // collapse every time Claude finishes one Task call and starts another.
        var now = DateTime.UtcNow;
        var subs = state.Agents.Values
            .Where(a => !a.IsMain && ShouldShowSubagent(state, a, now))
            .OrderBy(a => a.SpawnTime)
            .ToList();

        // Remove-first reconciliation: don't reorder existing children. Removing-and-
        // reinserting items destroys/recreates the underlying TreeViewItem, which makes
        // the tree visibly collapse/re-expand. By only removing children that are no
        // longer present and appending new ones at the end, existing rows stay put and
        // their TreeViewItem state (including IsExpanded) is preserved.
        var presentIds = subs.Select(a => a.Id).ToHashSet();
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (!presentIds.Contains(node.Children[i].Id))
                node.Children.RemoveAt(i);
        }

        var existingChildren = node.Children.ToDictionary(c => c.Id);
        foreach (var agent in subs)
        {
            if (!existingChildren.TryGetValue(agent.Id, out var child))
            {
                child = new SessionTreeNode { Id = agent.Id, IsSession = false };
                node.Children.Add(child);
            }

            child.Title = string.IsNullOrEmpty(agent.Name) ? "subagent" : agent.Name;
            child.WorkingDirectory = state.WorkingDirectory;
            child.Subtitle = !string.IsNullOrWhiteSpace(agent.Task)
                ? Truncate(agent.Task!, 80)
                : agent.Model is { Length: > 0 } subModel
                    ? ShortModelName(subModel)
                    : null;

            ApplyAgentLiveState(child, state, agent);
        }
    }

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

            const int max = 1_000_000;

            // Token counts only flow in on AssistantMessage events with real usage data;
            // between turns the agent can briefly report 0 even though the conversation
            // is mid-flight. Treat 0 as "no fresh data yet" and keep the last known value
            // on the row so the usage bar doesn't snap back to empty and re-fill.
            if (tokens <= 0 && node.UsageTokens > 0)
                return;

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
            node.MaxTokens = 0;
            node.UsageTokens = 0;
            node.UsagePercent = 0;
            node.UsageText = null;
        }
    }

    /// <summary>
    /// Returns true if the subagent should currently appear in the tree.
    /// Still-active agents are always shown; done agents are shown for a grace period
    /// after first completion so the tree doesn't churn between Claude turns.
    /// </summary>
    private bool ShouldShowSubagent(SessionActivityState state, AgentInstance agent, DateTime now)
    {
        if (!IsAgentDone(state, agent))
        {
            _subagentDoneAt.Remove(agent.Id); // active again — reset grace
            return true;
        }

        if (!_subagentDoneAt.TryGetValue(agent.Id, out var doneAt))
        {
            _subagentDoneAt[agent.Id] = now;
            return true;
        }

        return now - doneAt < SubagentGracePeriod;
    }

    private static bool IsAgentDone(SessionActivityState state, AgentInstance agent)
    {
        if (agent.CompleteTime != null) return true;
        if (agent.State is AgentState.Complete or AgentState.Error) return true;
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

    public event EventHandler<string>? OpenProjectRequested;

    [RelayCommand]
    private void OpenWorkspace(SessionTreeNode? node)
    {
        if (node?.WorkingDirectory is { Length: > 0 } path)
            OpenProjectRequested?.Invoke(this, path);
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
