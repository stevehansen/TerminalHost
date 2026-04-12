namespace TerminalHost.Core.Services;

/// <summary>
/// Converts directory paths to stable, filesystem-safe identifiers.
/// Uses the same encoding as Claude Code's project path format (~/.claude/projects/).
/// </summary>
public static class RepoIdNormalizer
{
    /// <summary>
    /// "P:\TerminalHost" → "P--TerminalHost"
    /// "/Users/steve/projects/my-app" → "-Users-steve-projects-my-app"
    /// </summary>
    public static string Normalize(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return "unknown";

        // Match Claude Code's EncodeClaudeProjectPath format exactly
        var normalized = directoryPath
            .TrimEnd('\\', '/')
            .Replace(':', '-')
            .Replace('\\', '-')
            .Replace('/', '-')
            .Replace('.', '-')
            .Replace('_', '-');

        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }
}
