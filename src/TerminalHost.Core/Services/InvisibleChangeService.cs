using System.Text;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Detects invisible (cosmetic) changes in diffs and can revert them.
/// </summary>
public class InvisibleChangeService : IInvisibleChangeService
{
    private readonly IFileSystem _fileSystem;
    private readonly IGitProcessRunner _gitRunner;

    private const char Bom = '\uFEFF';
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public InvisibleChangeService(IFileSystem fileSystem, IGitProcessRunner gitRunner)
    {
        _fileSystem = fileSystem;
        _gitRunner = gitRunner;
    }

    public InvisibleChangeInfo? Detect(string? rawDiff)
    {
        if (string.IsNullOrEmpty(rawDiff))
            return null;

        // Split on \n but keep \r so we can detect EOL differences
        var lines = rawDiff.Split('\n');

        bool inHunk = false;
        var deletions = new List<string>();
        var additions = new List<string>();
        bool noNewlineAfterDeletion = false;
        bool noNewlineAfterAddition = false;
        bool hasEolChange = false;
        bool deletionHasCr = false;
        bool additionHasCr = false;
        bool oldHasBom = false;
        bool newHasBom = false;
        bool firstDeletion = true;
        bool firstAddition = true;

        foreach (var rawLine in lines)
        {
            // Check for hunk header
            if (rawLine.StartsWith("@@") && rawLine.Contains("@@", StringComparison.Ordinal))
            {
                inHunk = true;
                continue;
            }

            if (!inHunk) continue;

            // Check for "\ No newline at end of file" marker
            if (rawLine.StartsWith("\\ No newline at end of file") ||
                rawLine.StartsWith("\\\r") && rawLine.Contains("No newline at end of file"))
            {
                // Determine which side this applies to based on what came before
                if (additions.Count > 0 &&
                    (deletions.Count == 0 || additions.Count > deletions.Count ||
                     additions[^1] == deletions.LastOrDefault()))
                {
                    noNewlineAfterAddition = true;
                }
                else if (deletions.Count > 0)
                {
                    noNewlineAfterDeletion = true;
                }
                continue;
            }

            if (rawLine.StartsWith('+') && !rawLine.StartsWith("+++"))
            {
                var content = rawLine[1..];
                additions.Add(content);

                if (content.EndsWith('\r'))
                    additionHasCr = true;

                if (firstAddition && content.TrimEnd('\r').StartsWith(Bom))
                    newHasBom = true;
                firstAddition = false;
            }
            else if (rawLine.StartsWith('-') && !rawLine.StartsWith("---"))
            {
                var content = rawLine[1..];
                deletions.Add(content);

                if (content.EndsWith('\r'))
                    deletionHasCr = true;

                if (firstDeletion && content.TrimEnd('\r').StartsWith(Bom))
                    oldHasBom = true;
                firstDeletion = false;
            }
        }

        // No changes found
        if (deletions.Count == 0 && additions.Count == 0)
            return null;

        // Determine EOL change
        if (deletions.Count > 0 && additions.Count > 0)
        {
            hasEolChange = deletionHasCr != additionHasCr;
        }

        // BOM change
        bool hasBomChange = oldHasBom != newHasBom;

        // Trailing newline change - the marker applies to the side that precedes it
        // We need to re-parse to get accurate "no newline" attribution
        bool hasTrailingNewlineChange = false;
        bool oldHasTrailingNewline = true;
        bool newHasTrailingNewline = true;

        // Re-scan for precise no-newline marker attribution
        (noNewlineAfterDeletion, noNewlineAfterAddition) = DetectNoNewlineMarkers(lines);

        if (noNewlineAfterDeletion != noNewlineAfterAddition)
        {
            hasTrailingNewlineChange = true;
            oldHasTrailingNewline = !noNewlineAfterDeletion;
            newHasTrailingNewline = !noNewlineAfterAddition;
        }

        // Determine if entirely invisible
        bool isEntirelyInvisible = false;
        if (hasEolChange || hasBomChange || hasTrailingNewlineChange)
        {
            isEntirelyInvisible = AreChangesEntirelyInvisible(deletions, additions);
        }

        if (!hasEolChange && !hasBomChange && !hasTrailingNewlineChange)
            return null;

        return new InvisibleChangeInfo
        {
            HasEolChange = hasEolChange,
            OldEol = hasEolChange ? (deletionHasCr ? "CRLF" : "LF") : null,
            NewEol = hasEolChange ? (additionHasCr ? "CRLF" : "LF") : null,
            HasBomChange = hasBomChange,
            OldHasBom = oldHasBom,
            NewHasBom = newHasBom,
            HasTrailingNewlineChange = hasTrailingNewlineChange,
            OldHasTrailingNewline = oldHasTrailingNewline,
            NewHasTrailingNewline = newHasTrailingNewline,
            IsEntirelyInvisible = isEntirelyInvisible
        };
    }

