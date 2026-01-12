namespace TerminalHost.Core.Domain;

/// <summary>
/// Information about a git submodule.
/// </summary>
public class SubmoduleInfo
{
    /// <summary>Path relative to the repository root.</summary>
    public string Path { get; set; } = "";

    /// <summary>Current commit hash in the submodule.</summary>
    public string CurrentCommit { get; set; } = "";

    /// <summary>The status of the submodule.</summary>
    public SubmoduleStatus Status { get; set; } = SubmoduleStatus.None;

    /// <summary>Optional description (e.g., tag name if on a tag).</summary>
    public string? Description { get; set; }
}
