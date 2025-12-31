using System;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Portable information about a terminal session.
/// Platform-specific session implementations can extend this.
/// </summary>
public class TerminalSessionInfo
{
    /// <summary>Unique identifier for this session.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The profile used to create this session.</summary>
    public required Profile Profile { get; init; }

    /// <summary>Working directory for this session.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Current state of the session.</summary>
    public SessionState State { get; set; } = SessionState.Running;

    /// <summary>Exit code if the session has exited.</summary>
    public int? ExitCode { get; set; }

    /// <summary>When the session was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>When the session exited (if applicable).</summary>
    public DateTime? ExitedAt { get; set; }

    /// <summary>Display name for the session.</summary>
    public string DisplayName => Profile.Name;

    /// <summary>Whether the session is still running.</summary>
    public bool IsRunning => State == SessionState.Running;
}
