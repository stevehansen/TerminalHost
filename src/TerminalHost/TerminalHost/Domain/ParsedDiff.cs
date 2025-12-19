namespace TerminalHost.Domain;

/// <summary>
/// Type of a diff line.
/// </summary>
public enum DiffLineType
{
    /// <summary>
    /// Context line (unchanged).
    /// </summary>
    Context,

    /// <summary>
    /// Added line.
    /// </summary>
    Addition,

    /// <summary>
    /// Deleted line.
    /// </summary>
    Deletion,

    /// <summary>
    /// Hunk header (@@ ... @@).
    /// </summary>
    HunkHeader,

    /// <summary>
    /// File header or metadata.
    /// </summary>
    Header
}

/// <summary>
/// Represents a single line in a unified diff.
/// </summary>
public record DiffLine(
    int? OldLineNumber,
    int? NewLineNumber,
    string Content,
    DiffLineType Type
);

/// <summary>
/// Represents a hunk in a diff (a contiguous section of changes).
/// </summary>
public class DiffHunk
{
    public int OldStart { get; init; }
    public int OldCount { get; init; }
    public int NewStart { get; init; }
    public int NewCount { get; init; }
    public string? Context { get; init; }
    public List<DiffLine> Lines { get; init; } = [];
}

/// <summary>
/// Represents a parsed unified diff for a single file.
/// </summary>
public class ParsedDiff
{
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
    public List<string> HeaderLines { get; init; } = [];
    public List<DiffHunk> Hunks { get; init; } = [];
}

/// <summary>
/// Represents a single row in a side-by-side diff view.
/// </summary>
public class SideBySideDiffRow
{
    /// <summary>
    /// Left side (old file) line number.
    /// </summary>
    public int? LeftLineNumber { get; init; }

    /// <summary>
    /// Left side content.
    /// </summary>
    public string? LeftContent { get; init; }

    /// <summary>
    /// Left side line type.
    /// </summary>
    public DiffLineType LeftType { get; init; }

    /// <summary>
    /// Right side (new file) line number.
    /// </summary>
    public int? RightLineNumber { get; init; }

    /// <summary>
    /// Right side content.
    /// </summary>
    public string? RightContent { get; init; }

    /// <summary>
    /// Right side line type.
    /// </summary>
    public DiffLineType RightType { get; init; }

    /// <summary>
    /// Whether this row represents a hunk header.
    /// </summary>
    public bool IsHunkHeader { get; init; }

    /// <summary>
    /// The hunk header text (if IsHunkHeader is true).
    /// </summary>
    public string? HunkHeaderText { get; init; }
}
