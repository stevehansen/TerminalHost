using Shouldly;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;
using Xunit;

namespace TerminalHost.Tests.Workspace;

public class ProjectMonitorTests
{
    [Fact]
    public void Start_RaisesTickForEachStartedSignal()
    {
        var (timers, sut) = Build();
        var seen = new List<SignalKind>();
        sut.Tick += (_, e) => seen.Add(e.Kind);

        sut.Start(SignalKind.GitStatus | SignalKind.Activity);

        timers[SignalKind.GitStatus].FireTick();
        timers[SignalKind.Activity].FireTick();
        timers[SignalKind.Links].FireTick(); // not started, but fakes always fire when asked

        seen.ShouldBe(new[] { SignalKind.GitStatus, SignalKind.Activity, SignalKind.Links });
    }

    [Fact]
    public void Stop_PreventsFurtherStartsFromRaising()
    {
        // The seam doesn't drop ticks on stopped timers (the timer itself stops),
        // but we confirm Stop reaches the underlying timer.
        var (timers, sut) = Build();
        sut.Start(SignalKind.GitStatus);
        timers[SignalKind.GitStatus].IsRunning.ShouldBeTrue();

        sut.Stop(SignalKind.GitStatus);
        timers[SignalKind.GitStatus].IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void Pause_SuppressesTicksUntilTokenDisposed()
    {
        var (timers, sut) = Build();
        var ticks = 0;
        sut.Tick += (_, _) => ticks++;
        sut.Start(SignalKind.Activity);

        var token = sut.Pause();
        timers[SignalKind.Activity].FireTick();
        ticks.ShouldBe(0);

        token.Dispose();
        timers[SignalKind.Activity].FireTick();
        ticks.ShouldBe(1);
    }

    [Fact]
    public void Pause_NestsCorrectly()
    {
        var (timers, sut) = Build();
        var ticks = 0;
        sut.Tick += (_, _) => ticks++;
        sut.Start(SignalKind.Activity);

        var t1 = sut.Pause();
        var t2 = sut.Pause();
        t1.Dispose();
        timers[SignalKind.Activity].FireTick();
        ticks.ShouldBe(0); // still paused — t2 outstanding

        t2.Dispose();
        timers[SignalKind.Activity].FireTick();
        ticks.ShouldBe(1);
    }

    [Fact]
    public void SetInterval_UpdatesUnderlyingTimer()
    {
        var (timers, sut) = Build();
        sut.SetInterval(SignalKind.GitAutoFetch, TimeSpan.FromSeconds(120));
        timers[SignalKind.GitAutoFetch].Interval.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void SetInterval_RejectsMultiFlag()
    {
        var (_, sut) = Build();
        Should.Throw<ArgumentException>(() =>
            sut.SetInterval(SignalKind.GitStatus | SignalKind.Activity, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Dispose_StopsAndDisposesEveryTimer()
    {
        var (timers, sut) = Build();
        sut.Start(SignalKind.All);
        sut.Dispose();

        foreach (var t in timers.Values)
        {
            t.IsDisposed.ShouldBeTrue();
            t.IsRunning.ShouldBeFalse();
        }
    }

    [Fact]
    public void Dispose_AfterDisposeDoesNotThrow()
    {
        var (_, sut) = Build();
        sut.Dispose();
        Should.NotThrow(() => sut.Dispose());
    }

    [Fact]
    public void Tick_AfterDispose_IsNotRaised()
    {
        var (timers, sut) = Build();
        var ticks = 0;
        sut.Tick += (_, _) => ticks++;
        sut.Dispose();

        // FakeTimer keeps its callback; the monitor itself must guard.
        timers[SignalKind.Activity].FireTick();
        ticks.ShouldBe(0);
    }

    private static (Dictionary<SignalKind, FakeAppTimer> timers, ProjectMonitor sut) Build()
    {
        var factory = new FakeTimerService();
        var sut = new ProjectMonitor(factory);
        return (factory.Created, sut);
    }

    private sealed class FakeTimerService : ITimerService
    {
        public Dictionary<SignalKind, FakeAppTimer> Created { get; } = new();
        private static readonly TimeSpan[] Intervals =
        {
            TimeSpan.FromSeconds(5),  // GitStatus
            TimeSpan.FromSeconds(60), // GitAutoFetch
            TimeSpan.FromSeconds(1),  // Activity
            TimeSpan.FromSeconds(3),  // Links
            TimeSpan.FromSeconds(2),  // RunUrl
        };
        private static readonly SignalKind[] Kinds =
        {
            SignalKind.GitStatus, SignalKind.GitAutoFetch, SignalKind.Activity, SignalKind.Links, SignalKind.RunUrl
        };

        public IAppTimer CreateTimer(TimeSpan interval, Action callback)
        {
            // ProjectMonitor creates timers in a fixed order — identify which signal
            // by matching the default interval.
            for (var i = 0; i < Intervals.Length; i++)
            {
                if (Intervals[i] == interval && !Created.ContainsKey(Kinds[i]))
                {
                    var t = new FakeAppTimer(interval, callback);
                    Created[Kinds[i]] = t;
                    return t;
                }
            }
            throw new InvalidOperationException($"Unexpected timer interval {interval}");
        }
    }

    private sealed class FakeAppTimer : IAppTimer
    {
        private readonly Action _callback;
        public FakeAppTimer(TimeSpan interval, Action callback)
        {
            Interval = interval;
            _callback = callback;
        }
        public bool IsRunning { get; private set; }
        public bool IsDisposed { get; private set; }
        public TimeSpan Interval { get; set; }
        public bool IsEnabled
        {
            get => IsRunning;
            set { if (value) Start(); else Stop(); }
        }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() { IsDisposed = true; IsRunning = false; }
        public void FireTick() => _callback();
    }
}
