using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Tests.TestAdapters;

namespace TerminalHost.Tests.Panels;

public partial class PanelRouterTests
{
    private static (PanelRouter Router, InMemoryPanelPersistence Persistence, Dictionary<(PanelZone, PanelScope), InMemoryPanelSurface> Surfaces) BuildRouter(
        params (PanelZone Zone, PanelScope Scope)[] surfaceSlots)
    {
        var surfaces = surfaceSlots
            .Select(s => new InMemoryPanelSurface { Zone = s.Zone, Scope = s.Scope })
            .ToList();
        var persistence = new InMemoryPanelPersistence();
        var dispatcher = new SynchronousDispatcherService();
        var router = new PanelRouter(surfaces, persistence, dispatcher);
        var byKey = surfaces.ToDictionary(s => (s.Zone, s.Scope));
        return (router, persistence, byKey);
    }

    [Fact]
    public void Show_OpensPanelInPreferredZone_WhenNoOverride()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.RightDock, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);

        router.Show(vm);

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        vm.IsOpen.ShouldBeTrue();
        vm.PreferredSide.ShouldBe(PanelSide.Right);
    }

    [Fact]
    public void Show_OpensInPopup_WhenNoPreferenceAndNoOverride()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("help");

        router.Show(vm);

        surfaces[(PanelZone.Popup, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
    }

    [Fact]
    public void Show_ExplicitZone_OverridesPreference()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window));

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        vm.DisplayState.ShouldBe(PanelDisplayState.Window);
    }

    [Fact]
    public void Show_SecondCall_TogglesClose_WhenAlreadyOpenInSameZone()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("palette");

        router.Show(vm);
        router.Show(vm);

        surfaces[(PanelZone.Popup, PanelScope.AppShell)].Mounted.ShouldBeNull();
        router.IsOpen("palette").ShouldBeFalse();
        vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Show_SecondCall_WithForceShow_DoesNotToggle_FocusesInstead()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("palette");

        router.Show(vm);
        router.Show(vm, new PanelShowOptions(ForceShow: true));

        surfaces[(PanelZone.Popup, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        router.IsOpen("palette").ShouldBeTrue();
        surfaces[(PanelZone.Popup, PanelScope.AppShell)].Focuses.ShouldBe(1);
    }

    [Fact]
    public void Show_SecondCall_DifferentZone_MovesPanel_PreservingVmIdentity()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);

        router.Show(vm);
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window));

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        router.Get("git").ShouldBeSameAs(vm);
        vm.DisplayState.ShouldBe(PanelDisplayState.Window);
    }

    [Fact]
    public void Move_PreservesVmIdentity_AndUpdatesDisplayState_AndPreferredSide()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.LeftDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("explorer", PanelZone.RightDock);

        router.Show(vm);
        router.Move("explorer", PanelZone.Window);
        vm.DisplayState.ShouldBe(PanelDisplayState.Window);
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);

        router.Move("explorer", PanelZone.LeftDock);

        vm.DisplayState.ShouldBe(PanelDisplayState.Panel);
        vm.PreferredSide.ShouldBe(PanelSide.Left);
        surfaces[(PanelZone.LeftDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeNull();
    }

    [Fact]
    public void Move_RestoresOldSurfaceMount_WhenNewSurfaceThrows()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        router.Show(vm);

        surfaces[(PanelZone.Window, PanelScope.AppShell)].MountException = new InvalidOperationException("boom");

        Should.Throw<InvalidOperationException>(() => router.Move("git", PanelZone.Window));

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        vm.DisplayState.ShouldBe(PanelDisplayState.Panel);
        router.Get("git").ShouldBeSameAs(vm);
    }

    [Fact]
    public void Close_UnmountsAndSetsIsOpenFalse_AndPersists()
    {
        var (router, persistence, surfaces) = BuildRouter((PanelZone.RightDock, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        router.Show(vm);
        var savesBefore = persistence.SaveCallCount;

        router.Close("git");

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
        vm.IsOpen.ShouldBeFalse();
        router.IsOpen("git").ShouldBeFalse();
        persistence.SaveCallCount.ShouldBeGreaterThan(savesBefore);
    }

    [Fact]
    public void CloseZone_ClosesAllPanelsInZoneAndScope_LeavesOtherScopesAlone()
    {
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.Popup, tabA),
            (PanelZone.Popup, tabB),
            (PanelZone.RightDock, tabA));
        var a1 = new StubPanelableViewModel("a1");
        var a2 = new StubPanelableViewModel("a2");
        var b1 = new StubPanelableViewModel("b1");
        var aDock = new StubPanelableViewModel("aDock");

        router.Show(a1, new PanelShowOptions(Zone: PanelZone.Popup, Scope: tabA));
        router.Show(a2, new PanelShowOptions(Zone: PanelZone.Popup, Scope: tabA));
        router.Show(b1, new PanelShowOptions(Zone: PanelZone.Popup, Scope: tabB));
        router.Show(aDock, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));

        router.CloseZone(PanelZone.Popup, tabA);

        router.IsOpen("a1").ShouldBeFalse();
        router.IsOpen("a2").ShouldBeFalse();
        router.IsOpen("b1").ShouldBeTrue();
        router.IsOpen("aDock").ShouldBeTrue();
        surfaces[(PanelZone.Popup, tabB)].Mounted.ShouldBeSameAs(b1);
        surfaces[(PanelZone.RightDock, tabA)].Mounted.ShouldBeSameAs(aDock);
    }

    [Fact]
    public void Surface_DismissRequested_TriggersClose()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("help");
        router.Show(vm);

        surfaces[(PanelZone.Popup, PanelScope.AppShell)].RaiseDismiss("help");

        router.IsOpen("help").ShouldBeFalse();
        vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void StateChangeRequested_FromVm_TranslatesToMove()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        router.Show(vm);

        vm.TriggerStateChangeRequest(PanelDisplayState.Window);

        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
        vm.DisplayState.ShouldBe(PanelDisplayState.Window);
    }

    [Fact]
    public void Scope_Isolation_PerTabPanelsDoNotInterfereWithAppShell()
    {
        var tabA = PanelScope.ForTab("a");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.Popup, PanelScope.AppShell),
            (PanelZone.Popup, tabA));
        var shellVm = new StubPanelableViewModel("shared");
        var tabVm = new StubPanelableViewModel("shared");

        router.Show(shellVm, new PanelShowOptions(Scope: PanelScope.AppShell));
        router.Show(tabVm, new PanelShowOptions(Scope: tabA));

        // Both should be open — different scopes.
        surfaces[(PanelZone.Popup, PanelScope.AppShell)].Mounted.ShouldBeSameAs(shellVm);
        surfaces[(PanelZone.Popup, tabA)].Mounted.ShouldBeSameAs(tabVm);
    }

    [Fact]
    public void Scope_Isolation_TwoTabsHaveIndependentRouting()
    {
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabA),
            (PanelZone.RightDock, tabB));
        var a = new StubPanelableViewModel("git");
        var b = new StubPanelableViewModel("git");

        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        surfaces[(PanelZone.RightDock, tabA)].Mounted.ShouldBeSameAs(a);
        surfaces[(PanelZone.RightDock, tabB)].Mounted.ShouldBeSameAs(b);
    }

    [Fact]
    public void Scope_Isolation_ToggleCloseDoesNotAffectSiblingTab()
    {
        // Regression: ToggleClose used PanelId-based Close which matched the first sibling-tab
        // registration in iteration order — unmounting the wrong tab's panel and leaving a desync.
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabA),
            (PanelZone.RightDock, tabB));
        var a = new StubPanelableViewModel("sessions");
        var b = new StubPanelableViewModel("sessions");

        // Open in tabA first so it appears first in dictionary iteration order.
        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        // Toggle-close in tabB — this should close tabB's panel, not tabA's.
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        surfaces[(PanelZone.RightDock, tabA)].Mounted.ShouldBeSameAs(a, "tabA's panel must remain mounted");
        surfaces[(PanelZone.RightDock, tabB)].Mounted.ShouldBeNull("tabB's panel should have been closed");
        a.IsOpen.ShouldBeTrue();
        b.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Scope_Isolation_MoveBetweenZonesDoesNotAffectSiblingTab()
    {
        // Regression: ShowDecision.Move used PanelId-based Move which matched the first sibling-tab
        // registration in iteration order — moving the wrong tab's panel.
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabA),
            (PanelZone.LeftDock, tabA),
            (PanelZone.RightDock, tabB),
            (PanelZone.LeftDock, tabB));
        var a = new StubPanelableViewModel("explorer");
        var b = new StubPanelableViewModel("explorer");

        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        // Move tabB's panel via Show with a different zone — must hit ShowDecision.Move on tabB's reg.
        router.Show(b, new PanelShowOptions(Zone: PanelZone.LeftDock, Scope: tabB));

        surfaces[(PanelZone.RightDock, tabA)].Mounted.ShouldBeSameAs(a, "tabA stays on RightDock");
        surfaces[(PanelZone.LeftDock, tabA)].Mounted.ShouldBeNull("tabA should not have moved");
        surfaces[(PanelZone.RightDock, tabB)].Mounted.ShouldBeNull("tabB moved off RightDock");
        surfaces[(PanelZone.LeftDock, tabB)].Mounted.ShouldBeSameAs(b, "tabB moved to LeftDock");
    }

    [Fact]
    public void MissingSurface_Throws_WithClearMessage()
    {
        var (router, _, _) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("git");

        var ex = Should.Throw<InvalidOperationException>(() =>
            router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window)));

        ex.Message.ShouldContain("Window");
        ex.Message.ShouldContain("AppShell");
    }

    [Fact]
    public void AllowMultiInstance_AllowsMultipleRegistrations()
    {
        var (router, _, _) = BuildRouter((PanelZone.Window, PanelScope.AppShell));
        var a = new StubPanelableViewModel("viewer");
        var b = new StubPanelableViewModel("viewer");

        router.Show(a, new PanelShowOptions(Zone: PanelZone.Window, AllowMultiInstance: true));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.Window, AllowMultiInstance: true));

        router.IsOpen("viewer").ShouldBeTrue();
        a.IsOpen.ShouldBeTrue();
        b.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Routed_EventFires_WithCorrectOldAndNewZones()
    {
        var (router, _, _) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        var events = new List<PanelRoutedEventArgs>();
        router.Routed += (_, e) => events.Add(e);

        router.Show(vm);
        router.Move("git", PanelZone.Window);
        router.Close("git");

        events.Count.ShouldBe(3);
        events[0].OldZone.ShouldBeNull();
        events[0].NewZone.ShouldBe(PanelZone.RightDock);
        events[1].OldZone.ShouldBe(PanelZone.RightDock);
        events[1].NewZone.ShouldBe(PanelZone.Window);
        events[2].OldZone.ShouldBe(PanelZone.Window);
        events[2].NewZone.ShouldBeNull();
    }

    [Fact]
    public void Restore_ReplaysOpenEntries_FromPersistence()
    {
        var persistence = new InMemoryPanelPersistence();
        var scope = PanelScope.AppShell;
        persistence.Seed(scope, new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, scope, IsOpen: true),
            new PanelLayoutEntry("explorer", PanelZone.LeftDock, scope, IsOpen: true),
            new PanelLayoutEntry("stale", PanelZone.Popup, scope, IsOpen: false),
        }));
        var surfaces = new List<IPanelSurface>
        {
            new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = scope },
            new InMemoryPanelSurface { Zone = PanelZone.LeftDock, Scope = scope },
            new InMemoryPanelSurface { Zone = PanelZone.Popup, Scope = scope },
        };
        var router = new PanelRouter(surfaces, persistence, new SynchronousDispatcherService());
        var git = new StubPanelableViewModel("git");
        var explorer = new StubPanelableViewModel("explorer");

        router.Restore(scope, id => id switch
        {
            "git" => git,
            "explorer" => explorer,
            _ => null,
        });

        router.IsOpen("git").ShouldBeTrue();
        router.IsOpen("explorer").ShouldBeTrue();
        router.IsOpen("stale").ShouldBeFalse();
        git.DisplayState.ShouldBe(PanelDisplayState.Panel);
        git.PreferredSide.ShouldBe(PanelSide.Right);
        explorer.PreferredSide.ShouldBe(PanelSide.Left);
    }

    [Fact]
    public void Move_SameZone_IsNoOp()
    {
        var (router, persistence, surfaces) = BuildRouter((PanelZone.RightDock, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        router.Show(vm);
        var surface = surfaces[(PanelZone.RightDock, PanelScope.AppShell)];
        var mountsBefore = surface.Mounts;
        var unmountsBefore = surface.Unmounts;
        var savesBefore = persistence.SaveCallCount;
        var routedEvents = 0;
        router.Routed += (_, _) => routedEvents++;

        router.Move("git", PanelZone.RightDock);

        surface.Mounts.ShouldBe(mountsBefore);
        surface.Unmounts.ShouldBe(unmountsBefore);
        persistence.SaveCallCount.ShouldBe(savesBefore);
        routedEvents.ShouldBe(0);
        router.Get("git").ShouldBeSameAs(vm);
    }

    [Fact]
    public void Close_OnUnknownPanel_IsSilentNoOp()
    {
        var (router, persistence, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var savesBefore = persistence.SaveCallCount;
        var surface = surfaces[(PanelZone.Popup, PanelScope.AppShell)];
        var unmountsBefore = surface.Unmounts;
        var routedEvents = 0;
        router.Routed += (_, _) => routedEvents++;

        Should.NotThrow(() => router.Close("does-not-exist"));

        persistence.SaveCallCount.ShouldBe(savesBefore);
        surface.Unmounts.ShouldBe(unmountsBefore);
        routedEvents.ShouldBe(0);
    }

    [Fact]
    public void Restore_SkipsEntriesWithUnresolvedVm()
    {
        var persistence = new InMemoryPanelPersistence();
        var scope = PanelScope.AppShell;
        persistence.Seed(scope, new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, scope, IsOpen: true),
            new PanelLayoutEntry("explorer", PanelZone.LeftDock, scope, IsOpen: true),
        }));
        var surfaces = new List<IPanelSurface>
        {
            new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = scope },
            new InMemoryPanelSurface { Zone = PanelZone.LeftDock, Scope = scope },
        };
        var router = new PanelRouter(surfaces, persistence, new SynchronousDispatcherService());
        var explorer = new StubPanelableViewModel("explorer");

        router.Restore(scope, id => id == "explorer" ? explorer : null);

        router.IsOpen("git").ShouldBeFalse();
        router.IsOpen("explorer").ShouldBeTrue();
    }

    [Fact]
    public void Routed_FiresOnToggleClosePath_FromShow()
    {
        var (router, _, _) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("palette");
        var events = new List<PanelRoutedEventArgs>();
        router.Routed += (_, e) => events.Add(e);

        router.Show(vm);
        router.Show(vm); // toggles closed

        events.Count.ShouldBe(2);
        events[0].OldZone.ShouldBeNull();
        events[0].NewZone.ShouldBe(PanelZone.Popup);
        events[1].OldZone.ShouldBe(PanelZone.Popup);
        events[1].NewZone.ShouldBeNull();
    }

    [Fact]
    public void Move_DoubleFailure_ForceClosesPanel_AndThrowsAggregate()
    {
        var (router, persistence, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        router.Show(vm);

        // Make both surfaces permanently throw on Mount.
        var oldSurface = surfaces[(PanelZone.RightDock, PanelScope.AppShell)];
        var newSurface = surfaces[(PanelZone.Window, PanelScope.AppShell)];
        oldSurface.MountExceptionIsPermanent = true;
        oldSurface.MountException = new InvalidOperationException("rollback-boom");
        newSurface.MountExceptionIsPermanent = true;
        newSurface.MountException = new InvalidOperationException("new-boom");

        var routedEvents = new List<PanelRoutedEventArgs>();
        router.Routed += (_, e) => routedEvents.Add(e);
        var savesBefore = persistence.SaveCallCount;

        var ex = Should.Throw<AggregateException>(() => router.Move("git", PanelZone.Window));
        ex.InnerExceptions.Count.ShouldBe(2);
        ex.InnerExceptions.OfType<InvalidOperationException>().Select(e => e.Message)
            .ShouldContain("new-boom");
        ex.InnerExceptions.OfType<InvalidOperationException>().Select(e => e.Message)
            .ShouldContain("rollback-boom");

        vm.IsOpen.ShouldBeFalse();
        router.IsOpen("git").ShouldBeFalse();
        router.Get("git").ShouldBeNull();
        routedEvents.Any(e => e.PanelId == "git" && e.NewZone is null).ShouldBeTrue();
        persistence.SaveCallCount.ShouldBeGreaterThan(savesBefore);
    }

    [Fact]
    public void Show_MixingAllowMultiInstance_ThrowsBothDirections()
    {
        var (router1, _, _) = BuildRouter((PanelZone.Window, PanelScope.AppShell));
        var a = new StubPanelableViewModel("viewer");
        var b = new StubPanelableViewModel("viewer");

        router1.Show(a, new PanelShowOptions(Zone: PanelZone.Window, AllowMultiInstance: false));
        var ex1 = Should.Throw<InvalidOperationException>(() =>
            router1.Show(b, new PanelShowOptions(Zone: PanelZone.Window, AllowMultiInstance: true)));
        ex1.Message.ShouldContain("viewer");

        var (router2, _, _) = BuildRouter((PanelZone.Window, PanelScope.AppShell));
        var c = new StubPanelableViewModel("viewer");
        var d = new StubPanelableViewModel("viewer");

        router2.Show(c, new PanelShowOptions(Zone: PanelZone.Window, AllowMultiInstance: true));
        var ex2 = Should.Throw<InvalidOperationException>(() =>
            router2.Show(d, new PanelShowOptions(Zone: PanelZone.Window, AllowMultiInstance: false)));
        ex2.Message.ShouldContain("viewer");
    }

    [Fact]
    public void Move_WithOptions_RefreshesMountOptions()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPlaceablePanelViewModel("git", PanelZone.RightDock);
        router.Show(vm, new PanelShowOptions(AlwaysOnTop: false));

        router.Move("git", PanelZone.Window, new PanelShowOptions(AlwaysOnTop: true));

        var winSurface = surfaces[(PanelZone.Window, PanelScope.AppShell)];
        winSurface.Mounted.ShouldBeSameAs(vm);
        winSurface.LastMountOptions.ShouldNotBeNull();
        winSurface.LastMountOptions!.AlwaysOnTop.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_StopsRoutingDismissEvents_AndPublicMethodsThrow()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("help");
        router.Show(vm);

        router.Dispose();

        // Dismiss after dispose should be a no-op (no exception, no state corruption).
        Should.NotThrow(() =>
            surfaces[(PanelZone.Popup, PanelScope.AppShell)].RaiseDismiss("help"));

        // Further public calls throw ObjectDisposedException.
        Should.Throw<ObjectDisposedException>(() => router.Show(new StubPanelableViewModel("x")));
        Should.Throw<ObjectDisposedException>(() => router.Move("help", PanelZone.Window));
        Should.Throw<ObjectDisposedException>(() => router.Close("help"));
    }

    [Fact]
    public void Show_PersistsSnapshot_ContainingOnlyAffectedScope()
    {
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var persistence = new InMemoryPanelPersistence();
        var surfaces = new List<IPanelSurface>
        {
            new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabA },
            new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabB },
        };
        var router = new PanelRouter(surfaces, persistence, new SynchronousDispatcherService());

        router.Show(new StubPanelableViewModel("p1"),
            new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(new StubPanelableViewModel("p2"),
            new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        var snapA = persistence.Load(tabA);
        var snapB = persistence.Load(tabB);

        snapA.Entries.Select(e => e.PanelId).ShouldBe(new[] { "p1" });
        snapB.Entries.Select(e => e.PanelId).ShouldBe(new[] { "p2" });
    }

    [Fact]
    public void VmIsOpenFalse_TriggersRouterClose_AndCleansUpRegistry()
    {
        var (router, persistence, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("help");
        var routedEvents = new List<PanelRoutedEventArgs>();
        router.Routed += (_, e) => routedEvents.Add(e);

        router.Show(vm);

        // Simulate the × button / BasePanelViewModel.CloseCommand path:
        vm.IsOpen = false;

        router.IsOpen("help").ShouldBeFalse();
        surfaces[(PanelZone.Popup, PanelScope.AppShell)].Mounted.ShouldBeNull();
        routedEvents.Last().NewZone.ShouldBeNull();
        persistence.Load(PanelScope.AppShell).Entries.ShouldBeEmpty();
    }

    [Fact]
    public void RouterInitiatedClose_DoesNotReEnter_ViaIsOpenSubscription()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("help");
        var closeEvents = 0;
        router.Routed += (_, e) => { if (e.NewZone is null) closeEvents++; };

        router.Show(vm);
        router.Close("help");

        closeEvents.ShouldBe(1);
        vm.IsOpen.ShouldBeFalse();
        router.IsOpen("help").ShouldBeFalse();
    }

    [Fact]
    public void Dispose_UnsubscribesIsOpenHandlers()
    {
        var (router, _, _) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("help");
        router.Show(vm);

        router.Dispose();

        // After dispose, flipping IsOpen on the VM must not throw (ObjectDisposedException would
        // surface if the router still received the PropertyChanged event and tried to Close).
        Should.NotThrow(() => vm.IsOpen = false);
    }

    [Fact]
    public void VmIsOpenFalse_OnRoutedShow_DoesNotTriggerStrayCloseForOtherPanels()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.Popup, PanelScope.AppShell),
            (PanelZone.RightDock, PanelScope.AppShell));
        var help = new StubPanelableViewModel("help");
        var git = new StubPanelableViewModel("git");
        router.Show(help, new PanelShowOptions(Zone: PanelZone.Popup));
        router.Show(git, new PanelShowOptions(Zone: PanelZone.RightDock));

        help.IsOpen = false;

        router.IsOpen("help").ShouldBeFalse();
        router.IsOpen("git").ShouldBeTrue();
        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(git);
    }

    // ---- Phase 2: WPF Window surface (router-boundary tests) ----

    [Fact]
    public void BuildMountOptions_SetsConfirmOnClose_WhenVmImplementsCloseGuard()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubCloseGuardPanelViewModel("guarded", canClose: true);

        router.Show(vm);

        var surface = surfaces[(PanelZone.Popup, PanelScope.AppShell)];
        surface.LastMountOptions.ShouldNotBeNull();
        surface.LastMountOptions!.ConfirmOnClose.ShouldBeTrue();
    }

    [Fact]
    public void BuildMountOptions_LeavesConfirmOnCloseFalse_WhenVmHasNoCloseGuard()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Popup, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("plain");

        router.Show(vm);

        var surface = surfaces[(PanelZone.Popup, PanelScope.AppShell)];
        surface.LastMountOptions.ShouldNotBeNull();
        surface.LastMountOptions!.ConfirmOnClose.ShouldBeFalse();
    }

    [Fact]
    public void Show_Window_Zone_RegistersPanelAndMounts()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("fileViewer");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window));

        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        vm.DisplayState.ShouldBe(PanelDisplayState.Window);
        vm.IsOpen.ShouldBeTrue();
        router.IsOpen("fileViewer").ShouldBeTrue();
    }

    [Fact]
    public void Window_Zone_AllowMultiInstance_AllowsTwoInstancesOfSamePanelId()
    {
        var (router, _, surfaces) = BuildRouter((PanelZone.Window, PanelScope.AppShell));
        var vm1 = new StubPanelableViewModel("fileViewer");
        var vm2 = new StubPanelableViewModel("fileViewer");

        router.Show(vm1, new PanelShowOptions(Zone: PanelZone.Window, ForceShow: true, AllowMultiInstance: true));
        router.Show(vm2, new PanelShowOptions(Zone: PanelZone.Window, ForceShow: true, AllowMultiInstance: true));

        var surface = surfaces[(PanelZone.Window, PanelScope.AppShell)];
        surface.Mounts.ShouldBe(2);
        surface.AllMounted.Count.ShouldBe(2);
        surface.AllMounted.ShouldContain(vm1);
        surface.AllMounted.ShouldContain(vm2);
        router.IsOpen("fileViewer").ShouldBeTrue();
        vm1.IsOpen.ShouldBeTrue();
        vm2.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Window_Zone_Close_Unmounts_AndClearsIsOpen()
    {
        var (router, persistence, surfaces) = BuildRouter((PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("fileViewer");
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window));

        router.Close("fileViewer");

        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeNull();
        vm.IsOpen.ShouldBeFalse();
        router.IsOpen("fileViewer").ShouldBeFalse();
        persistence.Load(PanelScope.AppShell).Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Window_Zone_PersistenceRoundTrip_RestoresWindowPanel()
    {
        // Save: open a Window-zone panel against router A
        var persistence = new InMemoryPanelPersistence();
        var dispatcher = new SynchronousDispatcherService();
        var surfaceA = new InMemoryPanelSurface { Zone = PanelZone.Window, Scope = PanelScope.AppShell };
        var routerA = new PanelRouter(new IPanelSurface[] { surfaceA }, persistence, dispatcher);
        var vmA = new StubPanelableViewModel("fileViewer");

        routerA.Show(vmA, new PanelShowOptions(Zone: PanelZone.Window));

        var snapshot = persistence.Load(PanelScope.AppShell);
        snapshot.Entries.ShouldContain(e =>
            e.PanelId == "fileViewer" && e.Zone == PanelZone.Window && e.IsOpen);

        // Restore: new router with same persistence + surface
        var surfaceB = new InMemoryPanelSurface { Zone = PanelZone.Window, Scope = PanelScope.AppShell };
        var routerB = new PanelRouter(new IPanelSurface[] { surfaceB }, persistence, dispatcher);
        var vmB = new StubPanelableViewModel("fileViewer");

        routerB.Restore(PanelScope.AppShell, id => id == "fileViewer" ? vmB : null);

        surfaceB.Mounted.ShouldBeSameAs(vmB);
        vmB.DisplayState.ShouldBe(PanelDisplayState.Window);
        routerB.IsOpen("fileViewer").ShouldBeTrue();
    }
}

/// <summary>
/// Test VM that implements both <see cref="IPanelableViewModel"/> (via <see cref="StubPanelableViewModel"/>)
/// and <see cref="IPanelCloseGuard"/>. The router only probes the interface presence; the veto path
/// is the surface's concern and is covered by FlaUI smoke tests rather than unit tests.
/// </summary>
internal sealed class StubCloseGuardPanelViewModel(string panelId, bool canClose)
    : StubPanelableViewModel(panelId), IPanelCloseGuard
{
    private readonly bool _canClose = canClose;
    public bool CanClose() => _canClose;
}

// ---- Phase 3: Dynamic surface registration, cross-scope Window fallback,
//                Center-origin dock-back bridge, IsActive tracking + persistence ----

#pragma warning disable CS0618 // SetOriginZone is intentionally [Obsolete] during Phase 3.
public partial class PanelRouterTests
{
    // ---- A. Dynamic surface registration ----

    [Fact]
    public void RegisterSurface_AddsSurfaceForZoneAndScope()
    {
        var persistence = new InMemoryPanelPersistence();
        var router = new PanelRouter(Array.Empty<IPanelSurface>(), persistence, new SynchronousDispatcherService());
        var late = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };

        router.RegisterSurface(late);

        var vm = new StubPanelableViewModel("git");
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: PanelScope.AppShell));

        late.Mounted.ShouldBeSameAs(vm);
        router.IsOpen("git").ShouldBeTrue();
    }

    [Fact]
    public void RegisterSurface_DuplicateZoneAndScope_Throws()
    {
        var (router, _, _) = BuildRouter((PanelZone.RightDock, PanelScope.AppShell));
        var dup = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };

        Should.Throw<InvalidOperationException>(() => router.RegisterSurface(dup));
    }

    [Fact]
    public void UnregisterSurface_ClosesAllPanelsInScope_BeforeRemoving()
    {
        var tabA = PanelScope.ForTab("a");
        var persistence = new InMemoryPanelPersistence();
        var router = new PanelRouter(Array.Empty<IPanelSurface>(), persistence, new SynchronousDispatcherService());
        var surface = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabA };
        router.RegisterSurface(surface);

        var vm1 = new StubPanelableViewModel("git");
        var vm2 = new StubPanelableViewModel("explorer");
        router.Show(vm1, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(vm2, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));

        var unmountsBefore = surface.Unmounts;
        router.UnregisterSurface(PanelZone.RightDock, tabA);

        vm1.IsOpen.ShouldBeFalse();
        vm2.IsOpen.ShouldBeFalse();
        (surface.Unmounts - unmountsBefore).ShouldBe(2);
        router.IsOpen("git").ShouldBeFalse();
        router.IsOpen("explorer").ShouldBeFalse();

        // Show in the now-unregistered scope must throw — no surface to mount on.
        Should.Throw<InvalidOperationException>(() =>
            router.Show(new StubPanelableViewModel("git"),
                new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA)));
    }

    [Fact]
    public void UnregisterSurface_UnknownZoneScope_NoOp()
    {
        var (router, _, _) = BuildRouter((PanelZone.RightDock, PanelScope.AppShell));

        Should.NotThrow(() => router.UnregisterSurface(PanelZone.LeftDock, PanelScope.ForTab("ghost")));
    }

    [Fact]
    public void UnregisterSurface_UnsubscribesDismissRequested()
    {
        var tabA = PanelScope.ForTab("a");
        var persistence = new InMemoryPanelPersistence();
        var router = new PanelRouter(Array.Empty<IPanelSurface>(), persistence, new SynchronousDispatcherService());
        var surface = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabA };
        router.RegisterSurface(surface);

        var vm = new StubPanelableViewModel("git");
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));

        router.UnregisterSurface(PanelZone.RightDock, tabA);

        // After unregister, a stray DismissRequested on the (now-detached) surface must not affect
        // the router's internal registry. We can't directly inspect _registry, so we assert no
        // exception occurs and a subsequent registration/show is clean.
        Should.NotThrow(() => surface.RaiseDismiss("git"));

        var fresh = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabA };
        router.RegisterSurface(fresh);
        var vm2 = new StubPanelableViewModel("git");
        router.Show(vm2, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        fresh.Mounted.ShouldBeSameAs(vm2);
    }

    // ---- B. Tab-scope isolation ----

    [Fact]
    public void Show_TwoTabs_SamePanelId_RegistersBothInstances()
    {
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabA),
            (PanelZone.RightDock, tabB));

        var vmA = new StubPanelableViewModel("gitFiles");
        var vmB = new StubPanelableViewModel("gitFiles");

        router.Show(vmA, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(vmB, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        surfaces[(PanelZone.RightDock, tabA)].Mounts.ShouldBe(1);
        surfaces[(PanelZone.RightDock, tabB)].Mounts.ShouldBe(1);
        surfaces[(PanelZone.RightDock, tabA)].Mounted.ShouldBeSameAs(vmA);
        surfaces[(PanelZone.RightDock, tabB)].Mounted.ShouldBeSameAs(vmB);
        router.IsOpen("gitFiles").ShouldBeTrue();
    }

    [Fact]
    public void Close_OneTabScope_LeavesOtherUntouched()
    {
        var tabA = PanelScope.ForTab("a");
        var tabB = PanelScope.ForTab("b");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabA),
            (PanelZone.RightDock, tabB));

        var vmA = new StubPanelableViewModel("gitFiles");
        var vmB = new StubPanelableViewModel("gitFiles");

        router.Show(vmA, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabA));
        router.Show(vmB, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabB));

        // Close hits one arbitrary instance (Phase 0 docs admit this). The other must remain.
        router.Close("gitFiles");

        // Exactly one of the two VMs is now closed; the other is still mounted.
        var aOpen = vmA.IsOpen;
        var bOpen = vmB.IsOpen;
        (aOpen ^ bOpen).ShouldBeTrue();
        router.IsOpen("gitFiles").ShouldBeTrue();

        if (aOpen) surfaces[(PanelZone.RightDock, tabA)].Mounted.ShouldBeSameAs(vmA);
        else surfaces[(PanelZone.RightDock, tabB)].Mounted.ShouldBeSameAs(vmB);
    }

    // ---- C. Cross-scope Move(Window) fallback ----

    [Fact]
    public void Move_ToWindow_FromTabScope_FallsBackToAppShellWindowSurface()
    {
        var tabP = PanelScope.ForTab("p");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabP),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("gitFiles");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));
        surfaces[(PanelZone.RightDock, tabP)].Mounted.ShouldBeSameAs(vm);

        router.Move("gitFiles", PanelZone.Window);

        surfaces[(PanelZone.RightDock, tabP)].Mounted.ShouldBeNull();
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        vm.DisplayState.ShouldBe(PanelDisplayState.Window);
        router.IsOpen("gitFiles").ShouldBeTrue();
    }

    [Fact]
    public void Move_BackFromWindow_ToRightDock_LandsOnOriginalTab()
    {
        var tabP = PanelScope.ForTab("p");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabP),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("gitFiles");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));
        router.Move("gitFiles", PanelZone.Window);

        // Round-trip — the registration's Scope must have stayed at tabP, otherwise this
        // would not resolve and would throw.
        router.Move("gitFiles", PanelZone.RightDock);

        surfaces[(PanelZone.RightDock, tabP)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeNull();
        vm.DisplayState.ShouldBe(PanelDisplayState.Panel);
        vm.PreferredSide.ShouldBe(PanelSide.Right);
    }

    [Fact]
    public void Move_ToNonExistentNonWindowZone_Throws()
    {
        // Verifies only Window gets the cross-scope fallback. Moving from a tab-scoped panel
        // to LeftDock (where no surface exists in that scope) must throw — no general retry.
        var tabP = PanelScope.ForTab("p");
        var (router, _, _) = BuildRouter(
            (PanelZone.RightDock, tabP),
            (PanelZone.LeftDock, PanelScope.AppShell)); // wrong scope on purpose
        var vm = new StubPanelableViewModel("gitFiles");
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));

        Should.Throw<InvalidOperationException>(() => router.Move("gitFiles", PanelZone.LeftDock));
    }

    [Fact]
    public void Close_OfCrossScopeWindowMount_UnmountsTheActualSurface()
    {
        var tabP = PanelScope.ForTab("p");
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, tabP),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("gitFiles");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));
        router.Move("gitFiles", PanelZone.Window);

        var dockUnmountsBefore = surfaces[(PanelZone.RightDock, tabP)].Unmounts;
        var winUnmountsBefore = surfaces[(PanelZone.Window, PanelScope.AppShell)].Unmounts;

        router.Close("gitFiles");

        // The AppShell window surface must take the unmount, not the tab dock surface
        // (which doesn't have the panel mounted anymore).
        (surfaces[(PanelZone.Window, PanelScope.AppShell)].Unmounts - winUnmountsBefore).ShouldBe(1);
        surfaces[(PanelZone.RightDock, tabP)].Unmounts.ShouldBe(dockUnmountsBefore);
        vm.IsOpen.ShouldBeFalse();
        router.IsOpen("gitFiles").ShouldBeFalse();
    }

    // ---- E. IsActive tracking + persistence ----

    [Fact]
    public void PersistScope_EmitsIsActiveTrue_ForCurrentlyActivePanel_OnlyOne()
    {
        var tabP = PanelScope.ForTab("p");
        var persistence = new InMemoryPanelPersistence();
        var surface = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabP };
        var router = new PanelRouter(new IPanelSurface[] { surface }, persistence, new SynchronousDispatcherService());

        var a = new StubPanelableViewModel("a");
        var b = new StubPanelableViewModel("b");
        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));

        var snap = persistence.Load(tabP);
        snap.Entries.Count.ShouldBe(2);
        snap.Entries.Count(e => e.IsActive).ShouldBe(1);
        snap.Entries.Single(e => e.IsActive).PanelId.ShouldBe("b");
    }

    [Fact]
    public void Focus_UpdatesActivePanel()
    {
        var tabP = PanelScope.ForTab("p");
        var persistence = new InMemoryPanelPersistence();
        var surface = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = tabP };
        var router = new PanelRouter(new IPanelSurface[] { surface }, persistence, new SynchronousDispatcherService());

        var a = new StubPanelableViewModel("a");
        var b = new StubPanelableViewModel("b");
        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP));

        // Re-show "a" with ForceShow → Focus path → router updates _activePanel and persists.
        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock, Scope: tabP, ForceShow: true));

        var snap = persistence.Load(tabP);
        snap.Entries.Single(e => e.IsActive).PanelId.ShouldBe("a");
        snap.Entries.Single(e => e.PanelId == "b").IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Move_UpdatesActivePanel_OnSourceAndTarget()
    {
        var persistence = new InMemoryPanelPersistence();
        var dock = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };
        var win = new InMemoryPanelSurface { Zone = PanelZone.Window, Scope = PanelScope.AppShell };
        var router = new PanelRouter(new IPanelSurface[] { dock, win }, persistence, new SynchronousDispatcherService());

        var a = new StubPanelableViewModel("a");
        var b = new StubPanelableViewModel("b");
        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock));
        router.Show(b, new PanelShowOptions(Zone: PanelZone.RightDock));
        // b is now active in RightDock.

        router.Move("a", PanelZone.Window);

        var snap = persistence.Load(PanelScope.AppShell);
        // a is in Window zone and is the active panel of its zone; b is still active in RightDock.
        snap.Entries.Single(e => e.PanelId == "a")
            .ShouldSatisfyAllConditions(
                e => e.Zone.ShouldBe(PanelZone.Window),
                e => e.IsActive.ShouldBeTrue());
        snap.Entries.Single(e => e.PanelId == "b")
            .ShouldSatisfyAllConditions(
                e => e.Zone.ShouldBe(PanelZone.RightDock),
                e => e.IsActive.ShouldBeTrue());
    }

    [Fact]
    public void Unmount_LastPanelInZone_ClearsActive()
    {
        var persistence = new InMemoryPanelPersistence();
        var surface = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };
        var router = new PanelRouter(new IPanelSurface[] { surface }, persistence, new SynchronousDispatcherService());

        var a = new StubPanelableViewModel("a");
        router.Show(a, new PanelShowOptions(Zone: PanelZone.RightDock));

        router.Close("a");

        var snap = persistence.Load(PanelScope.AppShell);
        snap.Entries.ShouldBeEmpty();
        snap.Entries.Any(e => e.IsActive).ShouldBeFalse();
    }
}
#pragma warning restore CS0618

