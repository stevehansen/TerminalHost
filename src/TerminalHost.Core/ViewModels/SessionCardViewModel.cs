using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for a session card in the flat timeline list.
/// Wraps either a ClaudeSessionIndexEntry (historical) or LiveSession (active).
/// All display properties are ObservableProperty for in-place updates without collection churn.
/// </summary>
public partial class SessionCardViewModel : ObservableObject
{
    /// <summary>Stable identity for reconciliation — never changes.</summary>
    public string SessionId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _endReason;

    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _projectPath = "";
    [ObservableProperty] private string _statusIcon = "⚪";
    [ObservableProperty] private string _statusColorHex = "#555555";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _gitBranch = "";
    [ObservableProperty] private string _durationDisplay = "";
    [ObservableProperty] private string _timeRangeDisplay = "";
    [ObservableProperty] private int _messageCount;
    [ObservableProperty] private DateTime _startTime;
    [ObservableProperty] private DateTime? _modifiedTime;
    [ObservableProperty] private bool _isSidechain;
    [ObservableProperty] private string? _transcriptPath;
    [ObservableProperty] private string? _firstPrompt;

    #region Activity Data (live sessions)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivityData))]
    [NotifyPropertyChangedFor(nameof(ActivitySummary))]
    private int _toolCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivitySummary))]
    private int _fileReads;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivitySummary))]
    private int _fileWrites;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivitySummary))]
    private int _shellCommands;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivitySummary))]
    private int _searches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivitySummary))]
    private int _subagentCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivityData))]
    private int _agentCount;

    [ObservableProperty] private string? _currentToolDescription;
    [ObservableProperty] private string? _model;

    public bool HasActivityData => ToolCount > 0 || AgentCount > 1;

    public string ActivitySummary
    {
        get
        {
            if (ToolCount == 0) return string.Empty;
            var parts = new List<string> { $"{ToolCount} tools" };
            if (FileReads > 0) parts.Add($"{FileReads} reads");
            if (FileWrites > 0) parts.Add($"{FileWrites} writes");
            if (ShellCommands > 0) parts.Add($"{ShellCommands} shell");
            if (Searches > 0) parts.Add($"{Searches} search");
            if (SubagentCount > 0) parts.Add($"{SubagentCount} subagent{(SubagentCount > 1 ? "s" : "")}");
            return string.Join(" · ", parts);
        }
    }

    #endregion

    public string StatusText => IsLive ? "LIVE" : EndReason switch
    {
        "timeout" => "TIMED OUT",
        _ => "COMPLETED"
    };

    private readonly TimelineTabViewModel? _parent;

    public SessionCardViewModel(string sessionId, TimelineTabViewModel? parent = null)
    {
        SessionId = sessionId;
        _parent = parent;
    }

    /// <summary>
    /// Update display properties from a ClaudeSessionIndexEntry (historical session).
    /// </summary>
    public void UpdateFromIndexEntry(ClaudeSessionIndexEntry entry)
    {
        IsLive = false;
        Summary = entry.Summary ?? entry.FirstPrompt ?? "";
        FirstPrompt = entry.FirstPrompt;
        GitBranch = entry.GitBranch ?? "";
        MessageCount = entry.MessageCount;
        IsSidechain = entry.IsSidechain;
        TranscriptPath = entry.FullPath;

        if (entry.Created.HasValue)
            StartTime = entry.Created.Value;

        ModifiedTime = entry.Modified;

        // Project name from path
        var path = entry.ProjectPath ?? "";
        ProjectPath = path;
        ProjectName = GetFolderName(path);

        UpdateTimeDisplays(entry.Created, entry.Modified);
    }

    /// <summary>
    /// Update display properties from a LiveSession (active session).
    /// <paramref name="lastActivityTime"/> comes from the canonical SessionActivityState.
    /// </summary>
    public void UpdateFromLiveSession(LiveSession live, DateTime? lastActivityTime = null)
    {
        IsLive = live.IsActive;
        ProjectName = live.DisplayName;
        ProjectPath = live.WorkingDirectory ?? "";
        StartTime = live.StartTime;
        ModifiedTime = lastActivityTime ?? live.EndTime ?? live.StartTime;
        TranscriptPath = live.TranscriptPath;

        EndReason = live.EndReason;

        if (live.IsActive)
        {
            StatusIcon = "🔵";
            StatusColorHex = "#1B4B6B";
        }
        else
        {
            // Determine status from end reason and activity data
            var (icon, color) = live.EndReason switch
            {
                "timeout" => ("⏱", "#4A3D1E"),                          // Amber — timed out
                "explicit" when live.HadErrors && !live.HadFileWrites
                    => ("❌", "#4D1E1E"),                                // Red — failed
                "explicit" when live.HadFileWrites
                    => ("✅", "#1E4D1E"),                                // Green — success with changes
                _ => ("✅", "#1E4D4D"),                                  // Teal — completed normally
            };
            StatusIcon = icon;
            StatusColorHex = color;
        }

        UpdateTimeDisplays(live.StartTime, live.EndTime ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Update activity data from SessionActivityService state.
    /// </summary>
    public void UpdateActivityData(SessionActivityState? state)
    {
        if (state == null)
        {
            ToolCount = 0;
            FileReads = 0;
            FileWrites = 0;
            ShellCommands = 0;
            Searches = 0;
            SubagentCount = 0;
            AgentCount = 0;
            Model = null;
            CurrentToolDescription = null;
            return;
        }

        var toolCalls = state.ToolCalls.Values;
        ToolCount = toolCalls.Count;
        FileReads = toolCalls.Count(t => t.Category == ToolCategory.FileRead);
        FileWrites = toolCalls.Count(t => t.Category == ToolCategory.FileWrite);
        ShellCommands = toolCalls.Count(t => t.Category == ToolCategory.Shell);
        Searches = toolCalls.Count(t => t.Category == ToolCategory.Search);
        SubagentCount = state.Agents.Values.Count(a => !a.IsMain);
        AgentCount = state.TotalAgents;
        Model = state.MainAgent?.Model;

        var activeTool = state.ActiveToolCalls.FirstOrDefault();
        CurrentToolDescription = activeTool != null
            ? $"{activeTool.ToolName}{(activeTool.InputSummary != null ? $": {activeTool.InputSummary}" : "")}"
            : null;
    }

    [RelayCommand]
    private void Select()
    {
        _parent?.SelectSession(this);
    }

    /// <summary>
    /// Called by the timer to keep live session duration displays current.
    /// </summary>
    public void RefreshTimeDisplays()
    {
        if (IsLive && StartTime != default)
            UpdateTimeDisplays(StartTime, null);
    }

    private void UpdateTimeDisplays(DateTime? start, DateTime? end)
    {
        if (!start.HasValue) return;

        var localStart = start.Value.Kind == DateTimeKind.Utc ? start.Value.ToLocalTime() : start.Value;
        var localEnd = end.HasValue
            ? (end.Value.Kind == DateTimeKind.Utc ? end.Value.ToLocalTime() : end.Value)
            : DateTime.Now;

        // Duration
        var duration = localEnd - localStart;
        DurationDisplay = FormatDuration(duration);

        // Time range
        if (localStart.Date != localEnd.Date)
        {
            TimeRangeDisplay = $"{localStart:MMM d HH:mm} → {localEnd:MMM d HH:mm}";
        }
        else
        {
            var endStr = IsLive ? "..." : localEnd.ToString("HH:mm");
            TimeRangeDisplay = $"{localStart:HH:mm} → {endStr}";
        }

        // Status icon/color for historical
        if (!IsLive)
        {
            StatusIcon = "✅";
            StatusColorHex = "#1E4D4D";
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1) return $"{(int)duration.TotalMinutes}m";
        return "< 1m";
    }

    private static string GetFolderName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Unknown";
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
