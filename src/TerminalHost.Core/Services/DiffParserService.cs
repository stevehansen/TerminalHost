using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for parsing unified diff content and converting to side-by-side format.
/// </summary>
public partial class DiffParserService : IDiffParserService
{
    // Regex patterns
    [GeneratedRegex(@"^diff --git", RegexOptions.Compiled)]
    private static partial Regex DiffHeaderRegex();

    [GeneratedRegex(@"^(---|\+\+\+) (.+)", RegexOptions.Compiled)]
    private static partial Regex FilePathRegex();

    [GeneratedRegex(@"^@@\s+-(\d+)(?:,(\d+))?\s+\+(\d+)(?:,(\d+))?\s+@@(.*)$", RegexOptions.Compiled)]
    private static partial Regex HunkHeaderRegex();

    [GeneratedRegex(@"^(index|new file mode|deleted file mode|old mode|new mode|similarity index|rename from|rename to|copy from|copy to|Binary files)", RegexOptions.Compiled)]
    private static partial Regex MetaRegex();

    public ParsedDiff Parse(string unifiedDiff)
    {
        if (string.IsNullOrEmpty(unifiedDiff))
        {
            return new ParsedDiff();
        }

        var result = new ParsedDiff
        {
            HeaderLines = [],
            Hunks = []
        };

        var lines = unifiedDiff.Split('\n');
        DiffHunk? currentHunk = null;
        int oldLineNumber = 0;
        int newLineNumber = 0;
        bool inHeader = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Check for diff header
            if (DiffHeaderRegex().IsMatch(line))
            {
                result.HeaderLines.Add(line);
                inHeader = true;
                continue;
            }

            // Check for file paths
            var filePathMatch = FilePathRegex().Match(line);
            if (filePathMatch.Success)
            {
                var prefix = filePathMatch.Groups[1].Value;
                var path = filePathMatch.Groups[2].Value;

                // Remove a/ or b/ prefix if present
                if (path.StartsWith("a/") || path.StartsWith("b/"))
                {
                    path = path[2..];
                }

                if (prefix == "---")
                {
                    result.OldPath = path;
                }
                else
                {
                    result.NewPath = path;
                }

                result.HeaderLines.Add(line);
                continue;
            }

            // Check for meta lines
            if (MetaRegex().IsMatch(line))
            {
                result.HeaderLines.Add(line);
                continue;
            }

            // Check for hunk header
            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success)
            {
                inHeader = false;

                oldLineNumber = int.Parse(hunkMatch.Groups[1].Value);
                var oldCount = hunkMatch.Groups[2].Success ? int.Parse(hunkMatch.Groups[2].Value) : 1;
                newLineNumber = int.Parse(hunkMatch.Groups[3].Value);
                var newCount = hunkMatch.Groups[4].Success ? int.Parse(hunkMatch.Groups[4].Value) : 1;
                var context = hunkMatch.Groups[5].Value.Trim();

                currentHunk = new DiffHunk
                {
                    OldStart = oldLineNumber,
                    OldCount = oldCount,
                    NewStart = newLineNumber,
                    NewCount = newCount,
                    Context = string.IsNullOrEmpty(context) ? null : context,
                    Lines = []
                };

                currentHunk.Lines.Add(new DiffLine(null, null, line, DiffLineType.HunkHeader));
                result.Hunks.Add(currentHunk);
                continue;
            }

            // Skip header lines before first hunk
            if (inHeader)
            {
                result.HeaderLines.Add(line);
                continue;
            }

