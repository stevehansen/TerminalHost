using System.Text.Json;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Services;

/// <summary>
/// Parses Claude Code transcript JSONL files to extract commands, summaries,
/// and rich activity data (tool calls, messages, agents).
/// </summary>
public class TranscriptParserService
{
    /// <summary>
    /// Result of parsing a transcript file (legacy, lightweight).
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
    /// Rich result from parsing a transcript file with full activity data.
    /// </summary>
    public record RichTranscriptParseResult
    {
        public List<ActivityEvent> Events { get; init; } = [];
        public List<string> Commands { get; init; } = [];
        public string? Summary { get; init; }
        public string? Model { get; init; }
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
            transcriptPath = ExpandPath(transcriptPath);

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
    /// Parses a Claude Code transcript file with rich activity data extraction.
    /// Extracts all content block types: text, thinking, tool_use, tool_result.
    /// </summary>
    public async Task<RichTranscriptParseResult> ParseTranscriptRichAsync(string transcriptPath, string sessionId, HashSet<string>? seenMessageIds = null, HashSet<string>? seenToolUseIds = null)
    {
        try
        {
            transcriptPath = ExpandPath(transcriptPath);
            if (!File.Exists(transcriptPath))
            {
                return new RichTranscriptParseResult
                {
                    ParsedSuccessfully = false,
                    Error = "Transcript file not found"
                };
            }

            var lines = new List<string>();
            await foreach (var line in ReadLinesAsync(transcriptPath))
            {
                lines.Add(line);
            }

            return ParseLines(lines, sessionId, seenMessageIds, seenToolUseIds);
        }
        catch (Exception ex)
        {
            return new RichTranscriptParseResult
            {
                ParsedSuccessfully = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Parses individual JSONL lines into rich activity data.
    /// Used by both full-file parsing and incremental (watcher) parsing.
    /// </summary>
    public RichTranscriptParseResult ParseLines(IEnumerable<string> lines, string sessionId, HashSet<string>? seenMessageIds = null, HashSet<string>? seenToolUseIds = null)
    {
        var events = new List<ActivityEvent>();
        var commands = new List<string>();
        string? lastAssistantMessage = null;
        string? detectedModel = null;
        int messageCount = 0;
        int toolCallCount = 0;
        seenMessageIds ??= [];
        seenToolUseIds ??= [];

        try
        {
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // Extract UUID for deduplication
                    var uuid = TryGetString(root, "uuid")
                        ?? TryGetNestedString(root, "entry", "uuid");
                    if (uuid != null && !seenMessageIds.Add(uuid))
                        continue; // Already processed

                    // Extract timestamp from JSONL line (used to stamp all events from this line)
                    var timestamp = TryGetTimestamp(root) ?? DateTime.UtcNow;
                    var eventsBeforeLine = events.Count;

                    // Extract model info
                    var model = TryGetNestedString(root, "entry", "message", "model")
                        ?? TryGetNestedString(root, "message", "model");
                    if (model != null && detectedModel == null)
                    {
                        detectedModel = model;
                        events.Add(ActivityEvent.CreateModelDetected(sessionId, sessionId, model, EventSource.Transcript));
                    }

                    // Determine the message type and extract content blocks
                    var type = TryGetString(root, "type");
                    var role = TryGetString(root, "role");

                    // Handle nested message format: entry.message or root.message
                    // Claude Code JSONL uses { type: "assistant", message: { role, content: [...] } }
                    var messageElement = TryGetNestedElement(root, "entry", "message");
                    if (!messageElement.HasValue && (type == "user" || type == "assistant"))
                        messageElement = TryGetNestedElement(root, "message");
                    if (messageElement.HasValue)
                    {
                        role ??= TryGetString(messageElement.Value, "role");
                        ProcessContentBlocks(messageElement.Value, sessionId, role, timestamp, uuid, events, commands, seenToolUseIds, ref messageCount, ref toolCallCount, ref lastAssistantMessage);
                        continue;
                    }

                    // Handle flat format messages
                    if (type == "user" || role == "user")
                    {
                        messageCount++;
                        var text = TryExtractText(root);
                        if (text != null)
                        {
                            events.Add(ActivityEvent.CreateUserMessage(
                                sessionId, text, TokenEstimator.Estimate(text), EventSource.Transcript));
                        }
                    }
                    else if (type == "assistant" || role == "assistant")
                    {
                        messageCount++;
                        ProcessContentBlocks(root, sessionId, "assistant", timestamp, uuid, events, commands, seenToolUseIds, ref messageCount, ref toolCallCount, ref lastAssistantMessage);
                    }
                    else if (type == "tool_use")
                    {
                        toolCallCount++;
                        ProcessToolUseBlock(root, sessionId, timestamp, events, commands, seenToolUseIds);
                    }
                    else if (type == "tool_result")
                    {
                        ProcessToolResultBlock(root, sessionId, timestamp, events, seenToolUseIds, ref toolCallCount);
                    }

                    // Handle flat tool_name format
                    if (root.TryGetProperty("tool_name", out var toolNameEl))
                    {
                        var toolName = toolNameEl.GetString();
                        if (toolName == "Bash")
                        {
                            var command = TryExtractToolInputCommand(root);
                            if (!string.IsNullOrEmpty(command))
                                commands.Add(command);
                        }
                    }

                    // Stamp all events produced from this JSONL line with the line's actual timestamp
                    // (factory methods default to DateTime.UtcNow which loses original timing)
                    for (int e = eventsBeforeLine; e < events.Count; e++)
                    {
                        events[e].Timestamp = timestamp;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            string? summary = null;
            if (!string.IsNullOrEmpty(lastAssistantMessage))
                summary = ExtractSummary(lastAssistantMessage);

            return new RichTranscriptParseResult
            {
                Events = events,
                Commands = commands.Distinct().Take(50).ToList(),
                Summary = summary,
                Model = detectedModel,
                MessageCount = messageCount,
                ToolCallCount = toolCallCount,
                ParsedSuccessfully = true
            };
        }
        catch (Exception ex)
        {
            return new RichTranscriptParseResult
            {
                ParsedSuccessfully = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Processes content blocks within a message element (handles content array with text, thinking, tool_use, tool_result).
    /// </summary>
    private void ProcessContentBlocks(JsonElement messageElement, string sessionId, string? role, DateTime timestamp, string? uuid,
        List<ActivityEvent> events, List<string> commands, HashSet<string> seenToolUseIds,
        ref int messageCount, ref int toolCallCount, ref string? lastAssistantMessage)
    {
        if (!messageElement.TryGetProperty("content", out var contentElement))
            return;

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            var text = contentElement.GetString();
            if (role == "assistant" && text != null)
            {
                lastAssistantMessage = text;
                events.Add(ActivityEvent.CreateAssistantMessage(
                    sessionId, sessionId, text, TokenEstimator.Estimate(text), EventSource.Transcript));
            }
            else if (role == "user" && text != null)
            {
                events.Add(ActivityEvent.CreateUserMessage(
                    sessionId, text, TokenEstimator.Estimate(text), EventSource.Transcript));
            }
            return;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var block in contentElement.EnumerateArray())
        {
            var blockType = TryGetString(block, "type");
            switch (blockType)
            {
                case "text":
                {
                    var text = TryGetString(block, "text");
                    if (text != null && role == "assistant")
                    {
                        lastAssistantMessage = text;
                        events.Add(ActivityEvent.CreateAssistantMessage(
                            sessionId, sessionId, text, TokenEstimator.Estimate(text), EventSource.Transcript));
                    }
                    break;
                }
                case "thinking":
                {
                    var thinking = TryGetString(block, "thinking") ?? TryGetString(block, "text");
                    if (thinking != null)
                    {
                        events.Add(ActivityEvent.CreateThinkingBlock(
                            sessionId, sessionId, thinking, TokenEstimator.Estimate(thinking), EventSource.Transcript));
                    }
                    break;
                }
                case "tool_use":
                {
                    toolCallCount++;
                    ProcessToolUseBlock(block, sessionId, timestamp, events, commands, seenToolUseIds);
                    break;
                }
                case "tool_result":
                {
                    ProcessToolResultBlock(block, sessionId, timestamp, events, seenToolUseIds, ref toolCallCount);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Processes a tool_use content block.
    /// </summary>
    private void ProcessToolUseBlock(JsonElement block, string sessionId, DateTime timestamp,
        List<ActivityEvent> events, List<string> commands, HashSet<string> seenToolUseIds)
    {
        var toolUseId = TryGetString(block, "id") ?? TryGetString(block, "tool_use_id") ?? Guid.NewGuid().ToString();
        var toolName = TryGetString(block, "name") ?? TryGetString(block, "tool_name") ?? "unknown";

        if (!seenToolUseIds.Add(toolUseId))
            return; // Already tracked via hook

        // Build input summary
        string? inputSummary = null;
        string? filePath = null;
        string? command = null;
        string? description = null;

        var input = TryGetNestedElement(block, "input");
        if (input.HasValue)
        {
            filePath = TryGetString(input.Value, "file_path");
            command = TryGetString(input.Value, "command");
            description = TryGetString(input.Value, "description")
                ?? TryGetString(input.Value, "prompt")
                ?? TryGetString(input.Value, "query")
                ?? TryGetString(input.Value, "pattern")
                ?? TryGetString(input.Value, "url");

            if (toolName == "Bash" && !string.IsNullOrEmpty(command))
                commands.Add(command);
        }

        inputSummary = ToolCall.SummarizeInput(toolName, filePath, command, description);

        // Detect subagent spawn
        if (toolName is "Agent" or "Task")
        {
            var name = description != null && description.Length > 30 ? description[..30] : description;
            events.Add(ActivityEvent.CreateAgentSpawn(
                sessionId, toolUseId, sessionId, name ?? "subagent", false, description, null, EventSource.Transcript));
        }

        events.Add(ActivityEvent.CreateToolCallStart(
            sessionId, sessionId, toolUseId, toolName, inputSummary, EventSource.Transcript));

        // Track file access
        if (!string.IsNullOrEmpty(filePath))
        {
            var accessType = ToolCall.CategorizeTool(toolName) == ToolCategory.FileWrite ? "write" : "read";
            events.Add(ActivityEvent.CreateFileAccessed(
                sessionId, sessionId, filePath, accessType, toolUseId, EventSource.Transcript));
        }
    }

    /// <summary>
    /// Processes a tool_result content block.
    /// </summary>
    private void ProcessToolResultBlock(JsonElement block, string sessionId, DateTime timestamp,
        List<ActivityEvent> events, HashSet<string> seenToolUseIds, ref int toolCallCount)
    {
        var toolUseId = TryGetString(block, "tool_use_id") ?? TryGetString(block, "id");
        if (toolUseId == null)
            return;

        // Get result content for token estimation
        string? resultContent = null;
        if (block.TryGetProperty("content", out var contentEl))
        {
            resultContent = contentEl.ValueKind == JsonValueKind.String
                ? contentEl.GetString()
                : contentEl.GetRawText();
        }
        else if (block.TryGetProperty("output", out var outputEl))
        {
            resultContent = outputEl.ValueKind == JsonValueKind.String
                ? outputEl.GetString()
                : outputEl.GetRawText();
        }

        var isError = TryGetString(block, "is_error") == "true"
            || (block.TryGetProperty("is_error", out var errEl) && errEl.ValueKind == JsonValueKind.True);
        var tokenCost = TokenEstimator.Estimate(resultContent);

        events.Add(ActivityEvent.CreateToolCallEnd(
            sessionId, sessionId, toolUseId, "", // tool name not in result block
            resultContent != null && resultContent.Length > 100 ? resultContent[..100] + "..." : resultContent,
            tokenCost,
            isError ? resultContent : null,
            EventSource.Transcript));

        // Detect subagent completion
        // (We can't know for sure here, but if we spawned a subagent with this ID, it completes when its result arrives)
    }

    // JSON helper methods

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string? TryGetNestedString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (!current.TryGetProperty(key, out var next))
                return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static JsonElement? TryGetNestedElement(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (!current.TryGetProperty(key, out var next))
                return null;
            current = next;
        }
        return current;
    }

    private static DateTime? TryGetTimestamp(JsonElement element)
    {
        var timeStr = TryGetString(element, "time")
            ?? TryGetString(element, "timestamp")
            ?? TryGetNestedString(element, "entry", "timestamp");

        if (timeStr != null && DateTime.TryParse(timeStr, out var dt))
            return dt.ToUniversalTime();

        return null;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }
        return path;
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
