using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a repository item for the quick access switcher.
/// </summary>
public partial class RepositoryItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIndicator))]
    private bool _isFavorite;

    /// <summary>
    /// The repository full name (e.g., "owner/repo").
    /// For local-only repos, this is the folder name.
    /// </summary>
    public string FullName { get; set; } = "";

    /// <summary>
    /// Local path to the repository on disk.
    /// Empty if the repository is not cloned locally.
    /// </summary>
    public string LocalPath { get; set; } = "";

    /// <summary>
    /// Whether the repository exists on the local disk.
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// Whether this repository is currently open in a tab.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// Whether this repository is in the recent folders list.
    /// </summary>
    public bool IsRecent { get; set; }

    /// <summary>
    /// When the repository was last accessed.
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// The repository description (from GitHub).
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets just the repository name without owner.
    /// </summary>
    public string Name => FullName.Contains('/') ? FullName.Split('/').Last() : FullName;

    /// <summary>
    /// Gets the owner part of the full name.
    /// </summary>
    public string Owner => FullName.Contains('/') ? FullName.Split('/').First() : "";

    /// <summary>
    /// Gets a human-readable time since last access.
    /// </summary>
    public string TimeSinceLastAccess
    {
        get
        {
            if (LastAccessedAt == null)
                return "";

            var diff = DateTime.UtcNow - LastAccessedAt.Value;
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            return LastAccessedAt.Value.ToString("MMM dd");
        }
    }

    /// <summary>
    /// Gets a status indicator for display.
    /// </summary>
    public string StatusIndicator
    {
        get
        {
            if (IsFavorite) return "*";  // Star
            if (IsOpen) return ">";      // Currently open
            if (IsRecent) return "r";    // Recent
            if (!IsLocal) return "c";    // Cloud/remote only
            return "";
        }
    }

    /// <summary>
    /// Gets a display label combining full name and local indicator.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            if (string.IsNullOrEmpty(FullName) && !string.IsNullOrEmpty(LocalPath))
            {
                // Local-only repo without a GitHub remote
                return System.IO.Path.GetFileName(LocalPath);
            }
            return FullName;
        }
    }
}

/// <summary>
/// Grouping category for repository items.
/// </summary>
public enum RepositoryGroup
{
    /// <summary>
    /// Favorited repositories.
    /// </summary>
    Favorites,

    /// <summary>
    /// Currently open in tabs.
    /// </summary>
    Open,

    /// <summary>
    /// Recently accessed.
    /// </summary>
    Recent,

    /// <summary>
    /// All other repositories.
    /// </summary>
    All
}
