using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents the persisted state of Timeline IDE.
/// </summary>
public class TimelineState
{
    /// <summary>
    /// Whether Timeline IDE mode is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Current time scale for the timeline view.
    /// </summary>
    [JsonPropertyName("currentScale")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TimeScale CurrentScale { get; set; } = TimeScale.Hours;

    /// <summary>
    /// Total accumulated focus time across all intents.
    /// </summary>
    [JsonPropertyName("totalFocusTime")]
    public TimeSpan TotalFocusTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// When the current focus session started (null if not focusing).
    /// </summary>
    [JsonPropertyName("focusStartTime")]
    public DateTime? FocusStartTime { get; set; }

    /// <summary>
    /// IDs of intents that are currently visible/expanded in the timeline.
    /// </summary>
    [JsonPropertyName("visibleIntentIds")]
    public List<string> VisibleIntentIds { get; set; } = [];

    /// <summary>
    /// ID of the currently selected/focused intent.
    /// </summary>
    [JsonPropertyName("currentIntentId")]
    public string? CurrentIntentId { get; set; }

    /// <summary>
    /// All intents in the timeline.
    /// </summary>
    [JsonPropertyName("intents")]
    public List<Intent> Intents { get; set; } = [];

    /// <summary>
    /// All Claude Code sessions across all intents.
    /// </summary>
    [JsonPropertyName("sessions")]
    public List<ClaudeSession> Sessions { get; set; } = [];

    /// <summary>
    /// Order of intent IDs for display (allows reordering).
    /// </summary>
    [JsonPropertyName("intentOrder")]
    public List<string> IntentOrder { get; set; } = [];

    /// <summary>
    /// Date when focus time was last reset (for daily tracking).
    /// </summary>
    [JsonPropertyName("focusTimeResetDate")]
    public DateTime? FocusTimeResetDate { get; set; }

    /// <summary>
    /// Sessions detected via hooks that don't have a matching intent.
    /// These can be assigned to intents later.
    /// </summary>
    [JsonPropertyName("orphanSessions")]
    public List<OrphanSession> OrphanSessions { get; set; } = [];

    // Computed properties

    /// <summary>
    /// Whether focus tracking is currently active.
    /// </summary>
    [JsonIgnore]
    public bool IsFocusing => FocusStartTime.HasValue;

    /// <summary>
    /// Gets the current focus time including any active session.
    /// </summary>
    [JsonIgnore]
    public TimeSpan CurrentFocusTime
    {
        get
        {
            var total = TotalFocusTime;
            if (FocusStartTime.HasValue)
                total += DateTime.UtcNow - FocusStartTime.Value;
            return total;
        }
    }

    /// <summary>
    /// Gets the formatted focus time display.
    /// </summary>
    [JsonIgnore]
    public string FocusTimeDisplay
    {
        get
        {
            var ts = CurrentFocusTime;
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
            return $"{(int)ts.TotalMinutes}m";
        }
    }

    /// <summary>
    /// Gets the count of active intents.
    /// </summary>
    [JsonIgnore]
    public int ActiveIntentCount => Intents.Count(i => i.Status == IntentStatus.Active);

    /// <summary>
    /// Gets the count of running sessions.
    /// </summary>
    [JsonIgnore]
    public int RunningSessionCount => Sessions.Count(s => s.Status == ClaudeSessionStatus.Running);

    /// <summary>
    /// Gets the count of forked sessions.
    /// </summary>
    [JsonIgnore]
    public int ForkCount => Sessions.Count(s => s.IsFork);

    /// <summary>
    /// Gets the total commit count (successful sessions with commits).
    /// </summary>
    [JsonIgnore]
    public int CommitCount => Sessions.Count(s => !string.IsNullOrEmpty(s.CommitHash));

    /// <summary>
    /// Gets the status bar summary.
    /// </summary>
    [JsonIgnore]
    public string StatusSummary =>
        $"{Intents.Count} intents · {ForkCount} forks · {RunningSessionCount} running · {CommitCount} commits";

    /// <summary>
    /// Starts focus time tracking.
    /// </summary>
    public void StartFocus()
    {
        if (!FocusStartTime.HasValue)
            FocusStartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Pauses focus time tracking and accumulates the time.
    /// </summary>
    public void PauseFocus()
    {
        if (FocusStartTime.HasValue)
        {
            TotalFocusTime += DateTime.UtcNow - FocusStartTime.Value;
            FocusStartTime = null;
        }
    }

    /// <summary>
    /// Resets focus time for a new day/session.
    /// </summary>
    public void ResetFocusTime()
    {
        PauseFocus();
        TotalFocusTime = TimeSpan.Zero;
        FocusTimeResetDate = DateTime.UtcNow.Date;
    }

    /// <summary>
    /// Gets an intent by ID.
    /// </summary>
    public Intent? GetIntent(string intentId) =>
        Intents.FirstOrDefault(i => i.Id == intentId);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    public ClaudeSession? GetSession(string sessionId) =>
        Sessions.FirstOrDefault(s => s.Id == sessionId);

    /// <summary>
    /// Gets all sessions for an intent.
    /// </summary>
    public IEnumerable<ClaudeSession> GetSessionsForIntent(string intentId) =>
        Sessions.Where(s => s.IntentId == intentId);

    /// <summary>
    /// Gets intents in display order.
    /// </summary>
    public IEnumerable<Intent> GetOrderedIntents()
    {
        if (IntentOrder.Count == 0)
            return Intents.OrderByDescending(i => i.LastActiveAt ?? i.CreatedAt);

        var ordered = new List<Intent>();
        foreach (var id in IntentOrder)
        {
            var intent = GetIntent(id);
            if (intent != null)
                ordered.Add(intent);
        }

        // Add any intents not in the order list
        foreach (var intent in Intents)
        {
            if (!ordered.Contains(intent))
                ordered.Add(intent);
        }

        return ordered;
    }

    /// <summary>
    /// Adds an intent and maintains order.
    /// </summary>
    public void AddIntent(Intent intent)
    {
        Intents.Add(intent);
        IntentOrder.Insert(0, intent.Id); // New intents at top
    }

    /// <summary>
    /// Removes an intent and its sessions.
    /// </summary>
    public void RemoveIntent(string intentId)
    {
        var intent = GetIntent(intentId);
        if (intent != null)
        {
            // Remove associated sessions
            Sessions.RemoveAll(s => s.IntentId == intentId);

            // Remove from lists
            Intents.Remove(intent);
            IntentOrder.Remove(intentId);
            VisibleIntentIds.Remove(intentId);

            if (CurrentIntentId == intentId)
                CurrentIntentId = null;
        }
    }

    /// <summary>
    /// Adds a session to an intent.
    /// </summary>
    public void AddSession(ClaudeSession session)
    {
        Sessions.Add(session);

        var intent = GetIntent(session.IntentId);
        intent?.AddSession(session.Id);
    }

    /// <summary>
    /// Gets an orphan session by Claude session ID.
    /// </summary>
    public OrphanSession? GetOrphanSession(string claudeSessionId) =>
        OrphanSessions.FirstOrDefault(o => o.SessionId == claudeSessionId && !o.IsAssigned);

    /// <summary>
    /// Gets all unassigned orphan sessions.
    /// </summary>
    public IEnumerable<OrphanSession> GetUnassignedOrphanSessions() =>
        OrphanSessions.Where(o => !o.IsAssigned).OrderByDescending(o => o.StartTime);

    /// <summary>
    /// Gets orphan sessions for a specific working directory.
    /// </summary>
    public IEnumerable<OrphanSession> GetOrphanSessionsForCwd(string cwd)
    {
        var normalizedCwd = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OrphanSessions.Where(o => !o.IsAssigned &&
            string.Equals(Path.GetFullPath(o.Cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedCwd, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds or updates an orphan session.
    /// </summary>
    public void AddOrUpdateOrphanSession(OrphanSession orphan)
    {
        var existing = OrphanSessions.FirstOrDefault(o => o.SessionId == orphan.SessionId);
        if (existing != null)
        {
            // Update existing
            existing.EndTime = orphan.EndTime ?? existing.EndTime;
            existing.TranscriptPath = orphan.TranscriptPath ?? existing.TranscriptPath;
            foreach (var file in orphan.FilesModified)
            {
                existing.AddFile(file);
            }
        }
        else
        {
            OrphanSessions.Add(orphan);
        }
    }

    /// <summary>
    /// Gets the count of unassigned orphan sessions.
    /// </summary>
    [JsonIgnore]
    public int OrphanSessionCount => OrphanSessions.Count(o => !o.IsAssigned);
}
