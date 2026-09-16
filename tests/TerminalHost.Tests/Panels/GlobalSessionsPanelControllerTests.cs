using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Tests.TestAdapters;

namespace TerminalHost.Tests.Panels;

/// <summary>
/// Tests for the placement controller that owns the single, app-global Sessions panel. Uses the
/// real <see cref="PanelRouter"/> over <see cref="InMemoryPanelSurface"/>s — the controller's whole
/// job is to drive the router toward <c>(RightDock, AppShell)</c> idempotently, and the regression
/// it exists to prevent (cross-scope relocation) is a router behavior, so the real router is used.
/// </summary>
public class GlobalSessionsPanelControllerTests
{
    private const string SessionsPanelId = "sessions";

    private static (GlobalSessionsPanelController Controller,
                    PanelRouter Router,
                    StubPanelableViewModel Vm,
                    InMemoryPanelSurface AppShellSurface,
                    InMemoryPanelPersistence Persistence)
        Build(params (PanelZone Zone, PanelScope Scope)[] extraSurfaces)
    {
        var appShell = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };
        var surfaces = new List<IPanelSurface> { appShell };
        surfaces.AddRange(extraSurfaces.Select(s => new InMemoryPanelSurface { Zone = s.Zone, Scope = s.Scope }));

        var persistence = new InMemoryPanelPersistence();
        var router = new PanelRouter(surfaces, persistence, new SynchronousDispatcherService());
        var vm = new StubPanelableViewModel(SessionsPanelId);
        var controller = new GlobalSessionsPanelController(router, vm);
        return (controller, router, vm, appShell, persistence);
    }

    [Fact]
    public void SetVisible_True_MountsOnAppShellRightDock_AndIsVisibleTrue()
    {
        var (controller, _, vm, appShell, _) = Build();

        controller.SetVisible(true);

        appShell.Mounted.ShouldBeSameAs(vm);
        appShell.Mounts.ShouldBe(1);
        controller.IsVisible.ShouldBeTrue();
        vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void SetVisible_True_Twice_IsIdempotent_OneMount()
    {
        var (controller, _, _, appShell, _) = Build();

        controller.SetVisible(true);
        controller.SetVisible(true);

        appShell.Mounts.ShouldBe(1);
        appShell.Unmounts.ShouldBe(0);
        controller.IsVisible.ShouldBeTrue();
    }

    [Fact]
    public void SetVisible_False_UnmountsAndClosesAndIsVisibleFalse()
    {
        var (controller, router, vm, appShell, _) = Build();
        controller.SetVisible(true);
        var unmountsBefore = appShell.Unmounts;

        controller.SetVisible(false);

        (appShell.Unmounts - unmountsBefore).ShouldBe(1);
        appShell.Mounted.ShouldBeNull();
        controller.IsVisible.ShouldBeFalse();
        router.IsOpen(SessionsPanelId).ShouldBeFalse();
        vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void SetVisible_False_WhenAlreadyHidden_IsNoOp()
    {
        var (controller, _, _, appShell, persistence) = Build();
        var savesBefore = persistence.SaveCallCount;

        controller.SetVisible(false);

        appShell.Unmounts.ShouldBe(0);
        appShell.Mounts.ShouldBe(0);
        controller.IsVisible.ShouldBeFalse();
        persistence.SaveCallCount.ShouldBe(savesBefore);
    }

    [Fact]
    public void SetVisible_ToggleCycle_MountsThenUnmountsCleanly()
    {
        var (controller, router, vm, appShell, _) = Build();

        controller.SetVisible(true);
        controller.SetVisible(false);
        controller.SetVisible(true);

        appShell.Mounts.ShouldBe(2);
        appShell.Mounted.ShouldBeSameAs(vm);
        controller.IsVisible.ShouldBeTrue();
        router.IsOpen(SessionsPanelId).ShouldBeTrue();
    }

    [Fact]
    public void SetVisible_True_WithTabScopedRightDockSurfacePresent_SessionsStaysOnAppShell()
    {
        // Regression for the original bug: the per-tab loop dragged the single Sessions VM into each
        // tab's (RightDock, Tab:x) surface, and the router's single-instance-per-VM rule relocated it
        // tab-to-tab so it vanished from all but one. The controller routes Sessions ONLY to
        // (RightDock, AppShell); the presence of a tab-scoped RightDock surface must never pull it.
        var tab = PanelScope.ForTab("p:\\proj");
        var (controller, router, vm, appShell, _) = Build((PanelZone.RightDock, tab));

        controller.SetVisible(true);

        appShell.Mounted.ShouldBeSameAs(vm, "Sessions must mount on the AppShell surface");
        controller.IsVisible.ShouldBeTrue();

        // The tab-scoped RightDock surface must be untouched — Sessions never relocated to it.
        // (Confirmed via the router's open scope: it remains the AppShell registration.)
        router.IsOpen(SessionsPanelId).ShouldBeTrue();
        // A second SetVisible(true) is idempotent and still does not relocate.
        controller.SetVisible(true);
        appShell.Mounted.ShouldBeSameAs(vm);
        appShell.Mounts.ShouldBe(1);
    }

    [Fact]
    public void PersistenceRoundTrip_AppShellPanels_ContainsRightDockEntry_AndSetVisibleRestoresIt()
    {
        // Open Sessions, save through the real DirectorySettingsPanelPersistence, then reload into a
        // fresh router and confirm AppShellPanels carries the (RightDock, AppShell) entry and that
        // Restore re-mounts the single VM on its AppShell home.
        var config = new AppConfiguration();
        var mock = new Mock<IConfigurationService>();
        mock.Setup(m => m.Load(It.IsAny<string?>())).Returns(config);
        mock.Setup(m => m.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()));
        var persistence = new DirectorySettingsPanelPersistence(mock.Object);
        var dispatcher = new SynchronousDispatcherService();

        var surfaceA = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };
        var routerA = new PanelRouter(new IPanelSurface[] { surfaceA }, persistence, dispatcher);
        var vmA = new StubPanelableViewModel(SessionsPanelId);
        var controllerA = new GlobalSessionsPanelController(routerA, vmA);

        controllerA.SetVisible(true);

        config.Settings.AppShellPanels.ShouldContain(
            e => e.PanelId == SessionsPanelId && e.Zone == "RightDock" && e.IsOpen);

        // Reload: new router + surface over the same persisted config.
        var surfaceB = new InMemoryPanelSurface { Zone = PanelZone.RightDock, Scope = PanelScope.AppShell };
        var routerB = new PanelRouter(new IPanelSurface[] { surfaceB }, persistence, dispatcher);
        var vmB = new StubPanelableViewModel(SessionsPanelId);
        var controllerB = new GlobalSessionsPanelController(routerB, vmB);

        routerB.Restore(PanelScope.AppShell, id => id == SessionsPanelId ? vmB : null);

        surfaceB.Mounted.ShouldBeSameAs(vmB);
        controllerB.IsVisible.ShouldBeTrue();
    }
}
