using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// DTO for a repository tab in API responses.
/// </summary>
public class ApiRepoInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("layout")]
    public string Layout { get; set; } = "";

    [JsonPropertyName("splitRatio")]
    public double SplitRatio { get; set; }

    [JsonPropertyName("activeTerminal")]
    public string ActiveTerminal { get; set; } = "";

    [JsonPropertyName("git")]
    public ApiGitInfo? Git { get; set; }

    [JsonPropertyName("terminals")]
    public ApiTerminalsInfo? Terminals { get; set; }

    [JsonPropertyName("activityIndicator")]
    public ApiActivityIndicator? ActivityIndicator { get; set; }
}

/// <summary>
/// DTO for detailed repo info (single repo endpoint).
/// </summary>
public class ApiRepoDetailInfo : ApiRepoInfo
{
    [JsonPropertyName("runConfiguration")]
    public ApiRunConfigInfo? RunConfiguration { get; set; }

    [JsonPropertyName("aiAssistant")]
    public ApiAiAssistantInfo? AiAssistant { get; set; }
}

/// <summary>
/// DTO for git status in API responses.
/// </summary>
public class ApiGitInfo
{
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("isDirty")]
    public bool IsDirty { get; set; }

    [JsonPropertyName("ahead")]
    public int Ahead { get; set; }

    [JsonPropertyName("behind")]
    public int Behind { get; set; }

    [JsonPropertyName("stashCount")]
    public int StashCount { get; set; }

    [JsonPropertyName("changedFiles")]
    public int ChangedFiles { get; set; }

    [JsonPropertyName("stagedFiles")]
    public int StagedFiles { get; set; }

    [JsonPropertyName("untrackedFiles")]
    public int UntrackedFiles { get; set; }
}

/// <summary>
/// DTO for detailed git status with file list.
/// </summary>
public class ApiGitDetailInfo : ApiGitInfo
{
    [JsonPropertyName("files")]
    public List<ApiGitFileInfo> Files { get; set; } = new();

    [JsonPropertyName("recentCommits")]
    public List<ApiGitCommitInfo> RecentCommits { get; set; } = new();
}

/// <summary>
/// DTO for a git file in API responses.
/// </summary>
public class ApiGitFileInfo
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("isStaged")]
    public bool IsStaged { get; set; }

    [JsonPropertyName("oldPath")]
    public string? OldPath { get; set; }
}

/// <summary>
/// DTO for a git commit in API responses.
/// </summary>
public class ApiGitCommitInfo
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}

/// <summary>
/// DTO for terminal info in API responses.
/// </summary>
public class ApiTerminalInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Whether this terminal pane is the focused/selected pane.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    /// <summary>Whether the terminal is actively generating output (output within last 2 seconds).</summary>
    [JsonPropertyName("isBusy")]
    public bool IsBusy { get; set; }

    /// <summary>The last time the terminal produced output (UTC). Null if no output has been received.</summary>
    [JsonPropertyName("lastActivityAt")]
    public DateTime? LastActivityAt { get; set; }
}

/// <summary>
/// DTO for the tab-level activity indicator state in API responses.
/// Maps to the visual indicators shown on the tab strip:
/// spinning orange = actively outputting, green dot = completed (unread),
/// orange dot = waiting for user input.
/// </summary>
public class ApiActivityIndicator
{
    /// <summary>
    /// The current indicator state.
    /// "busy" — terminal actively generating output (spinning orange indicator).
    /// "waiting" — terminal idle, detected input prompt waiting for user (orange dot indicator).
    /// "done" — activity finished but tab not yet viewed by user (green dot indicator).
    /// "idle" — no notable activity state.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = "idle";

    /// <summary>Whether any terminal in this tab has unacknowledged completed activity.</summary>
    [JsonPropertyName("hasUnreadActivity")]
    public bool HasUnreadActivity { get; set; }

    /// <summary>Whether the custom terminal is detected as waiting for user input.</summary>
    [JsonPropertyName("isWaitingForInput")]
    public bool IsWaitingForInput { get; set; }
}

