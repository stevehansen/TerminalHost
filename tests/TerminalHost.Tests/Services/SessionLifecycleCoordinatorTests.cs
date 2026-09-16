using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityEvent = TerminalHost.Core.Domain.ActivityEvent;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Tests.TestAdapters;
using Xunit;

namespace TerminalHost.Tests.Services;

public class SessionLifecycleCoordinatorTests
{
    private static (SessionLifecycleCoordinator Coordinator, SessionActivityService Activity, LiveSessionTracker Live, Mock<ISessionStateStore> Store)
        Build(IInactivityClock? clock = null)
    {
        var store = new Mock<ISessionStateStore>();
        store.Setup(s => s.EnsureIntentForWorkingDirectory(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((cwd, name) => Intent.Create(name, "main", cwd));

        var activity = new SessionActivityService();
        var live = new LiveSessionTracker(store.Object, activityService: activity);
        var coord = new SessionLifecycleCoordinator(activity, live, clock);
        return (coord, activity, live, store);
    }

    private static HookEvent SessionStart(string sessionId, DateTime? timestamp = null) => new()
    {
        EventType = HookEventType.SessionStart,
        SessionId = sessionId,
        Cwd = Path.GetTempPath(),
        Timestamp = timestamp ?? DateTime.UtcNow,
        Source = SessionSource.Local,
    };

    private static HookEvent SessionStop(string sessionId) => new()
    {
        EventType = HookEventType.SessionStop,
        SessionId = sessionId,
        Cwd = Path.GetTempPath(),
        Timestamp = DateTime.UtcNow,
    };

    [Fact]
    public void Ingest_SessionStart_GetSession_ReturnsLiveView()
    {
        var (coord, _, _, _) = Build();

        coord.Ingest(SessionStart("sess-1"));

        var view = coord.GetSession("sess-1");
        view.ShouldNotBeNull();
        view!.SessionId.ShouldBe("sess-1");
        view.IsLive.ShouldBeTrue();
        view.LiveSession.ShouldNotBeNull();
    }

    [Fact]
    public async Task Ingest_SessionStop_MarksSessionNotLive()
    {
        var (coord, _, _, _) = Build();
        coord.Ingest(SessionStart("sess-1"));

        coord.Ingest(SessionStop("sess-1"));
        // HandleSessionStopAsync sets EndTime synchronously even though it returns a Task;
        // give the synchronous portion a beat to flush.
        await Task.Yield();

        var view = coord.GetSession("sess-1");
        view.ShouldNotBeNull();
        view!.IsLive.ShouldBeFalse();
    }

    [Fact]
    public void ActivityEventProcessed_Repassed_FromActivityService()
    {
        var (coord, _, _, _) = Build();
        var events = new List<ActivityEvent>();
        coord.ActivityEventProcessed += (_, e) => events.Add(e);

        coord.Ingest(SessionStart("sess-1"));

        events.ShouldNotBeEmpty();
        events.ShouldContain(e => e.Type == ActivityEventType.SessionStart);
    }

    [Fact]
    public void Changed_FiresOnPermissionPrompt_AsCreated()
    {
        // The activity service raises LifecycleChanged on the WaitingPermission transition,
        // which is the first time the coordinator sees a lifecycle event for this session —
        // so it classifies as Created (previous == null).
        var (coord, _, _, _) = Build();
        var changes = new List<SessionChanged>();
        coord.Changed += (_, e) => changes.Add(e);

        coord.Ingest(SessionStart("sess-1"));
        coord.Ingest(new HookEvent
        {
            EventType = HookEventType.Notification,
            SessionId = "sess-1",
            Cwd = Path.GetTempPath(),
            NotificationType = "permission_prompt",
            Timestamp = DateTime.UtcNow,
        });

        changes.ShouldContain(c => c.SessionId == "sess-1" && c.Kind == SessionChangeKind.Created);
    }

    [Fact]
    public void ManualRevive_OnTerminalSession_RaisesRevivedAndActive()
    {
        var (coord, activity, _, _) = Build();
        coord.Ingest(SessionStart("sess-1"));
        var state = activity.GetState("sess-1")!;
        state.Lifecycle = SessionLifecycle.Completed;
        state.EndTime = DateTime.UtcNow;

        var changes = new List<SessionChanged>();
        coord.Changed += (_, e) => changes.Add(e);

        coord.Advanced.ManualRevive("sess-1", "user-request");

        var revived = changes.LastOrDefault(c => c.SessionId == "sess-1");
        revived.ShouldNotBeNull();
        revived!.Kind.ShouldBe(SessionChangeKind.Revived);
        revived.After.Lifecycle.ShouldBe(SessionLifecycle.Active);
        state.EndTime.ShouldBeNull();
    }

    [Fact]
    public void ManualRevive_OnNonTerminalSession_IsNoOp()
    {
        var (coord, _, _, _) = Build();
        coord.Ingest(SessionStart("sess-1"));

        var changes = new List<SessionChanged>();
        coord.Changed += (_, e) => changes.Add(e);

        coord.Advanced.ManualRevive("sess-1", "user-request");

        changes.ShouldBeEmpty();
    }

    [Fact]
    public void ForceTerminate_WithErrorReason_RaisesEndedFailed()
    {
        var (coord, _, _, _) = Build();
        coord.Ingest(SessionStart("sess-1"));

        var changes = new List<SessionChanged>();
        coord.Changed += (_, e) => changes.Add(e);

        coord.Advanced.ForceTerminate("sess-1", "error");

        var ended = changes.LastOrDefault(c => c.SessionId == "sess-1");
        ended.ShouldNotBeNull();
        ended!.Kind.ShouldBe(SessionChangeKind.Ended);
        ended.After.Lifecycle.ShouldBe(SessionLifecycle.Failed);
        ended.After.EndTime.ShouldNotBeNull();
    }

    [Fact]
    public void ForceTerminate_WithExplicitReason_RaisesEndedCompleted()
    {
        var (coord, _, _, _) = Build();
        coord.Ingest(SessionStart("sess-1"));

        var changes = new List<SessionChanged>();
        coord.Changed += (_, e) => changes.Add(e);

        coord.Advanced.ForceTerminate("sess-1", "explicit");

        var ended = changes.LastOrDefault(c => c.SessionId == "sess-1");
        ended.ShouldNotBeNull();
        ended!.Kind.ShouldBe(SessionChangeKind.Ended);
        ended.After.Lifecycle.ShouldBe(SessionLifecycle.Completed);
    }

    [Fact]
    public void GetAllSessions_IncludesEndedSessions()
    {
        var (coord, _, _, _) = Build();
        coord.Ingest(SessionStart("sess-active"));
        coord.Ingest(SessionStart("sess-ended"));
        coord.Advanced.ForceTerminate("sess-ended", "explicit");

        var all = coord.GetAllSessions();

        all.Select(s => s.SessionId).ShouldContain("sess-active");
        all.Select(s => s.SessionId).ShouldContain("sess-ended");
    }

    [Fact]
    public void GetActiveSessions_ExcludesSessionsThatAreInactiveOnBothSides()
    {
        // GetActiveSessions filters _activity.GetActiveStates() ∪ live-where-IsActive.
        // To exclude a session it must be inactive on both axes: live.EndTime set AND
        // activity-state DeriveParentDisplay == TimedOut. We engineer that by stopping
        // the live session and back-dating the main agent's timestamps so its display
        // derives to TimedOut.
        var (coord, activity, _, _) = Build();
        coord.Ingest(SessionStart("sess-active"));
        coord.Ingest(SessionStart("sess-ended"));
        coord.Ingest(SessionStop("sess-ended"));

        var endedState = activity.GetState("sess-ended")!;
        var main = endedState.Agents.Values.FirstOrDefault(a => a.IsMain);
        main.ShouldNotBeNull();
        var old = DateTime.UtcNow - TimeSpan.FromHours(1);
        // Force LastEventKind == Stop with elapsed > 2-min threshold so DeriveParentDisplay → TimedOut.
        // LastActivityEventTime must be ≤ LastStopHookTime or Activity wins the tie-break.
        main!.LastActivityEventTime = old;
        main.LastStopHookTime = old;
        main.CompleteTime = old;

        var active = coord.GetActiveSessions();

        active.Select(s => s.SessionId).ShouldContain("sess-active");
        active.Select(s => s.SessionId).ShouldNotContain("sess-ended");
    }

    [Fact]
    public void GetSession_UnknownId_ReturnsNull()
    {
        var (coord, _, _, _) = Build();
        coord.GetSession("never-seen").ShouldBeNull();
    }

    [Fact]
    public void InactivityScan_BackdatedStart_RaisesEndedViaVirtualClock()
    {
        // LiveSessionTracker.CheckInactiveSessions uses DateTime.UtcNow internally — so to
        // exercise the "no activity ever" branch (NoActivityTimeoutMinutes = 5), back-date
        // the hook event's Timestamp by >5 minutes. Advancing the FakeInactivityClock then
        // drives the coordinator to run the sweep, which detects the timeout and emits
        // a per-session Ended notification.
        var clock = new FakeInactivityClock();
        var (coord, _, _, _) = Build(clock);

        coord.Ingest(SessionStart("sess-1", timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(6)));

        var changes = new List<SessionChanged>();
        coord.Changed += (_, e) => changes.Add(e);

        coord.Advanced.StartInactivityClock();
        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));

        var ended = changes.FirstOrDefault(c => c.SessionId == "sess-1" && c.Kind == SessionChangeKind.Ended);
        ended.ShouldNotBeNull();
        ended!.After.EndTime.ShouldNotBeNull();
        ended.After.Lifecycle.ShouldBe(SessionLifecycle.TimedOut);
    }

