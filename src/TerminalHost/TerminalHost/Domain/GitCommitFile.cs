namespace TerminalHost.Domain;

public class GitCommitFile
{
    public string FilePath { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? "";
    public GitFileStatusType Status { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }

    // For renamed files
    public string? OriginalPath { get; set; }

    public string StatusIcon => Status switch
    {
        GitFileStatusType.Modified => "M",
        GitFileStatusType.Added => "A",
        GitFileStatusType.Deleted => "D",
        GitFileStatusType.Renamed => "R",
        GitFileStatusType.Copied => "C",
        GitFileStatusType.TypeChanged => "T",
        _ => "?"
    };

    public string StatusColor => Status switch
    {
        GitFileStatusType.Modified => "#E2C08D",   // Yellow/orange
        GitFileStatusType.Added => "#4EC9B0",      // Green
        GitFileStatusType.Deleted => "#F14C4C",    // Red
        GitFileStatusType.Renamed => "#569CD6",    // Blue
        GitFileStatusType.Copied => "#569CD6",     // Blue
        GitFileStatusType.TypeChanged => "#C586C0", // Purple
        _ => "#CCCCCC"
    };

    public string StatsDisplay
    {
        get
        {
            if (Additions == 0 && Deletions == 0)
                return "";

            var parts = new List<string>();
            if (Additions > 0) parts.Add($"+{Additions}");
            if (Deletions > 0) parts.Add($"-{Deletions}");

            return string.Join(" ", parts);
        }
    }
}