            // Process diff content lines
            if (currentHunk != null)
            {
                if (line.StartsWith('+') && !line.StartsWith("+++"))
                {
                    // Addition
                    currentHunk.Lines.Add(new DiffLine(null, newLineNumber, line[1..], DiffLineType.Addition));
                    newLineNumber++;
                }
                else if (line.StartsWith('-') && !line.StartsWith("---"))
                {
                    // Deletion
                    currentHunk.Lines.Add(new DiffLine(oldLineNumber, null, line[1..], DiffLineType.Deletion));
                    oldLineNumber++;
                }
                else if (line.StartsWith(' '))
                {
                    // Context line
                    currentHunk.Lines.Add(new DiffLine(oldLineNumber, newLineNumber, line[1..], DiffLineType.Context));
                    oldLineNumber++;
                    newLineNumber++;
                }
                else if (string.IsNullOrEmpty(line))
                {
                    // Empty context line (git sometimes omits the leading space for empty lines)
                    currentHunk.Lines.Add(new DiffLine(oldLineNumber, newLineNumber, "", DiffLineType.Context));
                    oldLineNumber++;
                    newLineNumber++;
                }
            }
        }

        return result;
    }

    public List<SideBySideDiffRow> ConvertToSideBySide(ParsedDiff parsedDiff)
    {
        var rows = new List<SideBySideDiffRow>();

        foreach (var hunk in parsedDiff.Hunks)
        {
            var hunkLines = hunk.Lines;
            var i = 0;

            while (i < hunkLines.Count)
            {
                var line = hunkLines[i];

                switch (line.Type)
                {
                    case DiffLineType.HunkHeader:
                        // Add hunk header as a special row spanning both sides
                        rows.Add(new SideBySideDiffRow
                        {
                            IsHunkHeader = true,
                            HunkHeaderText = line.Content
                        });
                        i++;
                        break;

                    case DiffLineType.Context:
                        // Context lines appear on both sides
                        rows.Add(new SideBySideDiffRow
                        {
                            LeftLineNumber = line.OldLineNumber,
                            LeftContent = line.Content,
                            LeftType = DiffLineType.Context,
                            RightLineNumber = line.NewLineNumber,
                            RightContent = line.Content,
                            RightType = DiffLineType.Context
                        });
                        i++;
                        break;

                    case DiffLineType.Deletion:
                        // Look ahead for matching additions to pair with
                        var deletions = new List<DiffLine>();
                        while (i < hunkLines.Count && hunkLines[i].Type == DiffLineType.Deletion)
                        {
                            deletions.Add(hunkLines[i]);
                            i++;
                        }

                        var additions = new List<DiffLine>();
                        while (i < hunkLines.Count && hunkLines[i].Type == DiffLineType.Addition)
                        {
                            additions.Add(hunkLines[i]);
                            i++;
                        }

                        // Pair deletions with additions side-by-side
                        var maxPairs = Math.Max(deletions.Count, additions.Count);
                        for (int j = 0; j < maxPairs; j++)
                        {
                            var deletion = j < deletions.Count ? deletions[j] : null;
                            var addition = j < additions.Count ? additions[j] : null;

                            rows.Add(new SideBySideDiffRow
                            {
                                LeftLineNumber = deletion?.OldLineNumber,
                                LeftContent = deletion?.Content ?? "",
                                LeftType = deletion != null ? DiffLineType.Deletion : DiffLineType.Context,
                                RightLineNumber = addition?.NewLineNumber,
                                RightContent = addition?.Content ?? "",
                                RightType = addition != null ? DiffLineType.Addition : DiffLineType.Context
                            });
                        }
                        break;

                    case DiffLineType.Addition:
                        // Standalone addition (shouldn't normally happen if deletions come first)
                        rows.Add(new SideBySideDiffRow
                        {
                            LeftLineNumber = null,
                            LeftContent = "",
                            LeftType = DiffLineType.Context,
                            RightLineNumber = line.NewLineNumber,
                            RightContent = line.Content,
                            RightType = DiffLineType.Addition
                        });
                        i++;
                        break;

                    default:
                        i++;
                        break;
                }
            }
        }

        return rows;
    }

    public List<SideBySideDiffRow> ParseToSideBySide(string unifiedDiff)
    {
        var parsed = Parse(unifiedDiff);
        return ConvertToSideBySide(parsed);
    }

    public string ExtractHunkPatch(ParsedDiff parsedDiff, int hunkIndex)
    {
        if (hunkIndex < 0 || hunkIndex >= parsedDiff.Hunks.Count)
            return "";

        var sb = new System.Text.StringBuilder();

        // File headers
        foreach (var header in parsedDiff.HeaderLines)
        {
            sb.AppendLine(header);
        }

        // Single hunk
        var hunk = parsedDiff.Hunks[hunkIndex];
        foreach (var line in hunk.Lines)
        {
            switch (line.Type)
            {
                case DiffLineType.HunkHeader:
                    sb.AppendLine(line.Content);
                    break;
                case DiffLineType.Addition:
                    sb.AppendLine("+" + line.Content);
                    break;
                case DiffLineType.Deletion:
                    sb.AppendLine("-" + line.Content);
                    break;
                case DiffLineType.Context:
                    sb.AppendLine(" " + line.Content);
                    break;
            }
        }

        return sb.ToString();
    }
}
