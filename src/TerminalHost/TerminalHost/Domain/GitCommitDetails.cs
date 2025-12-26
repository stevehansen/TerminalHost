namespace TerminalHost.Domain;

public class GitCommitDetails
{
    public string Hash { get; set; } = "";
    public string ShortHash => Hash.Length >= 7 ? Hash[..7] : Hash;
    public string Author { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public DateTime Date { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public List<GitCommitFile> Files { get; set; } = [];

    // Summary stats
    public int TotalAdditions => Files.Sum(f => f.Additions);
    public int TotalDeletions => Files.Sum(f => f.Deletions);
    public int TotalFilesChanged => Files.Count;

    public string StatsDisplay
    {
        get
        {
            var parts = new List<string>
            {
                $"{TotalFilesChanged} file{(TotalFilesChanged != 1 ? "s" : "")}"
            };

            if (TotalAdditions > 0) parts.Add($"+{TotalAdditions}");
            if (TotalDeletions > 0) parts.Add($"-{TotalDeletions}");

            return string.Join("  ", parts);
        }
    }

    public string FullMessage => string.IsNullOrWhiteSpace(Body) ? Subject : $"{Subject}\n\n{Body}";

    public string RelativeDate
    {
        get
        {
            var span = DateTime.Now - Date;

            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minutes ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)} weeks ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} months ago";

            return $"{(int)(span.TotalDays / 365)} years ago";
        }
    }
}
