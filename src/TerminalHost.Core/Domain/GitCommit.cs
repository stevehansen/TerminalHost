namespace TerminalHost.Core.Domain;

public enum GitDecorationType
{
    Head,
    LocalBranch,
    RemoteBranch,
    Tag
}

public record GitDecoration(GitDecorationType Type, string DisplayText, string ColorHex);

/// <summary>
/// Represents a git commit in the commit history.
/// </summary>
public class GitCommit
{
    public string Hash { get; set; } = "";
    public string ShortHash { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string RelativeDate { get; set; } = "";
    public DateTimeOffset CommitDate { get; set; }
    public string Subject { get; set; } = "";
    public string? Body { get; set; }
    public string? Decorations { get; set; }
    public List<string> ParentHashes { get; set; } = [];
    public bool IsWipEntry { get; set; }

    /// <summary>
    /// Display format for the author - shows name only or name with email.
    /// </summary>
    public string AuthorDisplay => AuthorName;

    /// <summary>
    /// Indicates if this commit is a merge commit (has multiple parents).
    /// </summary>
    public bool IsMergeCommit => ParentHashes.Count > 1;

    /// <summary>
    /// Parsed decoration badges with type-specific colors.
    /// </summary>
    public List<GitDecoration> ParsedDecorations
    {
        get
        {
            if (string.IsNullOrEmpty(Decorations))
                return [];

            var result = new List<GitDecoration>();
            var parts = Decorations.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in parts)
            {
                var part = raw.Trim();
                if (string.IsNullOrEmpty(part)) continue;

                if (part.StartsWith("HEAD -> "))
                {
                    result.Add(new GitDecoration(GitDecorationType.Head, "HEAD", "#4EC9B0"));
                    var branchName = part.Substring("HEAD -> ".Length).Trim();
                    if (!string.IsNullOrEmpty(branchName))
                        result.Add(new GitDecoration(GitDecorationType.LocalBranch, branchName, "#569CD6"));
                }
                else if (part.StartsWith("tag: "))
                {
                    var tagName = part.Substring("tag: ".Length).Trim();
                    result.Add(new GitDecoration(GitDecorationType.Tag, tagName, "#DCDCAA"));
                }
                else if (part.Contains('/'))
                {
                    result.Add(new GitDecoration(GitDecorationType.RemoteBranch, part, "#C586C0"));
                }
                else
                {
                    result.Add(new GitDecoration(GitDecorationType.LocalBranch, part, "#569CD6"));
                }
            }

            return result;
        }
    }
}

/// <summary>
/// Extended commit information including file changes.
/// </summary>
public class GitCommitDetails : GitCommit
{
    public string CommitterName { get; set; } = "";
    public string CommitterEmail { get; set; } = "";
    public List<GitCommitFile> Files { get; set; } = [];
    public int TotalInsertions { get; set; }
    public int TotalDeletions { get; set; }
}

/// <summary>
/// Represents a file changed in a commit.
/// </summary>
public class GitCommitFile
{
    public string FilePath { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? "";
    public GitFileStatusType Status { get; set; }
    public int Insertions { get; set; }
    public int Deletions { get; set; }

    public string StatsDisplay => $"+{Insertions} -{Deletions}";

    public string StatusIcon => Status switch
    {
        GitFileStatusType.Modified => "M",
        GitFileStatusType.Added => "A",
        GitFileStatusType.Deleted => "D",
        GitFileStatusType.Renamed => "R",
        GitFileStatusType.Copied => "C",
        _ => "?"
    };

    public string StatusColor => Status switch
    {
        GitFileStatusType.Modified => "#E2C08D",
        GitFileStatusType.Added => "#4EC9B0",
        GitFileStatusType.Deleted => "#F14C4C",
        GitFileStatusType.Renamed => "#569CD6",
        GitFileStatusType.Copied => "#569CD6",
        _ => "#CCCCCC"
    };
}