    public Task FixAsync(string workingDirectory, string filePath, InvisibleChangeInfo info)
    {
        var fullPath = Path.Combine(workingDirectory, filePath);
        if (!_fileSystem.FileExists(fullPath))
            return Task.CompletedTask;

        var bytes = _fileSystem.ReadAllBytes(fullPath);

        // BOM fix
        if (info.HasBomChange)
        {
            bool currentHasBom = bytes.Length >= 3 &&
                                 bytes[0] == Utf8Bom[0] &&
                                 bytes[1] == Utf8Bom[1] &&
                                 bytes[2] == Utf8Bom[2];

            if (info.OldHasBom && !currentHasBom)
            {
                // Old had BOM, current doesn't — add it back
                var newBytes = new byte[bytes.Length + 3];
                Utf8Bom.CopyTo(newBytes, 0);
                bytes.CopyTo(newBytes, 3);
                bytes = newBytes;
            }
            else if (!info.OldHasBom && currentHasBom)
            {
                // Old had no BOM, current has one — remove it
                bytes = bytes[3..];
            }
        }

        // Determine content start (skip BOM if present)
        int contentStart = 0;
        if (bytes.Length >= 3 &&
            bytes[0] == Utf8Bom[0] &&
            bytes[1] == Utf8Bom[1] &&
            bytes[2] == Utf8Bom[2])
        {
            contentStart = 3;
        }

        // EOL fix — convert between CRLF and LF
        if (info.HasEolChange)
        {
            var text = Encoding.UTF8.GetString(bytes, contentStart, bytes.Length - contentStart);

            if (info.OldEol == "CRLF")
            {
                // Revert to CRLF: normalize to LF first, then convert to CRLF
                text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
            else
            {
                // Revert to LF: strip all \r
                text = text.Replace("\r\n", "\n");
            }

            var textBytes = Encoding.UTF8.GetBytes(text);
            if (contentStart > 0)
            {
                var result = new byte[contentStart + textBytes.Length];
                Array.Copy(bytes, 0, result, 0, contentStart);
                textBytes.CopyTo(result, contentStart);
                bytes = result;
            }
            else
            {
                bytes = textBytes;
            }
        }

        // Trailing newline fix
        if (info.HasTrailingNewlineChange)
        {
            var eol = DetectCurrentEol(bytes, contentStart);

            if (info.OldHasTrailingNewline)
            {
                // Old had trailing newline — ensure file ends with one
                if (!EndsWithNewline(bytes))
                {
                    var eolBytes = Encoding.UTF8.GetBytes(eol);
                    var result = new byte[bytes.Length + eolBytes.Length];
                    bytes.CopyTo(result, 0);
                    eolBytes.CopyTo(result, bytes.Length);
                    bytes = result;
                }
            }
            else
            {
                // Old had no trailing newline — remove it if present
                while (EndsWithNewline(bytes))
                {
                    if (bytes.Length >= 2 && bytes[^2] == (byte)'\r' && bytes[^1] == (byte)'\n')
                        bytes = bytes[..^2];
                    else if (bytes[^1] == (byte)'\n')
                        bytes = bytes[..^1];
                    else
                        break;
                }
            }
        }

        _fileSystem.WriteAllBytes(fullPath, bytes);
        return Task.CompletedTask;
    }

    public async Task<EolDiagnosis?> DiagnoseEolIssueAsync(string workingDirectory, string filePath)
    {
        // Query core.autocrlf
        var autoCrlf = (await _gitRunner.RunGitCommandAsync(workingDirectory, "config core.autocrlf"))
            ?.Trim().ToLowerInvariant() ?? "";

        // Query core.eol
        var coreEol = (await _gitRunner.RunGitCommandAsync(workingDirectory, "config core.eol"))
            ?.Trim().ToLowerInvariant() ?? "";

        // Query what .gitattributes says for this file
        var attrOutput = (await _gitRunner.RunGitCommandAsync(
            workingDirectory, $"check-attr text eol -z -- \"{filePath}\""))
            ?.Trim() ?? "";

        // Parse check-attr NUL-delimited output: file\0attr\0value\0file\0attr\0value\0...
        var attrParts = attrOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        string textAttr = "unspecified";
        string eolAttr = "unspecified";
        for (int i = 0; i + 2 < attrParts.Length; i += 3)
        {
            var attr = attrParts[i + 1];
            var val = attrParts[i + 2];
            if (attr == "text") textAttr = val;
            if (attr == "eol") eolAttr = val;
        }

        // Build diagnosis
        var causes = new List<string>();
        bool isAutoCrlf = autoCrlf is "true" or "input";
        bool hasTextAttr = textAttr is not "unspecified" and not "unset";
        bool hasEolAttr = eolAttr is not "unspecified" and not "unset";

        if (isAutoCrlf)
            causes.Add($"core.autocrlf = {autoCrlf}");
        if (!string.IsNullOrEmpty(coreEol) && coreEol != "native")
            causes.Add($"core.eol = {coreEol}");
        if (hasTextAttr)
            causes.Add($".gitattributes text={textAttr}");
        if (hasEolAttr)
            causes.Add($".gitattributes eol={eolAttr}");

        if (causes.Count == 0)
            return null;

        var causeStr = string.Join(", ", causes);

        // Determine the best fix
        // If autocrlf or text attribute is involved, the index has normalized endings
        // but the working copy was written with the "wrong" endings. Renormalizing
        // the index aligns it with the configured policy and clears the diff.
        if (isAutoCrlf || hasTextAttr)
        {
            return new EolDiagnosis
            {
                Explanation = $"Git is converting line endings ({causeStr}). " +
                              "Renormalize will stage the file with correct EOL — commit to apply permanently.",
                FixCommand = $"add --renormalize -- \"{filePath}\"",
                FixLabel = "Renormalize"
            };
        }

        // eol attribute without text — informational only
        return new EolDiagnosis
        {
            Explanation = $"EOL mismatch caused by {causeStr}. " +
                          "Consider adding a .gitattributes rule for this file.",
            FixCommand = $"add --renormalize -- \"{filePath}\"",
            FixLabel = "Renormalize"
        };
    }

    public async Task<bool> RunDiagnosisFixAsync(string workingDirectory, EolDiagnosis diagnosis)
    {
        var result = await _gitRunner.RunGitOperationAsync(workingDirectory, diagnosis.FixCommand);
        return result.Success;
    }

    /// <summary>
    /// Parse "\ No newline at end of file" markers accurately by tracking
    /// which side (deletion or addition) immediately precedes each marker.
    /// </summary>
    private static (bool noNewlineAfterDeletion, bool noNewlineAfterAddition) DetectNoNewlineMarkers(string[] lines)
    {
        bool noNewlineAfterDeletion = false;
        bool noNewlineAfterAddition = false;
        bool inHunk = false;
        char lastSide = ' '; // '-' or '+'

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                inHunk = true;
                lastSide = ' ';
                continue;
            }
            if (!inHunk) continue;

            var trimmed = line.TrimEnd('\r');

            if (trimmed == "\\ No newline at end of file")
            {
                if (lastSide == '-')
                    noNewlineAfterDeletion = true;
                else if (lastSide == '+')
                    noNewlineAfterAddition = true;
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++"))
                lastSide = '+';
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                lastSide = '-';
            else if (line.StartsWith(' '))
                lastSide = ' ';
        }

        return (noNewlineAfterDeletion, noNewlineAfterAddition);
    }

    /// <summary>
    /// Compare deletion/addition pairs after normalizing away invisible differences.
    /// </summary>
    private static bool AreChangesEntirelyInvisible(List<string> deletions, List<string> additions)
    {
        if (deletions.Count != additions.Count)
            return false;

        for (int i = 0; i < deletions.Count; i++)
        {
            var d = Normalize(deletions[i]);
            var a = Normalize(additions[i]);
            if (d != a)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Normalize a line by stripping \r and BOM character.
    /// </summary>
    private static string Normalize(string s) =>
        s.TrimEnd('\r').TrimStart(Bom);

    private static bool EndsWithNewline(byte[] bytes) =>
        bytes.Length > 0 && bytes[^1] == (byte)'\n';

    private static string DetectCurrentEol(byte[] bytes, int contentStart)
    {
        for (int i = contentStart; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n')
                return "\r\n";
            if (bytes[i] == (byte)'\n')
                return "\n";
        }
        return "\n";
    }
}
