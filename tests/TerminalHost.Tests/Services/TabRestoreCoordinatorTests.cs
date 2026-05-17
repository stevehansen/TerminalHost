using System.Collections.Generic;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class TabRestoreCoordinatorTests
{
    private static ITabViewModel MakeTab() => new Mock<ITabViewModel>().Object;

    private static CenterPanelRestoreEventArgs MakeArgs(
        ITabViewModel tab,
        string panelId = "Git",
        bool skipDataLoad = false) =>
        new()
        {
            Tab = tab,
            PanelId = panelId,
            SkipDataLoad = skipDataLoad
        };

    private static (TabRestoreCoordinator Coordinator, List<CenterPanelRestoreEventArgs> Received) BuildWithHandler()
    {
        var coordinator = new TabRestoreCoordinator();
        var received = new List<CenterPanelRestoreEventArgs>();
        coordinator.RestoreRequested += (_, args) => received.Add(args);
        return (coordinator, received);
    }

    [Fact]
    public void Request_OutsideBatch_FiresRestoreRequestedImmediately()
    {
        var (coordinator, received) = BuildWithHandler();
        coordinator.IsBatching.ShouldBeFalse();

        var args = MakeArgs(MakeTab());
        coordinator.Request(args);

        received.Count.ShouldBe(1);
        received[0].ShouldBeSameAs(args);
    }

    [Fact]
    public void BeginBatch_FlipsIsBatching_AndQueuesRequests()
    {
        var (coordinator, received) = BuildWithHandler();

        coordinator.BeginBatch();
        coordinator.IsBatching.ShouldBeTrue();

        coordinator.Request(MakeArgs(MakeTab()));
        coordinator.Request(MakeArgs(MakeTab()));

        received.ShouldBeEmpty();
    }

    [Fact]
    public void EndBatch_WithNullSelected_FiresAllWithSkipDataLoadTrue()
    {
        var (coordinator, received) = BuildWithHandler();

        var tabA = MakeTab();
        var tabB = MakeTab();
        var tabC = MakeTab();
        var argsA = MakeArgs(tabA, "Git");
        var argsB = MakeArgs(tabB, "Files");
        var argsC = MakeArgs(tabC, "Markdown");

        coordinator.BeginBatch();
        coordinator.Request(argsA);
        coordinator.Request(argsB);
        coordinator.Request(argsC);

        coordinator.EndBatch(null);

        received.Count.ShouldBe(3);
        received.ShouldAllBe(a => a.SkipDataLoad);
        // Insertion order preserved.
        received[0].Tab.ShouldBeSameAs(tabA);
        received[1].Tab.ShouldBeSameAs(tabB);
        received[2].Tab.ShouldBeSameAs(tabC);
    }

    [Fact]
    public void EndBatch_WithMatchingSelectedTab_FiresNonSelectedFirstThenSelectedLast()
    {
        var (coordinator, received) = BuildWithHandler();

        var tabA = MakeTab();
        var tabB = MakeTab();
        var tabC = MakeTab();
        var argsA = MakeArgs(tabA, "Git");
        var argsB = MakeArgs(tabB, "Files");
        var argsC = MakeArgs(tabC, "Markdown");

        coordinator.BeginBatch();
        coordinator.Request(argsA);
        coordinator.Request(argsB);
        coordinator.Request(argsC);

        coordinator.EndBatch(tabB);

        received.Count.ShouldBe(3);
        // tabA fires first (non-selected, SkipDataLoad=true).
        received[0].Tab.ShouldBeSameAs(tabA);
        received[0].SkipDataLoad.ShouldBeTrue();
        // tabC fires next (non-selected, SkipDataLoad=true).
        received[1].Tab.ShouldBeSameAs(tabC);
        received[1].SkipDataLoad.ShouldBeTrue();
        // tabB fires last with its original args (SkipDataLoad unchanged = false).
        received[2].ShouldBeSameAs(argsB);
        received[2].SkipDataLoad.ShouldBeFalse();
    }

    [Fact]
    public void EndBatch_WithSelectedTabNotInQueue_FiresAllWithSkipDataLoadTrue()
    {
        var (coordinator, received) = BuildWithHandler();

        var tabA = MakeTab();
        var tabB = MakeTab();
        var tabC = MakeTab(); // not queued
        var argsA = MakeArgs(tabA);
        var argsB = MakeArgs(tabB);

        coordinator.BeginBatch();
        coordinator.Request(argsA);
        coordinator.Request(argsB);

        coordinator.EndBatch(tabC);

        received.Count.ShouldBe(2);
        received.ShouldAllBe(a => a.SkipDataLoad);
        received[0].Tab.ShouldBeSameAs(tabA);
        received[1].Tab.ShouldBeSameAs(tabB);
    }

    [Fact]
    public void EndBatch_OutsideBatch_IsNoOp()
    {
        var (coordinator, received) = BuildWithHandler();

        coordinator.IsBatching.ShouldBeFalse();
        Should.NotThrow(() => coordinator.EndBatch(null));

        received.ShouldBeEmpty();
        coordinator.IsBatching.ShouldBeFalse();
    }

    [Fact]
    public void BeginBatch_CalledTwice_IsIdempotent_DoesNotClearQueue()
    {
        // Observed behavior of the current implementation: a second BeginBatch while
        // already batching is a no-op (logs a Debug warning and reuses the existing
        // queue). Items requested before the second BeginBatch are still dispatched
        // by the eventual EndBatch.
        var (coordinator, received) = BuildWithHandler();

        coordinator.BeginBatch();
        var args1 = MakeArgs(MakeTab());
        coordinator.Request(args1);

        coordinator.BeginBatch(); // second call — should be idempotent.
        coordinator.IsBatching.ShouldBeTrue();

        coordinator.EndBatch(null);

        received.Count.ShouldBe(1);
        received[0].Tab.ShouldBeSameAs(args1.Tab);
        received[0].SkipDataLoad.ShouldBeTrue();
    }

    [Fact]
    public void AfterEndBatch_IsBatchingFalse_AndRequestResumesImmediateDispatch()
    {
        var (coordinator, received) = BuildWithHandler();

        coordinator.BeginBatch();
        coordinator.EndBatch(null);

        coordinator.IsBatching.ShouldBeFalse();

        var args = MakeArgs(MakeTab());
        coordinator.Request(args);

        received.Count.ShouldBe(1);
        received[0].ShouldBeSameAs(args);
    }
}
