namespace TerminalHost.Core.Domain;

/// <summary>
/// Query parameters for retrieving commit history.
/// </summary>
public record GitHistoryQuery(
    int Count = 50,
    string? Author = null,
    string? FilePath = null,
    string? SearchText = null,
    DateTimeOffset? AfterDate = null,
    DateTimeOffset? BeforeDate = null);
