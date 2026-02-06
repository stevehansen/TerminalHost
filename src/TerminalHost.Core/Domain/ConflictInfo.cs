namespace TerminalHost.Core.Domain;

public class ConflictHunk
{
    public string OursContent { get; set; } = "";
    public string TheirsContent { get; set; } = "";
    public string? BaseContent { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public class ConflictInfo
{
    public string FilePath { get; set; } = "";
    public List<ConflictHunk> Hunks { get; set; } = [];
    public string FullContent { get; set; } = "";
    public List<string> ResolvedLines { get; set; } = [];
}

public enum ConflictResolution { AcceptOurs, AcceptTheirs, AcceptBoth, Manual }
