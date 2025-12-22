namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a file changed in a GitHub pull request.
/// </summary>
public class GitHubPrFile
{
    /// <summary>
    /// The file path relative to the repository root.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The file status: "added", "removed", "modified", "renamed".
    /// </summary>
    public string Status { get; set; } = "modified";

    /// <summary>
    /// Number of lines added.
    /// </summary>
    public int Additions { get; set; }

    /// <summary>
    /// Number of lines deleted.
    /// </summary>
    public int Deletions { get; set; }

    /// <summary>
    /// The previous path (for renamed files).
    /// </summary>
    public string? PreviousPath { get; set; }

    /// <summary>
    /// Gets just the filename from the path.
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// Gets the directory part of the path.
    /// </summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";

    /// <summary>
    /// Gets a formatted diff summary (e.g., "+142 -23").
    /// </summary>
    public string DiffSummary => $"+{Additions} -{Deletions}";

    /// <summary>
    /// Gets a status indicator character.
    /// </summary>
    public string StatusIndicator => Status switch
    {
        "added" => "A",
        "removed" => "D",
        "modified" => "M",
        "renamed" => "R",
        _ => "?"
    };
}
