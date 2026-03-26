using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

/// <summary>
/// Stub implementation of SessionActivityService for Avalonia/macOS.
/// </summary>
public class SessionActivityService : ISessionActivityService
{
    public event EventHandler<ActivityEvent>? ActivityEventProcessed;
    public event EventHandler<(string SessionId, SessionLifecycle NewState)>? LifecycleChanged;

    public SessionActivityState? GetState(string sessionId) => null;
    public IReadOnlyList<SessionActivityState> GetActiveStates() => [];
    public IReadOnlyList<SessionActivityState> GetAllStates() => [];

    public SessionActivityState GetOrCreateState(string sessionId, string? cwd = null, string? transcriptPath = null)
        => SessionActivityState.Create(sessionId, cwd, transcriptPath);

    public void RemoveState(string sessionId) { }
    public void ProcessHookEvent(HookEvent hookEvent, HookEventData? rawData = null) { }
    public Task EnrichFromTranscriptAsync(string sessionId) => Task.CompletedTask;
    public void ProcessTranscriptEvents(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null) { }

    public (int Total, int FileReads, int FileWrites, int ShellCommands, int Subagents) GetToolCallStats(string sessionId)
        => (0, 0, 0, 0, 0);

    public IReadOnlyList<FileActivity> GetTopFiles(string sessionId, int count = 10) => [];
}
