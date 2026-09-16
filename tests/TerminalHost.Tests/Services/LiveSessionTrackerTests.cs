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

    private static HookEvent SessionStart(string sessionId, string cwd, string? transcriptPath = null, DateTime? timestamp = null)
        => new()
        {
            SessionId = sessionId,
            Cwd = cwd,
            TranscriptPath = transcriptPath,
            EventType = HookEventType.SessionStart,
            Timestamp = timestamp ?? DateTime.UtcNow,
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

    [Fact]
    public void CheckInactiveSessions_FiresChangedOnce_WhenCrossingThreshold()
    {
        // Session with no activity stream → NoActivityTimeoutMinutes (5) applies.
        var tracker = BuildTracker();
        var fired = 0;
        var stale = SessionStart("stale-1", Path.GetTempPath(), timestamp: DateTime.UtcNow.AddMinutes(-10));
        tracker.HandleSessionStart(stale);
        tracker.LiveSessionsChanged += (_, _) => fired++;

        tracker.CheckInactiveSessions();
        tracker.CheckInactiveSessions(); // second pass: session is already inactive, must not refire.

        fired.ShouldBe(1);
        var live = tracker.GetLiveSessionByClaudeId("stale-1")!;
        live.IsActive.ShouldBeFalse();
        live.EndReason.ShouldBe("timeout");
    }

    [Fact]
    public void CheckInactiveSessions_StaysActive_WhenActivityServiceRefreshedClockRecently()
    {
        // Session started long ago, but the activity service reports recent activity:
        // the idle clock should be derived from LastActivityTime, not StartTime.
        var tracker = BuildTracker(withActivity: true);
        _activity.Setup(a => a.GetState("sess-1"))
            .Returns(new SessionActivityState { SessionId = "sess-1", LastActivityTime = DateTime.UtcNow });

        var oldStart = SessionStart("sess-1", Path.GetTempPath(), timestamp: DateTime.UtcNow.AddMinutes(-60));
        tracker.HandleSessionStart(oldStart);

        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;
        tracker.CheckInactiveSessions();

        fired.ShouldBe(0);
        tracker.GetLiveSessionByClaudeId("sess-1")!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void CheckInactiveSessions_StaysActive_WhenTranscriptWatcherRefreshedClockRecently()
    {
        // Activity from the transcript watcher must feed the same idle clock as the
        // activity service. Without an activity service, GetLastFileChangeTime alone
        // is sufficient to keep the session alive.
        var tracker = BuildTracker(withWatcher: true);
        _watcher.Setup(w => w.GetLastFileChangeTime("sess-1")).Returns(DateTime.UtcNow);

        var oldStart = SessionStart("sess-1", Path.GetTempPath(), timestamp: DateTime.UtcNow.AddMinutes(-60));
        tracker.HandleSessionStart(oldStart);

        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;
        tracker.CheckInactiveSessions();

        fired.ShouldBe(0);
        tracker.GetLiveSessionByClaudeId("sess-1")!.IsActive.ShouldBeTrue();
    }

    private void MarkTimedOut(LiveSessionTracker tracker, string sessionId)
    {
        _activity.Setup(a => a.GetState(sessionId))
            .Returns(new SessionActivityState
            {
                SessionId = sessionId,
                LastActivityTime = DateTime.UtcNow.AddMinutes(-(LiveSessionTracker.InactivityTimeoutMinutes * 5)),
            });
        tracker.CheckInactiveSessions();
    }

    [Fact]
    public void LifecycleChanged_ToActive_ClearsEndTime_OnRevivedSession()
    {
        var tracker = BuildTracker(withActivity: true);
        tracker.HandleSessionStart(SessionStart("sess-1", Path.GetTempPath(),
            timestamp: DateTime.UtcNow.AddMinutes(-30)));
        MarkTimedOut(tracker, "sess-1");

        var live = tracker.GetLiveSessionByClaudeId("sess-1")!;
        live.EndTime.ShouldNotBeNull();
        live.EndReason.ShouldBe("timeout");

        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;

        _activity.Raise(a => a.LifecycleChanged += null, this,
            ("sess-1", SessionLifecycle.Active));

        live.EndTime.ShouldBeNull();
        live.EndReason.ShouldBeNull();
        live.IsActive.ShouldBeTrue();
        fired.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void LifecycleChanged_ToActive_NoOp_WhenSessionStillActive()
    {
        var tracker = BuildTracker(withActivity: true);
        tracker.HandleSessionStart(SessionStart("sess-1", Path.GetTempPath()));

        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;

        _activity.Raise(a => a.LifecycleChanged += null, this,
            ("sess-1", SessionLifecycle.Active));

        var live = tracker.GetLiveSessionByClaudeId("sess-1")!;
        live.EndTime.ShouldBeNull();
        live.IsActive.ShouldBeTrue();
        fired.ShouldBe(0);
    }

    [Fact]
    public void LifecycleChanged_ToTerminal_DoesNotClearEndTime()
    {
        var tracker = BuildTracker(withActivity: true);
        tracker.HandleSessionStart(SessionStart("sess-1", Path.GetTempPath(),
            timestamp: DateTime.UtcNow.AddMinutes(-30)));
        MarkTimedOut(tracker, "sess-1");

        var live = tracker.GetLiveSessionByClaudeId("sess-1")!;
        var endTimeBefore = live.EndTime;
        endTimeBefore.ShouldNotBeNull();

        _activity.Raise(a => a.LifecycleChanged += null, this,
            ("sess-1", SessionLifecycle.Completed));

        live.EndTime.ShouldBe(endTimeBefore);
        live.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void LifecycleChanged_UnknownSession_DoesNotThrow()
    {
        var tracker = BuildTracker(withActivity: true);

        var fired = 0;
        tracker.LiveSessionsChanged += (_, _) => fired++;

        Should.NotThrow(() => _activity.Raise(a => a.LifecycleChanged += null, this,
            ("not-tracked", SessionLifecycle.Active)));

        fired.ShouldBe(0);
    }

    [Fact]
    public void Dispose_UnsubscribesFromActivityLifecycleChanged()
    {
        var tracker = BuildTracker(withActivity: true);
        tracker.HandleSessionStart(SessionStart("sess-1", Path.GetTempPath(),
            timestamp: DateTime.UtcNow.AddMinutes(-30)));
        MarkTimedOut(tracker, "sess-1");

        var live = tracker.GetLiveSessionByClaudeId("sess-1")!;
        var endTimeBefore = live.EndTime;
        endTimeBefore.ShouldNotBeNull();

        tracker.Dispose();

        _activity.Raise(a => a.LifecycleChanged += null, this,
            ("sess-1", SessionLifecycle.Active));

        live.EndTime.ShouldBe(endTimeBefore);
    }
}
