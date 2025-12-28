using System.Text.Json;

namespace TerminalHost.Services;

/// <summary>
/// Result of parsing a Claude Code transcript.
/// </summary>
public class TranscriptParseResult
{
    /// <summary>List of extracted bash commands.</summary>
    public List<string> Commands { get; set; } = [];

    /// <summary>AI summary text extracted from the transcript.</summary>
    public string? Summary { get; set; }

    /// <summary>Number of messages in the transcript.</summary>
    public int MessageCount { get; set; }

    /// <summary>Number of tool calls in the transcript.</summary>
    public int ToolCallCount { get; set; }

    /// <summary>Whether parsing was successful.</summary>
    public bool ParsedSuccessfully { get; set; }

    /// <summary>Error message if parsing failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Service for parsing Claude Code JSONL transcript files.
/// </summary>
public static class TranscriptParserService
{
    private const int MaxCommands = 20;

    /// <summary>
    /// Parses a Claude Code transcript file to extract commands and summary.
    /// </summary>
    /// <param name="transcriptPath">Path to the JSONL transcript file.</param>
    /// <returns>Parsed result with commands and summary.</returns>
    public static async Task<TranscriptParseResult> ParseAsync(string transcriptPath)
    {
        var result = new TranscriptParseResult();

        try
        {
            // Expand home directory if needed
            if (transcriptPath.StartsWith("~/"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                transcriptPath = Path.Combine(home, transcriptPath[2..]);
            }

            if (!File.Exists(transcriptPath))
            {
                result.Error = "Transcript file not found";
                return result;
            }

            var lines = await File.ReadAllLinesAsync(transcriptPath);
            var commands = new HashSet<string>();
            string? lastAssistantMessage = null;
            var toolCallCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    result.MessageCount++;

                    // Check message role
                    if (root.TryGetProperty("role", out var roleElement))
                    {
                        var role = roleElement.GetString();

                        if (role == "assistant")
                        {
                            // Extract text content for potential summary
                            if (root.TryGetProperty("content", out var contentElement))
                            {
                                lastAssistantMessage = ExtractTextContent(contentElement);
                            }

                            // Extract tool uses
                            ExtractToolUses(root, commands, ref toolCallCount);
                        }
                    }

                    // Alternative format: check for tool_use directly
                    if (root.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "tool_use")
                    {
                        ExtractCommandFromToolUse(root, commands, ref toolCallCount);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            result.ToolCallCount = toolCallCount;
            result.Commands = commands.Take(MaxCommands).ToList();
            result.Summary = ExtractSummary(lastAssistantMessage);
            result.ParsedSuccessfully = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private static string? ExtractTextContent(JsonElement contentElement)
    {
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString();
        }

        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in contentElement.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text")
                {
                    if (item.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            texts.Add(text);
                    }
                }
            }
            return texts.Count > 0 ? string.Join("\n", texts) : null;
        }

        return null;
    }

    private static void ExtractToolUses(JsonElement root, HashSet<string> commands, ref int toolCallCount)
    {
        if (!root.TryGetProperty("content", out var contentElement))
            return;

        if (contentElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tool_use")
            {
                ExtractCommandFromToolUse(item, commands, ref toolCallCount);
            }
        }
    }

    private static void ExtractCommandFromToolUse(JsonElement toolUse, HashSet<string> commands, ref int toolCallCount)
    {
        toolCallCount++;

        // Check if it's a Bash tool
        if (!toolUse.TryGetProperty("name", out var nameEl))
            return;

        var toolName = nameEl.GetString();
        if (toolName != "Bash")
            return;

        // Extract command from input
        if (!toolUse.TryGetProperty("input", out var inputEl))
            return;

        string? command = null;

        // Format 1: input.command
        if (inputEl.TryGetProperty("command", out var cmdEl))
        {
            command = cmdEl.GetString();
        }
        // Format 2: input is a string
        else if (inputEl.ValueKind == JsonValueKind.String)
        {
            command = inputEl.GetString();
        }

        if (!string.IsNullOrWhiteSpace(command) && commands.Count < MaxCommands)
        {
            // Clean up command (first line only, truncate if too long)
            var cleanCommand = command.Split('\n')[0].Trim();
            if (cleanCommand.Length > 100)
                cleanCommand = cleanCommand[..97] + "...";

            if (!string.IsNullOrEmpty(cleanCommand))
                commands.Add(cleanCommand);
        }
    }

    private static string? ExtractSummary(string? lastMessage)
    {
        if (string.IsNullOrWhiteSpace(lastMessage))
            return null;

        // Look for summary patterns
        var summaryPatterns = new[]
        {
            "Summary:",
            "I've completed",
            "The changes include",
            "I have completed",
            "Done!",
            "I've made the following changes",
            "Changes made:",
            "I've implemented",
            "I've added",
            "I've updated",
            "I've fixed"
        };

        foreach (var pattern in summaryPatterns)
        {
            var index = lastMessage.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var summary = lastMessage[index..];
                // Get first few lines
                var lines = summary.Split('\n').Take(5);
                return string.Join("\n", lines).Trim();
            }
        }

        // If no pattern found, just take the first part of the last message
        var firstPart = lastMessage.Split('\n').Take(3);
        var result = string.Join("\n", firstPart).Trim();

        // Truncate if too long
        if (result.Length > 500)
            result = result[..497] + "...";

        return result;
    }
}
