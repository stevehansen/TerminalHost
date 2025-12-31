using System.Text.Json;

namespace TerminalHost.Core.Services;

/// <summary>
/// Parses Claude Code transcript JSONL files to extract commands and summaries.
/// </summary>
public class TranscriptParserService
{
    /// <summary>
    /// Result of parsing a transcript file.
    /// </summary>
    public record TranscriptParseResult
    {
        public List<string> Commands { get; init; } = [];
        public string? Summary { get; init; }
        public int MessageCount { get; init; }
        public int ToolCallCount { get; init; }
        public bool ParsedSuccessfully { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Parses a Claude Code transcript file.
    /// </summary>
    public async Task<TranscriptParseResult> ParseTranscriptAsync(string transcriptPath)
    {
        var commands = new List<string>();
        string? lastAssistantMessage = null;
        int messageCount = 0;
        int toolCallCount = 0;

        try
        {
            // Expand ~ to user home directory if present
            if (transcriptPath.StartsWith("~/") || transcriptPath.StartsWith("~\\"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                transcriptPath = Path.Combine(home, transcriptPath[2..]);
            }

            if (!File.Exists(transcriptPath))
            {
                return new TranscriptParseResult
                {
                    ParsedSuccessfully = false,
                    Error = "Transcript file not found"
                };
            }

            // Read and parse each line as JSON
            await foreach (var line in ReadLinesAsync(transcriptPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // Check for message type
                    if (root.TryGetProperty("type", out var typeElement))
                    {
                        var type = typeElement.GetString();

                        // Handle tool_use messages
                        if (type == "tool_use" || type == "tool_result")
                        {
                            toolCallCount++;
                            TryExtractBashCommand(root, commands);
                        }
                        // Handle assistant messages
                        else if (type == "assistant")
                        {
                            messageCount++;
                            lastAssistantMessage = TryExtractText(root);
                        }
                    }
                    // Also check for role-based format
                    else if (root.TryGetProperty("role", out var roleElement))
                    {
                        var role = roleElement.GetString();
                        if (role == "assistant")
                        {
                            messageCount++;
                            lastAssistantMessage = TryExtractText(root);
                        }
                    }

                    // Check for tool_name in the root (flat format)
                    if (root.TryGetProperty("tool_name", out var toolNameElement))
                    {
                        var toolName = toolNameElement.GetString();
                        if (toolName == "Bash")
                        {
                            toolCallCount++;
                            var command = TryExtractToolInputCommand(root);
                            if (!string.IsNullOrEmpty(command))
                                commands.Add(command);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            // Extract a summary from the last assistant message
            string? summary = null;
            if (!string.IsNullOrEmpty(lastAssistantMessage))
            {
                summary = ExtractSummary(lastAssistantMessage);
            }

            return new TranscriptParseResult
            {
                Commands = commands.Distinct().Take(20).ToList(), // Limit to 20 unique commands
                Summary = summary,
                MessageCount = messageCount,
                ToolCallCount = toolCallCount,
                ParsedSuccessfully = true
            };
        }
        catch (Exception ex)
        {
            return new TranscriptParseResult
            {
                ParsedSuccessfully = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Attempts to extract a Bash command from a transcript entry.
    /// </summary>
    private void TryExtractBashCommand(JsonElement root, List<string> commands)
    {
        // Check for tool_use with Bash
        if (root.TryGetProperty("name", out var nameElement) &&
            nameElement.GetString() == "Bash")
        {
            var command = TryExtractToolInputCommand(root);
            if (!string.IsNullOrEmpty(command))
                commands.Add(command);
        }

        // Check for content array with tool_use
        if (root.TryGetProperty("content", out var contentElement) &&
            contentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentElement.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var itemType) &&
                    itemType.GetString() == "tool_use" &&
                    item.TryGetProperty("name", out var itemName) &&
                    itemName.GetString() == "Bash")
                {
                    if (item.TryGetProperty("input", out var inputElement))
                    {
                        if (inputElement.TryGetProperty("command", out var cmdElement))
                        {
                            var cmd = cmdElement.GetString();
                            if (!string.IsNullOrEmpty(cmd))
                                commands.Add(cmd);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extracts the command from tool_input.
    /// </summary>
    private string? TryExtractToolInputCommand(JsonElement root)
    {
        // Try tool_input.command
        if (root.TryGetProperty("tool_input", out var toolInput) &&
            toolInput.TryGetProperty("command", out var cmdElement))
        {
            return cmdElement.GetString();
        }

        // Try input.command
        if (root.TryGetProperty("input", out var input) &&
            input.TryGetProperty("command", out var cmdElement2))
        {
            return cmdElement2.GetString();
        }

        return null;
    }

    /// <summary>
    /// Extracts text content from a message.
    /// </summary>
    private string? TryExtractText(JsonElement root)
    {
        // Try content array with text items
        if (root.TryGetProperty("content", out var contentElement))
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
                    if (item.TryGetProperty("type", out var typeEl) &&
                        typeEl.GetString() == "text" &&
                        item.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            texts.Add(text);
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrEmpty(text))
                            texts.Add(text);
                    }
                }
                return texts.Count > 0 ? string.Join("\n", texts) : null;
            }
        }

        // Try direct text property
        if (root.TryGetProperty("text", out var textElement))
        {
            return textElement.GetString();
        }

        return null;
    }

    /// <summary>
    /// Extracts a summary from the last assistant message.
    /// Looks for patterns like "Summary:", "I've completed", "The changes include", etc.
    /// </summary>
    private string? ExtractSummary(string text)
    {
        // Limit the text to a reasonable length
        if (text.Length > 5000)
            text = text[..5000];

        // Split into paragraphs
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Look for summary patterns
        var summaryPatterns = new[]
        {
            "summary:", "in summary:", "to summarize:",
            "i've completed", "i've finished", "i've implemented",
            "the changes include", "the key changes",
            "this commit", "this pr", "this implementation"
        };

        foreach (var paragraph in paragraphs)
        {
            var lower = paragraph.ToLowerInvariant();
            if (summaryPatterns.Any(p => lower.Contains(p)))
            {
                return TruncateSummary(paragraph.Trim());
            }
        }

        // If no summary pattern found, use the first paragraph if it's reasonable length
        var firstPara = paragraphs.FirstOrDefault();
        if (!string.IsNullOrEmpty(firstPara) && firstPara.Length >= 20 && firstPara.Length <= 500)
        {
            return TruncateSummary(firstPara.Trim());
        }

        // Otherwise, try to find a reasonable first sentence
        var sentences = text.Split(new[] { ". ", ".\n", ".\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var firstSentence = sentences.FirstOrDefault()?.Trim();
        if (!string.IsNullOrEmpty(firstSentence) && firstSentence.Length >= 10 && firstSentence.Length <= 300)
        {
            return firstSentence + (firstSentence.EndsWith('.') ? "" : ".");
        }

        return null;
    }

    /// <summary>
    /// Truncates a summary to a reasonable length.
    /// </summary>
    private string TruncateSummary(string summary)
    {
        const int maxLength = 200;
        if (summary.Length <= maxLength)
            return summary;

        // Find a good break point
        var breakPoint = summary.LastIndexOf(' ', maxLength);
        if (breakPoint < maxLength / 2)
            breakPoint = maxLength;

        return summary[..breakPoint].TrimEnd() + "...";
    }

    /// <summary>
    /// Asynchronously reads lines from a file.
    /// Opens with FileShare.ReadWrite to avoid locking conflicts with Claude Code
    /// which may still be writing to the transcript file.
    /// </summary>
    private async IAsyncEnumerable<string> ReadLinesAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line != null)
                yield return line;
        }
    }
}
