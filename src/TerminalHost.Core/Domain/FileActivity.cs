using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Tracks access patterns for a file during a session.
/// Unlike FileChange (which tracks git diff stats), this tracks how tools interacted with the file.
/// </summary>
public class FileActivity
{
    /// <summary>
    /// File path.
    /// </summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Number of times the file was read.
    /// </summary>
    [JsonPropertyName("readCount")]
    public int ReadCount { get; set; }

    /// <summary>
    /// Number of times the file was written/edited.
    /// </summary>
    [JsonPropertyName("writeCount")]
    public int WriteCount { get; set; }

    /// <summary>
    /// Number of times the file appeared in search results.
    /// </summary>
    [JsonPropertyName("searchHitCount")]
    public int SearchHitCount { get; set; }

    /// <summary>
    /// First time the file was accessed.
    /// </summary>
    [JsonPropertyName("firstAccess")]
    public DateTime FirstAccess { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time the file was accessed.
    /// </summary>
    [JsonPropertyName("lastAccess")]
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// File name without path.
    /// </summary>
    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Total number of accesses across all types.
    /// </summary>
    [JsonIgnore]
    public int TotalAccesses => ReadCount + WriteCount + SearchHitCount;

    /// <summary>
    /// Whether this file was modified (written to).
    /// </summary>
    [JsonIgnore]
    public bool WasModified => WriteCount > 0;

    /// <summary>
    /// Records a file access.
    /// </summary>
    public void RecordAccess(string accessType)
    {
        LastAccess = DateTime.UtcNow;
        switch (accessType)
        {
            case "read":
                ReadCount++;
                break;
            case "write":
                WriteCount++;
                break;
            case "search":
                SearchHitCount++;
                break;
        }
    }
}
