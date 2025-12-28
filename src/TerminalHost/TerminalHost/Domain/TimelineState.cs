using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Root state container for all Timeline Mode data.
/// Persisted in AppConfiguration.
/// </summary>
public class TimelineState
{
    /// <summary>Whether Timeline Mode is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Current time scale for the timeline view.</summary>
    [JsonPropertyName("currentScale")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TimeScale CurrentScale { get; set; } = TimeScale.Hours;

    /// <summary>Total accumulated focus time.</summary>
    [JsonPropertyName("totalFocusTime")]
    public TimeSpan TotalFocusTime { get; set; } = TimeSpan.Zero;

    /// <summary>When the current focus session started (null if not focusing).</summary>
    [JsonPropertyName("focusStartTime")]
    public DateTime? FocusStartTime { get; set; }

    /// <summary>All intents.</summary>
    [JsonPropertyName("intents")]
    public List<Intent> Intents { get; set; } = [];

    /// <summary>All sessions across all intents.</summary>
    [JsonPropertyName("sessions")]
    public List<ClaudeCodeSession> Sessions { get; set; } = [];

    /// <summary>Display order of intents (list of IDs).</summary>
    [JsonPropertyName("intentOrder")]
    public List<string> IntentOrder { get; set; } = [];

    /// <summary>Currently expanded/visible intent IDs.</summary>
    [JsonPropertyName("visibleIntentIds")]
    public List<string> VisibleIntentIds { get; set; } = [];

    /// <summary>Currently selected/focused intent ID.</summary>
    [JsonPropertyName("currentIntentId")]
    public string? CurrentIntentId { get; set; }

    /// <summary>Date when focus time was last reset.</summary>
    [JsonPropertyName("focusTimeResetDate")]
    public DateTime? FocusTimeResetDate { get; set; }

    /// <summary>Sessions detected via hooks without matching intents.</summary>
    [JsonPropertyName("orphanSessions")]
    public List<OrphanSession> OrphanSessions { get; set; } = [];

    // Focus time management

    /// <summary>Whether currently focusing (timer running).</summary>
    [JsonIgnore]
    public bool IsFocusing => FocusStartTime.HasValue;

    /// <summary>Starts the focus timer.</summary>
    public void StartFocus()
    {
        if (!FocusStartTime.HasValue)
        {
            FocusStartTime = DateTime.UtcNow;
        }
    }

    /// <summary>Pauses the focus timer and accumulates time.</summary>
    public void PauseFocus()
    {
        if (FocusStartTime.HasValue)
        {
            TotalFocusTime += DateTime.UtcNow - FocusStartTime.Value;
            FocusStartTime = null;
        }
    }

    /// <summary>Resets the accumulated focus time.</summary>
    public void ResetFocusTime()
    {
        TotalFocusTime = TimeSpan.Zero;
        FocusTimeResetDate = DateTime.UtcNow;
    }

    /// <summary>Gets the current focus time including any active session.</summary>
    [JsonIgnore]
    public TimeSpan CurrentFocusTime
    {
        get
        {
            var total = TotalFocusTime;
            if (FocusStartTime.HasValue)
            {
                total += DateTime.UtcNow - FocusStartTime.Value;
            }
            return total;
        }
    }

    // Intent queries

    /// <summary>Gets an intent by ID.</summary>
    public Intent? GetIntent(string id) => Intents.FirstOrDefault(i => i.Id == id);

    /// <summary>Gets the current/selected intent.</summary>
    public Intent? GetCurrentIntent() => string.IsNullOrEmpty(CurrentIntentId) ? null : GetIntent(CurrentIntentId);

    /// <summary>Gets all active intents.</summary>
    public List<Intent> GetActiveIntents() => Intents.Where(i => i.Status == IntentStatus.Active).ToList();

    /// <summary>Gets intents in display order.</summary>
    public List<Intent> GetOrderedIntents()
    {
        if (IntentOrder.Count == 0)
            return [.. Intents.OrderByDescending(i => i.LastActiveAt ?? i.CreatedAt)];

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

    // Session queries

    /// <summary>Gets a session by ID.</summary>
    public ClaudeCodeSession? GetSession(string id) => Sessions.FirstOrDefault(s => s.Id == id);

    /// <summary>Gets all sessions for an intent.</summary>
    public List<ClaudeCodeSession> GetSessionsForIntent(string intentId) =>
        Sessions.Where(s => s.IntentId == intentId).OrderBy(s => s.StartTime).ToList();

    /// <summary>Gets all running sessions.</summary>
    public List<ClaudeCodeSession> GetRunningSessions() =>
        Sessions.Where(s => s.Status == ClaudeSessionStatus.Running).ToList();

    // Mutations

    /// <summary>Adds an intent.</summary>
    public void AddIntent(Intent intent)
    {
        if (!Intents.Any(i => i.Id == intent.Id))
        {
            Intents.Add(intent);
            IntentOrder.Insert(0, intent.Id);
            VisibleIntentIds.Add(intent.Id);
        }
    }

    /// <summary>Adds a session.</summary>
    public void AddSession(ClaudeCodeSession session)
    {
        if (!Sessions.Any(s => s.Id == session.Id))
        {
            Sessions.Add(session);

            // Also add to the intent's session list
            var intent = GetIntent(session.IntentId);
            intent?.AddSession(session.Id);
        }
    }

    /// <summary>Removes an intent and its sessions.</summary>
    public void RemoveIntent(string intentId)
    {
        var intent = GetIntent(intentId);
        if (intent == null) return;

        // Remove all sessions for this intent
        Sessions.RemoveAll(s => s.IntentId == intentId);

        // Remove from collections
        Intents.Remove(intent);
        IntentOrder.Remove(intentId);
        VisibleIntentIds.Remove(intentId);

        if (CurrentIntentId == intentId)
            CurrentIntentId = null;
    }

    // Orphan session management

    /// <summary>Gets an orphan session by session ID.</summary>
    public OrphanSession? GetOrphanSession(string sessionId) =>
        OrphanSessions.FirstOrDefault(o => o.SessionId == sessionId);

    /// <summary>Gets all orphan sessions for a working directory.</summary>
    public List<OrphanSession> GetOrphanSessionsForCwd(string cwd) =>
        OrphanSessions.Where(o => !o.IsAssigned && o.Cwd.Equals(cwd, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Adds or updates an orphan session.</summary>
    public void AddOrUpdateOrphanSession(OrphanSession orphan)
    {
        var existing = GetOrphanSession(orphan.SessionId);
        if (existing != null)
        {
            // Update existing
            existing.EndTime = orphan.EndTime;
            existing.TranscriptPath = orphan.TranscriptPath;
            foreach (var file in orphan.FilesModified)
            {
                if (!existing.FilesModified.Contains(file))
                    existing.FilesModified.Add(file);
            }
        }
        else
        {
            // Add new (limit to 20)
            OrphanSessions.Add(orphan);
            while (OrphanSessions.Count > 20)
            {
                // Remove oldest non-assigned
                var oldest = OrphanSessions
                    .Where(o => !o.IsAssigned)
                    .OrderBy(o => o.StartTime)
                    .FirstOrDefault();
                if (oldest != null)
                    OrphanSessions.Remove(oldest);
                else
                    break;
            }
        }
    }

    /// <summary>Marks orphan sessions as assigned and returns the created sessions.</summary>
    public List<ClaudeCodeSession> AssignOrphansToIntent(string intentId, string cwd)
    {
        var created = new List<ClaudeCodeSession>();
        var orphans = GetOrphanSessionsForCwd(cwd);

        foreach (var orphan in orphans)
        {
            var session = orphan.ToClaudeCodeSession(intentId);
            AddSession(session);
            created.Add(session);

            orphan.IsAssigned = true;
            orphan.AssignedSessionId = session.Id;
        }

        return created;
    }
}
