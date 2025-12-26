namespace TerminalHost.Domain;

public class GitCommit
{
    public string Hash { get; set; } = "";
    public string ShortHash => Hash.Length >= 7 ? Hash[..7] : Hash;
    public string Author { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public DateTime Date { get; set; }
    public string Subject { get; set; } = "";

    // Display helpers
    public string RelativeDate
    {
        get
        {
            var span = DateTime.Now - Date;

            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";

            return $"{(int)(span.TotalDays / 365)}y ago";
        }
    }

    public string AuthorInitials
    {
        get
        {
            var parts = Author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            if (parts.Length == 1 && parts[0].Length >= 2)
                return parts[0][..2].ToUpper();
            return Author.Length >= 2 ? Author[..2].ToUpper() : Author.ToUpper();
        }
    }

    // Color based on author email hash for consistent coloring per author
    public string AuthorColor
    {
        get
        {
            var colors = new[]
            {
                "#4EC9B0", // Cyan
                "#569CD6", // Blue
                "#C586C0", // Purple
                "#E2C08D", // Yellow
                "#CE9178", // Orange
                "#6A9955", // Green
                "#D7BA7D", // Gold
                "#9CDCFE"  // Light blue
            };

            var hash = AuthorEmail.GetHashCode();
            return colors[Math.Abs(hash) % colors.Length];
        }
    }
}
