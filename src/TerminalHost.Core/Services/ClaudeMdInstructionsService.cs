using System.Text;
using System.Text.RegularExpressions;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Status of the memory instructions block in ~/.claude/CLAUDE.md.
/// </summary>
public enum ClaudeMdStatus
{
    /// <summary>File doesn't exist or section not found.</summary>
    NotConfigured,

    /// <summary>Section found and matches current version.</summary>
    Configured,

    /// <summary>Section found but content differs from current version.</summary>
    Outdated,
}

/// <summary>
/// Manages the memory instructions section in ~/.claude/CLAUDE.md.
/// Handles detection, insertion, update, and removal with proper EOL preservation.
/// </summary>
public class ClaudeMdInstructionsService
{
    private const string SectionHeading = "## Memory System (via TerminalHost MCP)";
    private const string VersionMarker = "<!-- TerminalHost:Memory:v";
    private const int CurrentVersion = 2;

    private readonly IFileSystem _fileSystem;

    public ClaudeMdInstructionsService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Get the path to ~/.claude/CLAUDE.md
    /// </summary>
    public static string GetClaudeMdPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "CLAUDE.md");
    }

    /// <summary>
    /// Check the current status of memory instructions in CLAUDE.md.
    /// </summary>
    public ClaudeMdStatus CheckStatus()
    {
        var path = GetClaudeMdPath();
        if (!_fileSystem.FileExists(path))
            return ClaudeMdStatus.NotConfigured;

        var content = _fileSystem.ReadAllText(path);
        return CheckStatusFromContent(content);
    }

    /// <summary>
    /// Install or update the memory instructions section.
    /// </summary>
    public void InstallOrUpdate()
    {
        var path = GetClaudeMdPath();
        var dir = Path.GetDirectoryName(path)!;

        if (!_fileSystem.DirectoryExists(dir))
            _fileSystem.CreateDirectory(dir);

        string content;
        string eol;

        if (_fileSystem.FileExists(path))
        {
            var bytes = _fileSystem.ReadAllBytes(path);
            content = Encoding.UTF8.GetString(bytes);
            eol = DetectEol(content);
        }
        else
        {
            content = "";
            eol = Environment.NewLine;
        }

        var instructions = GetInstructionsBlock(eol);
        var status = CheckStatusFromContent(content);

        string newContent;
        if (status == ClaudeMdStatus.NotConfigured)
        {
            // Append to end with spacing
            var trimmed = content.TrimEnd();
            if (trimmed.Length > 0)
                newContent = trimmed + eol + eol + instructions + eol;
            else
                newContent = instructions + eol;
        }
        else
        {
            // Replace existing section
            newContent = ReplaceSectionInContent(content, instructions, eol);
        }

        WriteWithEol(path, newContent, eol);
    }

    /// <summary>
    /// Remove the memory instructions section from CLAUDE.md.
    /// </summary>
    public bool Remove()
    {
        var path = GetClaudeMdPath();
        if (!_fileSystem.FileExists(path))
            return false;

        var bytes = _fileSystem.ReadAllBytes(path);
        var content = Encoding.UTF8.GetString(bytes);
        var eol = DetectEol(content);

        if (CheckStatusFromContent(content) == ClaudeMdStatus.NotConfigured)
            return false;

        var newContent = RemoveSectionFromContent(content, eol);
        WriteWithEol(path, newContent, eol);
        return true;
    }

    // ── Internal helpers ──────────────────────────────────────────────

    internal ClaudeMdStatus CheckStatusFromContent(string content)
    {
        if (!content.Contains(SectionHeading))
            return ClaudeMdStatus.NotConfigured;

        // Check version marker
        var versionMatch = Regex.Match(content, @"<!-- TerminalHost:Memory:v(\d+) -->");
        if (!versionMatch.Success)
            return ClaudeMdStatus.Outdated;

        var version = int.Parse(versionMatch.Groups[1].Value);
        return version == CurrentVersion
            ? ClaudeMdStatus.Configured
            : ClaudeMdStatus.Outdated;
    }

    private string ReplaceSectionInContent(string content, string newSection, string eol)
    {
        var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();

        var startIdx = lines.FindIndex(l => l.TrimStart().StartsWith(SectionHeading));
        if (startIdx < 0)
            return content; // shouldn't happen

        // Find end: next same-level or higher heading (## or #), or EOF
        var endIdx = startIdx + 1;
        while (endIdx < lines.Count)
        {
            var trimmed = lines[endIdx].TrimStart();
            if (trimmed.StartsWith("## ") || trimmed.StartsWith("# "))
                break;
            endIdx++;
        }

        // Remove trailing blank lines before next section
        while (endIdx > startIdx + 1 && string.IsNullOrWhiteSpace(lines[endIdx - 1]))
            endIdx--;

        // Replace
        var newLines = newSection.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        lines.RemoveRange(startIdx, endIdx - startIdx);
        lines.InsertRange(startIdx, newLines);

        return string.Join(eol, lines);
    }

    private string RemoveSectionFromContent(string content, string eol)
    {
        var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();

        var startIdx = lines.FindIndex(l => l.TrimStart().StartsWith(SectionHeading));
        if (startIdx < 0)
            return content;

        // Find end: next same-level or higher heading, or EOF
        var endIdx = startIdx + 1;
        while (endIdx < lines.Count)
        {
            var trimmed = lines[endIdx].TrimStart();
            if (trimmed.StartsWith("## ") || trimmed.StartsWith("# "))
                break;
            endIdx++;
        }

        // Also remove leading blank lines before the section
        while (startIdx > 0 && string.IsNullOrWhiteSpace(lines[startIdx - 1]))
            startIdx--;

        lines.RemoveRange(startIdx, endIdx - startIdx);

        return string.Join(eol, lines);
    }

    private static string DetectEol(string content)
    {
        var crlf = content.Count(c => false); // count manually
        int crlfCount = 0, lfCount = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                crlfCount++;
                i++; // skip the \n
            }
            else if (content[i] == '\n')
            {
                lfCount++;
            }
        }

        return crlfCount >= lfCount ? "\r\n" : "\n";
    }

    private void WriteWithEol(string path, string content, string eol)
    {
        // Normalize all line endings to the detected EOL style
        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        if (eol == "\r\n")
            normalized = normalized.Replace("\n", "\r\n");

        _fileSystem.WriteAllBytes(path, Encoding.UTF8.GetBytes(normalized));
    }

    private static string GetInstructionsBlock(string eol)
    {
        var lines = new[]
        {
            SectionHeading,
            $"{VersionMarker}{CurrentVersion} -->",
            "",
            "When the `memory_*` MCP tools are available, use them to maintain long-term project knowledge:",
            "",
            "### Session Start",
            "- Call `memory_recall` with a query relevant to the current task to load prior context",
            "- The system auto-injects a compact project summary, but targeted recall gives deeper results",
            "",
            "### During Work",
            "- **Store observations** (`memory_store` type=observation) when you discover something non-obvious:",
            "  - Bug root causes, tricky configurations, \"gotchas\"",
            "  - Architecture decisions and their rationale",
            "  - Important relationships between components",
            "- **Store insights** (`memory_store` type=insight) for stable knowledge you've confirmed:",
            "  - Patterns that work well in this codebase",
            "  - Integration points, API contracts, deployment notes",
            "- **Store procedures** (`memory_store` type=procedure) for multi-step workflows:",
            "  - How to set up/test/deploy specific features",
            "  - Debugging recipes for recurring issues",
            "- **Store heuristics** (`memory_store` type=heuristic) for rules of thumb and decision shortcuts:",
            "  - \"Always run migrations before tests in this repo\"",
            "  - \"The CI flakes on Windows when path > 260 chars\"",
            "- Do NOT store things easily derived from code or git history",
            "- Set provenance: `user_stated` for facts the user told you, `agent_inferred` (default) for your own observations",
            "",
            "### Cross-Repo Knowledge",
            "- Use `memory_recall` with `cross_repo: true` (default) to search linked project memories",
            "- NuGet/npm dependencies are auto-detected — sibling project memories surface automatically",
            "- Use `memory_link` to manually link related repos (e.g., `relation: \"depends-on\"`)",
            "",
            "### Periodically",
            "- Call `memory_consolidate` when you've stored several observations in a session — it merges related observations into insights",
            "- Use `memory_forget` to invalidate memories that are no longer true",
            "- Use `memory_feedback` to echo (useful) or fizzle (not useful) recalled memories — this tunes future recall",
            "",
            "### Other Tools",
            "- `memory_intake`: Re-scan project files (CLAUDE.md, README, etc.) to seed memories",
            "- `memory_export`: Export memories as markdown for human review",
            "- `memory_history`: View version chain for a specific memory",
            "",
            "### Guidelines",
            "- Be specific and self-contained in memory content — future sessions won't have today's context",
            "- Use tags for discoverability (e.g., \"git\", \"testing\", \"raven\", \"deployment\")",
            "- Set importance: 0.3=minor, 0.5=normal, 0.8=important, 1.0=critical",
        };

        return string.Join(eol, lines);
    }
}
