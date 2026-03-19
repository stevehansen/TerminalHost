// Copyright (c) TerminalHost. All rights reserved.
// Deserialization DTOs that mirror the TerminalHost REST API response shapes.
// Source of truth: src/TerminalHost.Core/Domain/ApiDtos.cs

using System.Text.Json.Serialization;

namespace TerminalHost.CmdPal.Helpers;

internal static class ApiModels
{
    // ── Status ──────────────────────────────────────────────

    internal sealed class StatusResponse
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("uptime")]
        public string Uptime { get; set; } = "";

        [JsonPropertyName("tabCount")]
        public int TabCount { get; set; }

        [JsonPropertyName("activeTabIndex")]
        public int ActiveTabIndex { get; set; } = -1;
    }

    // ── Repos ───────────────────────────────────────────────

    internal sealed class ReposResponse
    {
        [JsonPropertyName("repos")]
        public List<RepoInfo> Repos { get; set; } = new();
    }

    internal sealed class RepoInfo
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("workingDirectory")]
        public string WorkingDirectory { get; set; } = "";

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("git")]
        public GitInfo? Git { get; set; }

        [JsonPropertyName("terminals")]
        public TerminalsInfo? Terminals { get; set; }

        [JsonPropertyName("activityIndicator")]
        public ActivityIndicator? ActivityIndicator { get; set; }
    }

    // ── Git ─────────────────────────────────────────────────

    internal sealed class GitInfo
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

    internal sealed class GitDetailInfo
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

        [JsonPropertyName("files")]
        public List<GitFileInfo> Files { get; set; } = new();

        [JsonPropertyName("recentCommits")]
        public List<GitCommitInfo> RecentCommits { get; set; } = new();
    }

    internal sealed class GitFileInfo
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

    internal sealed class GitCommitInfo
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

    // ── Terminals ───────────────────────────────────────────

    internal sealed class TerminalInfo
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("isBusy")]
        public bool IsBusy { get; set; }

        [JsonPropertyName("lastActivityAt")]
        public DateTime? LastActivityAt { get; set; }
    }

    internal sealed class TerminalsInfo
    {
        [JsonPropertyName("custom")]
        public TerminalInfo? Custom { get; set; }

        [JsonPropertyName("shell")]
        public TerminalInfo? Shell { get; set; }

        [JsonPropertyName("run")]
        public TerminalInfo? Run { get; set; }
    }

    // ── Activity ────────────────────────────────────────────

    internal sealed class ActivityIndicator
    {
        [JsonPropertyName("state")]
        public string State { get; set; } = "idle";

        [JsonPropertyName("hasUnreadActivity")]
        public bool HasUnreadActivity { get; set; }

        [JsonPropertyName("isWaitingForInput")]
        public bool IsWaitingForInput { get; set; }
    }

    // ── Tasks ───────────────────────────────────────────────

    internal sealed class TasksResponse
    {
        [JsonPropertyName("tasks")]
        public List<TaskInfo> Tasks { get; set; } = new();
    }

    internal sealed class TaskInfo
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

        [JsonPropertyName("elapsedTime")]
        public string? ElapsedTime { get; set; }

        [JsonPropertyName("repoIndex")]
        public int? RepoIndex { get; set; }

        [JsonPropertyName("projectPaths")]
        public List<string> ProjectPaths { get; set; } = new();

        [JsonPropertyName("linkedBranch")]
        public string? LinkedBranch { get; set; }

        [JsonPropertyName("claude")]
        public ClaudeTaskInfo? Claude { get; set; }
    }

    internal sealed class ClaudeTaskInfo
    {
        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("claudeTaskId")]
        public string? ClaudeTaskId { get; set; }

        [JsonPropertyName("activeForm")]
        public string? ActiveForm { get; set; }
    }

    // ── Timeline ────────────────────────────────────────────

    internal sealed class TimelineResponse
    {
        [JsonPropertyName("intents")]
        public List<TimelineIntentInfo> Intents { get; set; } = new();
    }

    internal sealed class TimelineIntentInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("branchName")]
        public string? BranchName { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("sessions")]
        public List<TimelineSessionInfo> Sessions { get; set; } = new();
    }

    internal sealed class TimelineSessionInfo
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
}
