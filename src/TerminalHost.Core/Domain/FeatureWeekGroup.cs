namespace TerminalHost.Core.Domain;

/// <summary>
/// Groups features by the week they were introduced.
/// </summary>
public class FeatureWeekGroup(string weekLabel, DateOnly weekStart, List<FeatureEntry> features)
{
    public string WeekLabel { get; } = weekLabel;
    public DateOnly WeekStart { get; } = weekStart;
    public List<FeatureEntry> Features { get; } = features;
    public bool HasNewFeatures => Features.Any(f => f.IsNew);
}

/// <summary>
/// A single feature entry within a week group.
/// </summary>
public class FeatureEntry(PaletteCommand command, bool isNew)
{
    public PaletteCommand Command { get; } = command;
    public bool IsNew { get; } = isNew;
}
