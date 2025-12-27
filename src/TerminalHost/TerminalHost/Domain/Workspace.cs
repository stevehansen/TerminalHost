using System.IO;
using System.Text.Json.Serialization;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a workspace (project directory) for the sidebar.
/// Used for tracking recent workspaces and favorites.
/// </summary>
public class Workspace
{
    /// <summary>
    /// The full path to the workspace directory.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Display name for the workspace. Defaults to directory name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Whether this workspace is a playground/experimental project.
    /// </summary>
    [JsonPropertyName("isPlayground")]
    public bool IsPlayground { get; set; }

    /// <summary>
    /// When this workspace was last opened.
    /// </summary>
    [JsonPropertyName("lastOpened")]
    public DateTime LastOpened { get; set; }

    /// <summary>
    /// Cached git branch name (updated when workspace is opened).
    /// </summary>
    [JsonPropertyName("gitBranch")]
    public string? GitBranch { get; set; }

    /// <summary>
    /// Display name - uses Name if set, otherwise extracts from Path.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrEmpty(Name)
        ? Name
        : System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    /// <summary>
    /// Abbreviated path for display (replaces home with ~).
    /// </summary>
    [JsonIgnore]
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Path.StartsWith(home))
                return "~" + Path[home.Length..];
            return Path;
        }
    }

    /// <summary>
    /// Relative time since last opened (e.g., "2 hours ago").
    /// </summary>
    [JsonIgnore]
    public string RelativeLastOpened
    {
        get
        {
            var diff = DateTime.Now - LastOpened;
            if (diff.TotalMinutes < 1) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
            return LastOpened.ToString("MMM d");
        }
    }

    /// <summary>
    /// Creates a workspace from a directory path.
    /// </summary>
    public static Workspace FromPath(string path)
    {
        return new Workspace
        {
            Path = path,
            Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
            LastOpened = DateTime.Now
        };
    }
}