/// <summary>
/// DTO for terminals group in API responses.
/// </summary>
public class ApiTerminalsInfo
{
    [JsonPropertyName("custom")]
    public ApiTerminalInfo? Custom { get; set; }

    [JsonPropertyName("shell")]
    public ApiTerminalInfo? Shell { get; set; }

    [JsonPropertyName("run")]
    public ApiTerminalInfo? Run { get; set; }
}

/// <summary>
/// DTO for a detected link in API responses.
/// </summary>
public class ApiLinkInfo
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

/// <summary>
/// DTO for a focus task in API responses.
/// Merges tasks from ITaskService, IClaudeTaskDetectionService, and IClaudeTaskFileService.
/// </summary>
public class ApiTaskInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("elapsedTime")]
    public string? ElapsedTime { get; set; }

    [JsonPropertyName("repoIndex")]
    public int? RepoIndex { get; set; }

    [JsonPropertyName("projectPaths")]
    public List<string> ProjectPaths { get; set; } = new();

    [JsonPropertyName("parentTaskId")]
    public string? ParentTaskId { get; set; }

    [JsonPropertyName("blocks")]
    public List<string> Blocks { get; set; } = new();

    [JsonPropertyName("blockedBy")]
    public List<string> BlockedBy { get; set; } = new();

    [JsonPropertyName("isBlocked")]
    public bool IsBlocked { get; set; }

    [JsonPropertyName("linkedBranch")]
    public string? LinkedBranch { get; set; }

    [JsonPropertyName("linkedPrNumber")]
    public string? LinkedPrNumber { get; set; }

    [JsonPropertyName("linkedPrUrl")]
    public string? LinkedPrUrl { get; set; }

    [JsonPropertyName("claude")]
    public ApiClaudeTaskInfo? Claude { get; set; }
}

/// <summary>
/// Claude-specific metadata for a task.
/// Present only when the task originated from Claude Code.
/// </summary>
public class ApiClaudeTaskInfo
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("claudeTaskId")]
    public string? ClaudeTaskId { get; set; }

    [JsonPropertyName("activeForm")]
    public string? ActiveForm { get; set; }
}

/// <summary>
/// DTO for a workspace/playground in API responses.
/// </summary>
public class ApiWorkspaceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("pathId")]
    public string PathId { get; set; } = "";

    [JsonPropertyName("section")]
    public string Section { get; set; } = "main";

    [JsonPropertyName("isPinned")]
    public bool IsPinned { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("customIcon")]
    public string? CustomIcon { get; set; }

    [JsonPropertyName("isOpen")]
    public bool IsOpen { get; set; }

    [JsonPropertyName("repoIndex")]
    public int? RepoIndex { get; set; }

    [JsonPropertyName("activityIndicator")]
    public ApiActivityIndicator? ActivityIndicator { get; set; }

    [JsonPropertyName("terminals")]
    public ApiTerminalsInfo? Terminals { get; set; }
}

/// <summary>
/// DTO for a timeline intent in API responses.
/// </summary>
public class ApiTimelineIntentInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("branchName")]
    public string? BranchName { get; set; }

    [JsonPropertyName("repoPath")]
    public string? RepoPath { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("sessions")]
    public List<ApiTimelineSessionInfo> Sessions { get; set; } = new();
}

/// <summary>
/// DTO for a timeline session in API responses.
/// </summary>
public class ApiTimelineSessionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("endedAt")]
    public DateTime? EndedAt { get; set; }

    [JsonPropertyName("commitHash")]
    public string? CommitHash { get; set; }

    [JsonPropertyName("commitMessage")]
    public string? CommitMessage { get; set; }
}

/// <summary>
/// DTO for run configuration in API responses.
/// </summary>
public class ApiRunConfigInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; set; }
}

/// <summary>
/// DTO for AI assistant info in API responses.
/// </summary>
public class ApiAiAssistantInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";
}

/// <summary>
/// Standard error response for the API.
/// </summary>
public class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public ApiErrorDetail Error { get; set; } = new();
}

/// <summary>
/// Error detail within an API error response.
/// </summary>
public class ApiErrorDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
