namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a detected link from terminal output.
/// </summary>
public class DetectedLink
{
    /// <summary>
    /// Short display text for the link (truncated if needed).
    /// </summary>
    public string DisplayText { get; set; } = "";

    /// <summary>
    /// The full URL or file path to open.
    /// </summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// The type of link detected.
    /// </summary>
    public LinkType LinkType { get; set; }

    /// <summary>
    /// Icon to display based on link type.
    /// </summary>
    public string Icon => LinkType switch
    {
        LinkType.Url => "🔗",
        LinkType.File => "📄",
        LinkType.Directory => "📁",
        LinkType.Custom => "🏷️",
        _ => "🔗"
    };

    /// <summary>
    /// Whether this link points to a file that can be previewed.
    /// </summary>
    public bool IsFile => LinkType == LinkType.File;
}
