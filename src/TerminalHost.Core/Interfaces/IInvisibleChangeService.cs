using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Detects and fixes invisible changes (EOL, BOM, trailing newline) in diffs.
/// </summary>
public interface IInvisibleChangeService
{
    /// <summary>
    /// Analyse raw diff text and detect invisible changes.
    /// The diff should be the raw output from git (before any \r stripping).
    /// </summary>
    InvisibleChangeInfo? Detect(string? rawDiff);

    /// <summary>
    /// Fix invisible changes in a file by reverting its EOL, BOM, and trailing
    /// newline to match the old (HEAD) version described by <paramref name="info"/>.
    /// </summary>
    Task FixAsync(string workingDirectory, string filePath, InvisibleChangeInfo info);

    /// <summary>
    /// Diagnose WHY an EOL diff persists by querying git config (core.autocrlf,
    /// core.eol) and .gitattributes.  Returns a human-readable explanation
    /// and suggested fix command, or null when no diagnosis is available.
    /// </summary>
    Task<EolDiagnosis?> DiagnoseEolIssueAsync(string workingDirectory, string filePath);

    /// <summary>
    /// Run the git command from a diagnosis (e.g. "git add --renormalize -- file").
    /// Returns true if the command succeeded.
    /// </summary>
    Task<bool> RunDiagnosisFixAsync(string workingDirectory, EolDiagnosis diagnosis);
}
