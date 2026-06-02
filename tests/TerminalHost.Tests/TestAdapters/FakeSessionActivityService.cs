using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// Minimal in-memory <see cref="ISessionActivityService"/> for orchestrator tests.
/// Most methods are unused stubs — the only contract exercised is
/// <see cref="ActivityEventProcessed"/> and <c>EnrichFromTranscriptAsync</c>.
/// </summary>
public sealed class FakeSessionActivityService : ISessionActivityService
{
    public int EnrichCallCount { get; private set; }

    public event EventHandler<ActivityEvent>? ActivityEventProcessed;
    public event EventHandler<(string SessionId, SessionLifecycle NewState)>? LifecycleChanged { add { } remove { } }

    /// <summary>Synchronously raises <see cref="ActivityEventProcessed"/>. Mimics the real service's UI-thread post.</summary>
    public void RaiseActivityEvent(ActivityEvent evt)
    {
        ActivityEventProcessed?.Invoke(this, evt);
    }

    // Stubs — orchestrator doesn't call these in tests.
    public SessionActivityState? GetState(string sessionId) => null;
    public IReadOnlyList<SessionActivityState> GetActiveStates() => Array.Empty<SessionActivityState>();
    public IReadOnlyList<SessionActivityState> GetAllStates() => Array.Empty<SessionActivityState>();
    public SessionActivityState GetOrCreateState(string sessionId, string? cwd = null, string? transcriptPath = null,
        SessionSource source = SessionSource.Local, string? containerName = null) =>
        SessionActivityState.Create(sessionId);
    public void RemoveState(string sessionId) { }
    public bool RecordTerminalTitleActivity(string workingDirectory, string title, DateTime timestampUtc) => false;
    public void ProcessHookEvent(HookEvent hookEvent, HookEventData? rawData = null) { }
    public Task EnrichFromTranscriptAsync(string sessionId)
    {
        EnrichCallCount++;
        return Task.CompletedTask;
    }
    public void ProcessTranscriptEvents(string sessionId, IReadOnlyList<ActivityEvent> events, string? summary = null, string? model = null) { }
    public bool MarkLifecycle(string sessionId, SessionLifecycle newLifecycle) => false;
    public (int Total, int FileReads, int FileWrites, int ShellCommands, int Subagents) GetToolCallStats(string sessionId) => (0, 0, 0, 0, 0);
    public IReadOnlyList<FileActivity> GetTopFiles(string sessionId, int count = 10) => Array.Empty<FileActivity>();
}