// ---- Phase 4: LastDockedZone, OnOpenedAsync lifecycle, SetOriginZone removal ----

public partial class PanelRouterTests
{
    #region Phase 4

    [Fact]
    public void Center_To_Window_DockBack_RestoresCenter_ViaLastDockedZone()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.Center, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell),
            (PanelZone.RightDock, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("git");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Center));
        router.Move("git", PanelZone.Window);

        // Dock-back: VM raises StateChangeRequested(Panel) with no DockSide. Should land on Center
        // because LastDockedZone snapshotted the Center origin during Move(Window).
        vm.TriggerStateChangeRequest(PanelDisplayState.Panel, side: null);

        surfaces[(PanelZone.Center, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeNull();
        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
        vm.DisplayState.ShouldBe(PanelDisplayState.Panel);
    }

    [Fact]
    public void RightDock_To_Window_DockBack_RestoresRightDock_ViaLastDockedZone()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.LeftDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("git");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock));
        router.Move("git", PanelZone.Window);

        // Dock-back without DockSide info — LastDockedZone (RightDock) wins over default fallback.
        vm.TriggerStateChangeRequest(PanelDisplayState.Panel, side: null);

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.LeftDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeNull();
        vm.PreferredSide.ShouldBe(PanelSide.Right);
    }

    [Fact]
    public void DockBack_NoLastDockedZone_FallsBackToDockSide_Left()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.LeftDock, PanelScope.AppShell),
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        // Panel starts in Window (never docked) — LastDockedZone is null.
        var vm = new StubPanelableViewModel("git");
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window, ForceShow: true));

        vm.TriggerStateChangeRequest(PanelDisplayState.Panel, side: PanelSide.Left);

        surfaces[(PanelZone.LeftDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
    }

    [Fact]
    public void DockBack_NoLastDockedZone_FallsBackToDockSide_Right()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.LeftDock, PanelScope.AppShell),
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new StubPanelableViewModel("git");
        router.Show(vm, new PanelShowOptions(Zone: PanelZone.Window, ForceShow: true));

        vm.TriggerStateChangeRequest(PanelDisplayState.Panel, side: PanelSide.Right);

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        surfaces[(PanelZone.LeftDock, PanelScope.AppShell)].Mounted.ShouldBeNull();
    }

    [Fact]
    public void OnOpenedAsync_FiredAfterShow()
    {
        var (router, _, _) = BuildRouter((PanelZone.RightDock, PanelScope.AppShell));
        var vm = new FakeOpenContextVm("git");
        var marker = new object();

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock, Context: marker));

        vm.OpenedCallCount.ShouldBe(1);
        vm.LastContext.ShouldBeSameAs(marker);
    }

    [Fact]
    public void OnOpenedAsync_FiredAfterMove()
    {
        var (router, _, _) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new FakeOpenContextVm("git");

        router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock));
        vm.OpenedCallCount.ShouldBe(1);

        router.Move("git", PanelZone.Window);

        // Move triggers OnOpenedAsync again after post-mount on the new surface.
        vm.OpenedCallCount.ShouldBe(2);
    }

    [Fact]
    public void OnOpenedAsync_NotFiredDuringRestore()
    {
        var persistence = new InMemoryPanelPersistence();
        var scope = PanelScope.AppShell;
        persistence.Seed(scope, new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, scope, IsOpen: true, IsActive: true),
        }));
        var surfaces = new List<IPanelSurface>
        {
            new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = scope },
        };
        var router = new PanelRouter(surfaces, persistence, new SynchronousDispatcherService());
        var vm = new FakeOpenContextVm("git");

        router.Restore(scope, id => id == "git" ? vm : null);

        router.IsOpen("git").ShouldBeTrue();
        vm.OpenedCallCount.ShouldBe(0);
    }

    [Fact]
    public void OnOpenedAsync_ExceptionDoesNotCrashRouter()
    {
        var (router, _, surfaces) = BuildRouter(
            (PanelZone.RightDock, PanelScope.AppShell),
            (PanelZone.Window, PanelScope.AppShell));
        var vm = new FakeOpenContextVm("git") { ThrowOnOpened = true };

        Should.NotThrow(() => router.Show(vm, new PanelShowOptions(Zone: PanelZone.RightDock)));

        surfaces[(PanelZone.RightDock, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        router.IsOpen("git").ShouldBeTrue();

        // Router stays usable for subsequent Move/Close even after the open-callback threw.
        Should.NotThrow(() => router.Move("git", PanelZone.Window));
        surfaces[(PanelZone.Window, PanelScope.AppShell)].Mounted.ShouldBeSameAs(vm);
        Should.NotThrow(() => router.Close("git"));
        router.IsOpen("git").ShouldBeFalse();
    }

    [Fact]
    public void SetOriginZone_Removed_FromInterface()
    {
        // Compile-time/reflection guard: the Phase 3 SetOriginZone hook is gone.
        typeof(IPanelRouter).GetMethod("SetOriginZone").ShouldBeNull();
    }

    #endregion Phase 4
}

/// <summary>
/// Test VM that implements <see cref="IPanelOpenContext"/> alongside <see cref="IPanelableViewModel"/>.
/// Records call count and the last context payload; optionally throws to exercise the router's
/// fire-and-forget exception swallow.
/// </summary>
internal sealed class FakeOpenContextVm(string panelId)
    : StubPanelableViewModel(panelId), IPanelOpenContext
{
    public int OpenedCallCount { get; private set; }
    public object? LastContext { get; private set; }
    public bool ThrowOnOpened { get; set; }

    public Task OnOpenedAsync(object? context)
    {
        OpenedCallCount++;
        LastContext = context;
        if (ThrowOnOpened) throw new InvalidOperationException("open-boom");
        return Task.CompletedTask;
    }
}