    [Fact]
    public void ConcurrentIngestAndSweep_CompletesWithinBudget_NoDeadlock()
    {
        const int perThread = 200;
        var (coord, _, _, _) = Build();

        var done = new ManualResetEventSlim(false);
        Exception? threadException = null;

        void Ingester(string prefix)
        {
            try
            {
                for (int i = 0; i < perThread; i++)
                    coord.Ingest(SessionStart($"{prefix}-{i}"));
            }
            catch (Exception ex) { threadException = ex; }
        }

        var sw = Stopwatch.StartNew();

        var ingestA = new Thread(() => Ingester("A")) { IsBackground = true };
        var ingestB = new Thread(() => Ingester("B")) { IsBackground = true };
        var sweep = new Thread(() =>
        {
            try
            {
                while (!done.IsSet)
                    _ = coord.GetAllSessions();
            }
            catch (Exception ex) { threadException = ex; }
        }) { IsBackground = true };

        ingestA.Start();
        ingestB.Start();
        sweep.Start();

        var joinA = ingestA.Join(TimeSpan.FromSeconds(2));
        var joinB = ingestB.Join(TimeSpan.FromSeconds(2));
        done.Set();
        var joinSweep = sweep.Join(TimeSpan.FromSeconds(2));

        sw.Stop();

        joinA.ShouldBeTrue($"ingest A did not join within budget — likely deadlock (elapsed {sw.ElapsedMilliseconds}ms)");
        joinB.ShouldBeTrue($"ingest B did not join within budget — likely deadlock (elapsed {sw.ElapsedMilliseconds}ms)");
        joinSweep.ShouldBeTrue($"sweep did not join within budget — likely deadlock (elapsed {sw.ElapsedMilliseconds}ms)");
        threadException.ShouldBeNull();
        sw.ElapsedMilliseconds.ShouldBeLessThan(2000);

        coord.GetAllSessions().Count.ShouldBe(perThread * 2);
    }

