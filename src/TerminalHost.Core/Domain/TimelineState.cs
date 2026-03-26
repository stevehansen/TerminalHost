using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents the persisted state of Timeline IDE.
/// Contains intents, focus time, and display preferences.
/// Session data is NOT stored here — it comes from ClaudeSessionIndexService (historical)
/// and TimelineService live session tracking (active).
/// </summary>
public class TimelineState
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("totalFocusTime")]
    public TimeSpan TotalFocusTime { get; set; } = TimeSpan.Zero;

    [JsonPropertyName("focusStartTime")]
    public DateTime? FocusStartTime { get; set; }

    [JsonPropertyName("visibleIntentIds")]
    public List<string> VisibleIntentIds { get; set; } = [];

    [JsonPropertyName("currentIntentId")]
    public string? CurrentIntentId { get; set; }

    [JsonPropertyName("intents")]
    public List<Intent> Intents { get; set; } = [];

    [JsonPropertyName("intentOrder")]
    public List<string> IntentOrder { get; set; } = [];

    [JsonPropertyName("focusTimeResetDate")]
    public DateTime? FocusTimeResetDate { get; set; }

    // Computed properties

    [JsonIgnore]
    public bool IsFocusing => FocusStartTime.HasValue;

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

    [JsonIgnore]
    public int ActiveIntentCount => Intents.Count(i => i.Status == IntentStatus.Active);

    // Focus time methods

    public void StartFocus()
    {
        if (!FocusStartTime.HasValue)
            FocusStartTime = DateTime.UtcNow;
    }

    public void PauseFocus()
    {
        if (FocusStartTime.HasValue)
        {
            TotalFocusTime += DateTime.UtcNow - FocusStartTime.Value;
            FocusStartTime = null;
        }
    }

    public void ResetFocusTime()
    {
        PauseFocus();
        TotalFocusTime = TimeSpan.Zero;
        FocusTimeResetDate = DateTime.UtcNow.Date;
    }

    // Intent methods

    public Intent? GetIntent(string intentId) =>
        Intents.FirstOrDefault(i => i.Id == intentId);

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

        foreach (var intent in Intents)
        {
            if (!ordered.Contains(intent))
                ordered.Add(intent);
        }

        return ordered;
    }

    public void AddIntent(Intent intent)
    {
        Intents.Add(intent);
        IntentOrder.Insert(0, intent.Id);
    }

    public void RemoveIntent(string intentId)
    {
        var intent = GetIntent(intentId);
        if (intent != null)
        {
            Intents.Remove(intent);
            IntentOrder.Remove(intentId);
            VisibleIntentIds.Remove(intentId);

            if (CurrentIntentId == intentId)
                CurrentIntentId = null;
        }
    }
}
