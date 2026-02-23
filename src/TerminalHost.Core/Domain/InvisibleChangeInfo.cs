namespace TerminalHost.Core.Domain;

/// <summary>
/// Diagnosis of why an EOL diff persists, with a suggested fix.
/// </summary>
public sealed class EolDiagnosis
{
    /// <summary>Human-readable explanation of the root cause.</summary>
    public required string Explanation { get; init; }

    /// <summary>The git command that would fix the issue (e.g. "git add --renormalize .").</summary>
    public required string FixCommand { get; init; }

    /// <summary>Short label for a fix button.</summary>
    public required string FixLabel { get; init; }
}

/// <summary>
/// Describes invisible (cosmetic) changes detected in a file's diff:
/// EOL conversions (CRLF ↔ LF), UTF-8 BOM additions/removals, and
/// trailing-newline changes at EOF.
/// </summary>
public sealed class InvisibleChangeInfo
{
    /// <summary>True when the diff contains an EOL-style change.</summary>
    public bool HasEolChange { get; init; }

    /// <summary>Old EOL style (e.g. "CRLF" or "LF"). Null when no EOL change.</summary>
    public string? OldEol { get; init; }

    /// <summary>New EOL style (e.g. "CRLF" or "LF"). Null when no EOL change.</summary>
    public string? NewEol { get; init; }

    /// <summary>True when a UTF-8 BOM was added or removed.</summary>
    public bool HasBomChange { get; init; }

    /// <summary>True when the old version had a BOM.</summary>
    public bool OldHasBom { get; init; }

    /// <summary>True when the new version has a BOM.</summary>
    public bool NewHasBom { get; init; }

    /// <summary>True when the trailing newline at EOF changed.</summary>
    public bool HasTrailingNewlineChange { get; init; }

    /// <summary>True when the old version had a trailing newline at EOF.</summary>
    public bool OldHasTrailingNewline { get; init; }

    /// <summary>True when the new version has a trailing newline at EOF.</summary>
    public bool NewHasTrailingNewline { get; init; }

    /// <summary>
    /// True when every visible change in the diff is purely invisible
    /// (i.e. normalizing the content makes deletions and additions identical).
    /// </summary>
    public bool IsEntirelyInvisible { get; init; }

    /// <summary>Diagnosis of why the EOL diff persists (populated after fix attempt fails).</summary>
    public EolDiagnosis? Diagnosis { get; set; }

    /// <summary>Whether any invisible change was detected at all.</summary>
    public bool HasAnyChange => HasEolChange || HasBomChange || HasTrailingNewlineChange;

    /// <summary>Human-readable summary of the detected invisible changes.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (HasEolChange)
                parts.Add($"Line endings: {OldEol} \u2192 {NewEol}");

            if (HasBomChange)
                parts.Add(NewHasBom ? "BOM added" : "BOM removed");

            if (HasTrailingNewlineChange)
                parts.Add(NewHasTrailingNewline ? "Trailing newline added" : "Trailing newline removed");

            if (parts.Count == 0)
                return "";

            var summary = string.Join(", ", parts);
            if (IsEntirelyInvisible)
                summary += " (no visible content changes)";

            return summary;
        }
    }
}