    [Fact]
    public void SessionsChanged_FiresOnIngestSessionStart()
    {
        var (coord, _, _, _) = Build();
        int pulses = 0;
        coord.SessionsChanged += (_, _) => pulses++;

        coord.Ingest(SessionStart("sess-1"));

        pulses.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void SessionsChanged_CoalescesSiblingEventsInSingleIngest_ToSinglePulse()
    {
        // ProcessHookEvent(SessionStart) raises multiple ActivityEventProcessed events
        // (SessionStart + AgentSpawn) and may raise LifecycleChanged too. Each upstream
        // signal calls PulseSessionsChanged. The dispatch-frame opened by Ingest defers
        // every one of them into a single drain on frame exit.
        var (coord, _, _, _) = Build();
        int pulses = 0;
        coord.SessionsChanged += (_, _) => pulses++;

        coord.Ingest(SessionStart("sess-1"));

        pulses.ShouldBe(1);
    }

    [Fact]
    public void SessionsChanged_ReentryFromHandler_FiresOncePerOuterCall()
    {
        // Re-entry from a handler is a separate outer call — it opens its own dispatch
        // frame and drains its own pulse. Outer Ingest = 1 pulse; nested Ingest = +1.
        var (coord, _, _, _) = Build();
        int pulses = 0;
        bool reentered = false;
        coord.SessionsChanged += (_, _) =>
        {
            pulses++;
            if (!reentered)
            {
                reentered = true;
                coord.Ingest(SessionStart("nested-1"));
            }
        };

        coord.Ingest(SessionStart("outer-1"));

        pulses.ShouldBe(2);
    }

    [Fact]
    public void SessionsChanged_FiresOnInactivityTick_WithNoLifecycleTransition()
    {
        // Recent timestamp ⇒ no timeout transition fires from the sweep. The pulse must
        // still arrive because consumers care about "5m ago → 5m1s ago" relative-time
        // re-renders even when nothing crossed a lifecycle boundary.
        var clock = new FakeInactivityClock();
        var (coord, _, _, _) = Build(clock);

        coord.Ingest(SessionStart("sess-1", timestamp: DateTime.UtcNow));

        var changesBefore = new List<SessionChanged>();
        coord.Changed += (_, e) => changesBefore.Add(e);

        int pulses = 0;
        coord.SessionsChanged += (_, _) => pulses++;

        coord.Advanced.StartInactivityClock();
        clock.Advance(TimeSpan.FromSeconds(1));

        pulses.ShouldBeGreaterThan(0);
        changesBefore.ShouldBeEmpty();
    }

    private static HookEvent SessionStartWithCwd(string sessionId, string cwd) => new()
    {
        EventType = HookEventType.SessionStart,
        SessionId = sessionId,
        Cwd = cwd,
        Timestamp = DateTime.UtcNow,
        Source = SessionSource.Local,
    };

    [Fact]
    public void GetSessionsForDisplay_DedupesByWorkingDirectory_MostRecentlyActiveWins()
    {
        var (coord, _, _, _) = Build();
        var sharedCwd = Path.Combine(Path.GetTempPath(), "dedupe-wd");

        coord.Ingest(SessionStartWithCwd("old-id", sharedCwd));
        // Force a measurable gap so StartTime ordering is deterministic.
        Thread.Sleep(10);
        coord.Ingest(SessionStartWithCwd("new-id", sharedCwd));
        coord.Advanced.ForceTerminate("old-id", "explicit");

        var display = coord.GetSessionsForDisplay();

        display.Count.ShouldBe(1);
        display[0].SessionId.ShouldBe("new-id");
    }

    [Fact]
    public void GetSessionsForDisplay_FallsBackToSessionIdWhenWorkingDirectoryEmpty()
    {
        var (coord, _, _, _) = Build();

        coord.Ingest(SessionStartWithCwd("sess-a", ""));
        coord.Ingest(SessionStartWithCwd("sess-b", ""));

        var display = coord.GetSessionsForDisplay();

        display.Select(v => v.SessionId).ShouldBe(new[] { "sess-a", "sess-b" }, ignoreOrder: true);
        display.Count.ShouldBe(2);
    }

    [Fact]
    public void GetSessionsForDisplay_OrdersActiveFirst_ThenLastActivityDescending()
    {
        // Three sessions across two workspaces. Force sess-old-active to "TimedOut" via
        // back-dated agent timestamps (same trick as GetActiveSessions_Excludes...) so
        // ActivityState.IsActive flips to false. The two remaining active sessions are
        // ordered by recency (StartTime ↓ since LastActivityTime starts null).
        var (coord, activity, _, _) = Build();
        var wdA = Path.Combine(Path.GetTempPath(), "ord-a");
        var wdB = Path.Combine(Path.GetTempPath(), "ord-b");

        coord.Ingest(SessionStartWithCwd("sess-old-active", wdA));
        Thread.Sleep(10);
        coord.Ingest(SessionStartWithCwd("sess-mid", wdB));
        Thread.Sleep(10);
        coord.Ingest(SessionStartWithCwd("sess-new", wdA));

        // Force sess-old-active to derive as TimedOut (and thus IsActive == false).
        var oldState = activity.GetState("sess-old-active")!;
        var oldMain = oldState.Agents.Values.First(a => a.IsMain);
        var stale = DateTime.UtcNow - TimeSpan.FromHours(1);
        oldMain.LastActivityEventTime = stale;
        oldMain.LastStopHookTime = stale;
        oldMain.CompleteTime = stale;

        // sess-old-active and sess-new share wdA; dedupe will pick sess-new (active, newer).
        var display = coord.GetSessionsForDisplay();

        // Expect: [sess-new (active, wdA), sess-mid (active, wdB)] — sess-old-active is collapsed.
        display.Select(v => v.SessionId).ShouldBe(new[] { "sess-new", "sess-mid" });
        display.All(v => v.ActivityState.IsActive).ShouldBeTrue();
    }

    [Fact]
    public void GetSessionsForDisplay_CaseInsensitiveWorkingDirectoryGrouping()
    {
        var (coord, _, _, _) = Build();

        coord.Ingest(SessionStartWithCwd("sess-upper", @"C:\Foo"));
        coord.Ingest(SessionStartWithCwd("sess-lower", @"c:\foo"));

        var display = coord.GetSessionsForDisplay();

        display.Count.ShouldBe(1);
    }
}
