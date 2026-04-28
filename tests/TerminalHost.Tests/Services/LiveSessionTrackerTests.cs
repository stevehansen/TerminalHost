using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class LiveSessionTrackerTests
{
    private readonly Mock<ISessionStateStore> _store = new();
    private readonly Mock<ITranscriptWatcher> _watcher = new();
    private readonly Mock<ISessionActivityService> _activity = new();
    private readonly Mock<IClaudeSessionIndexService> _index = new();

    private LiveSessionTracker BuildTracker(bool withWatcher = false, bool withActivity = false, bool withIndex = false)
    {
        // Default: state store EnsureIntentForWorkingDirectory returns a fresh intent.
        _store.Setup(s => s.EnsureIntentForWorkingDirectory(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((cwd, name) => Intent.Create(name, "main", cwd));

        return new LiveSessionTracker(
            _store.Object,
            sessionIndexService: withIndex ? _index.Object : null,
            transcriptWatcher: withWatcher ? _watcher.Object : null,
            activityService: withActivity ? _activity.Object : null,
            collabService: null);
    }

    private static HookEvent SessionStart(string sessionId, string cwd, string? transcriptPath = null)
        => new()
        {
            SessionId = sessionId,
            Cwd = cwd,
            TranscriptPath = transcriptPath,
            EventType = HookEventType.SessionStart,
            Timestamp = DateTime.UtcNow,
            Source = SessionSource.Local,
        };

    [Fact]
    public void HandleSessionStart_RegistersLiveSession_AndFiresChange()
    {
        var tracker = BuildTracker();
        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;

        var ev = SessionStart("sess-1", Path.GetTempPath());
        tracker.HandleSessionStart(ev);

        var live = tracker.GetLiveSessionByClaudeId("sess-1");
        live.ShouldNotBeNull();
        live!.WorkingDirectory.ShouldBe(ev.Cwd);
        fired.ShouldBe(1);
        _store.Verify(s => s.EnsureIntentForWorkingDirectory(ev.Cwd, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void HandleSessionStart_IsIdempotent_DoesNotDoubleRegister()
    {
        var tracker = BuildTracker();
        var ev = SessionStart("sess-1", Path.GetTempPath());

        tracker.HandleSessionStart(ev);
        tracker.HandleSessionStart(ev);

        tracker.GetLiveSessions().Count.ShouldBe(1);
    }

    [Fact]
    public void HandleSessionStart_IgnoresEvent_WhenSessionIdMissing()
    {
        var tracker = BuildTracker();
        tracker.HandleSessionStart(new HookEvent { Cwd = Path.GetTempPath() });
        tracker.GetLiveSessions().ShouldBeEmpty();
    }

    [Fact]
    public void HandleSessionStart_IgnoresEvent_WhenCwdMissing()
    {
        var tracker = BuildTracker();
        tracker.HandleSessionStart(new HookEvent { SessionId = "sess-1" });
        tracker.GetLiveSessions().ShouldBeEmpty();
    }

    [Fact]
    public void HandleToolStart_AutoRegistersSession_WhenStartHookMissed()
    {
        var tracker = BuildTracker();
        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;

        var ev = new HookEvent
        {
            SessionId = "sess-1",
            Cwd = Path.GetTempPath(),
            EventType = HookEventType.ToolStart,
            Timestamp = DateTime.UtcNow,
        };
        tracker.HandleToolStart(ev);

        tracker.GetLiveSessionByClaudeId("sess-1").ShouldNotBeNull();
        fired.ShouldBe(1); // From the auto-created session start
    }

    [Fact]
    public void HandleToolStart_HydratesFromIndex_WhenAvailable()
    {
        var tracker = BuildTracker(withIndex: true);
        var indexCreated = DateTime.UtcNow.AddMinutes(-30);
        _index.Setup(i => i.GetSessionById("sess-1")).Returns(new ClaudeSessionIndexEntry
        {
            SessionId = "sess-1",
            ProjectPath = Path.GetTempPath(),
            FullPath = "/tmp/abc.jsonl",
            Created = indexCreated,
        });

        tracker.HandleToolStart(new HookEvent
        {
            SessionId = "sess-1",
            EventType = HookEventType.ToolStart,
            Timestamp = DateTime.UtcNow,
        });

        var live = tracker.GetLiveSessionByClaudeId("sess-1");
        live.ShouldNotBeNull();
        live!.StartTime.ShouldBe(indexCreated);
    }

    [Fact]
    public async Task HandleSessionStopAsync_MarksSessionEnded_AndUnwatches()
    {
        var tracker = BuildTracker(withWatcher: true);
        tracker.HandleSessionStart(SessionStart("sess-1", Path.GetTempPath()));

        await tracker.HandleSessionStopAsync(new HookEvent
        {
            SessionId = "sess-1",
            EventType = HookEventType.SessionStop,
            Timestamp = DateTime.UtcNow,
        });

        var live = tracker.GetLiveSessionByClaudeId("sess-1")!;
        live.IsActive.ShouldBeFalse();
        live.EndReason.ShouldBe("explicit");
        _watcher.Verify(w => w.Unwatch("sess-1"), Times.Once);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTranscriptWatcher()
    {
        var tracker = BuildTracker(withWatcher: true);
        tracker.Dispose();
        _watcher.Verify(w => w.UnwatchAll(), Times.Once);
    }

    [Fact]
    public void StartInactivityTimer_DoesNotThrow()
    {
        var tracker = BuildTracker();
        tracker.StartInactivityTimer();
        tracker.StopInactivityTimer();
    }
}
