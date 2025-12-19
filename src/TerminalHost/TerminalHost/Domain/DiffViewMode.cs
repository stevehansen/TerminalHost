namespace TerminalHost.Domain;

/// <summary>
/// Mode for displaying diff content.
/// </summary>
public enum DiffViewMode
{
    /// <summary>
    /// Traditional unified diff format with + and - prefixes.
    /// </summary>
    Unified,

    /// <summary>
    /// Side-by-side diff with old content on left, new content on right.
    /// </summary>
    SideBySide
}
