namespace TerminalHost.Core.Domain;

/// <summary>
/// Estimates token counts from text content using character-based heuristics.
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Average characters per token (industry standard approximation).
    /// </summary>
    private const int CharsPerToken = 4;

    /// <summary>
    /// Estimates token count for general text content.
    /// </summary>
    public static int Estimate(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        return Math.Max(1, content.Length / CharsPerToken);
    }

    /// <summary>
    /// Estimates token count for search tool results (Grep/Glob output).
    /// Search results are structured and repetitive, so use a lower multiplier.
    /// </summary>
    public static int EstimateSearchResult(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        return Math.Max(1, (int)(content.Length / CharsPerToken * 0.8));
    }

    /// <summary>
    /// Estimates token cost of a tool result based on tool name and content.
    /// </summary>
    public static int EstimateToolResult(string toolName, string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        return toolName is "Grep" or "Glob"
            ? EstimateSearchResult(content)
            : Estimate(content);
    }
}
