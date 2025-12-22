using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Interface for parsing unified diff content.
/// </summary>
public interface IDiffParserService
{
    /// <summary>
    /// Parse a unified diff string into a structured format.
    /// </summary>
    ParsedDiff Parse(string unifiedDiff);

    /// <summary>
    /// Convert a parsed diff to side-by-side format.
    /// </summary>
    List<SideBySideDiffRow> ConvertToSideBySide(ParsedDiff parsedDiff);

    /// <summary>
    /// Parse and convert a unified diff string directly to side-by-side format.
    /// </summary>
    List<SideBySideDiffRow> ParseToSideBySide(string unifiedDiff);
}
