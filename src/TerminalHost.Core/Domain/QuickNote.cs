using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// A quick note that may be converted to a task later.
/// </summary>
public class QuickNote
{
    /// <summary>Unique identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Note content.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>When the note was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// If this note was converted to a task, stores the task ID.
    /// Null if not yet converted.
    /// </summary>
    [JsonPropertyName("convertedToTaskId")]
    public string? ConvertedToTaskId { get; set; }

    /// <summary>Optional associated project path.</summary>
    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }

    /// <summary>Whether this note has been converted to a task.</summary>
    [JsonIgnore]
    public bool IsConverted => !string.IsNullOrEmpty(ConvertedToTaskId);

    /// <summary>
    /// Creates a new QuickNote with a generated ID.
    /// </summary>
    public static QuickNote Create(string text, string? projectPath = null)
    {
        return new QuickNote
        {
            Id = $"note-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
            Text = text,
            ProjectPath = projectPath,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets a preview of the note text (first line, truncated).
    /// </summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            var firstLine = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Text;
            return firstLine.Length > 60 ? firstLine[..57] + "..." : firstLine;
        }
    }
}
