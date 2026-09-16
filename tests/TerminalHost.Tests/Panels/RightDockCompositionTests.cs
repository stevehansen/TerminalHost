using Shouldly;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Tests.TestAdapters;

namespace TerminalHost.Tests.Panels;

/// <summary>
/// Unit tests for the pure Q4 "sticky-by-kind" merge rule. No WPF surface involved — the rule
/// decides the merged ordering (per-workspace first, global tail) and the active panel id after
/// a workspace switch.
/// </summary>
public class RightDockCompositionTests
{
    private static IReadOnlyList<IPanelableViewModel> List(params string[] ids) =>
        ids.Select(id => (IPanelableViewModel)new StubPanelableViewModel(id)).ToList();

    [Fact]
    public void Compose_Ordering_PerWorkspaceFirst_GlobalTail()
    {
        var perWorkspace = List("explorer", "tasks");
        var global = List("sessions");

        var (merged, _) = RightDockComposition.Compose(perWorkspace, global, null, null);

        merged.Select(p => p.PanelId).ShouldBe(new[] { "explorer", "tasks", "sessions" });
    }

    [Fact]
    public void Compose_GlobalFocused_StaysFocused_AcrossWorkspaceSwitch()
    {
        // The currently-focused panel is the global Sessions panel. After a workspace switch the
        // per-workspace panels change, but the focused global panel must survive (sticky-by-kind).
        var perWorkspace = List("explorerB");
        var global = List("sessions");

        var (_, activeId) = RightDockComposition.Compose(
            perWorkspace,
            global,
            currentActiveId: "sessions",
            incomingWorkspaceLastActiveId: "explorerB");

        activeId.ShouldBe("sessions");
    }

    [Fact]
    public void Compose_PerWorkspaceFocused_FallsToIncomingWorkspaceLastActive()
    {
        // The previously focused dock tab was a per-workspace panel ("explorerA") that no longer
        // exists after the switch. The active id should become the incoming workspace's last-active.
        var perWorkspace = List("explorerB", "tasksB");
        var global = List("sessions");

        var (_, activeId) = RightDockComposition.Compose(
            perWorkspace,
            global,
            currentActiveId: "explorerA",
            incomingWorkspaceLastActiveId: "tasksB");

        activeId.ShouldBe("tasksB");
    }

    [Fact]
    public void Compose_EmptyIncomingPerWorkspace_FallsToPresentGlobal()
    {
        // Incoming workspace has no per-workspace panels and no remembered last-active. The active
        // id falls through to the first present (global) panel.
        var perWorkspace = List();
        var global = List("sessions");

        var (merged, activeId) = RightDockComposition.Compose(
            perWorkspace,
            global,
            currentActiveId: "explorerA",
            incomingWorkspaceLastActiveId: null);

        merged.Select(p => p.PanelId).ShouldBe(new[] { "sessions" });
        activeId.ShouldBe("sessions");
    }

    [Fact]
    public void Compose_IncomingLastActiveNotPresent_FallsToFirstMerged()
    {
        // The remembered last-active id refers to a panel that is no longer mounted. The rule must
        // not select a non-present id; it falls to the first merged panel (favoring per-workspace).
        var perWorkspace = List("explorerB");
        var global = List("sessions");

        var (_, activeId) = RightDockComposition.Compose(
            perWorkspace,
            global,
            currentActiveId: "explorerA",
            incomingWorkspaceLastActiveId: "ghost");

        activeId.ShouldBe("explorerB");
    }

    [Fact]
    public void Compose_NothingPresent_ActiveIsNull()
    {
        var (merged, activeId) = RightDockComposition.Compose(List(), List(), "anything", "other");

        merged.ShouldBeEmpty();
        activeId.ShouldBeNull();
    }

    [Fact]
    public void Compose_IsIdempotent_RecomputeYieldsSameResult()
    {
        var perWorkspace = List("explorer");
        var global = List("sessions");

        var first = RightDockComposition.Compose(perWorkspace, global, "sessions", null);
        var second = RightDockComposition.Compose(perWorkspace, global, first.ActiveId, null);

        second.ActiveId.ShouldBe(first.ActiveId);
        second.Merged.Select(p => p.PanelId).ShouldBe(first.Merged.Select(p => p.PanelId));
    }

    [Fact]
    public void Compose_PreservesSourceOrderWithinEachKind()
    {
        var perWorkspace = List("a", "b", "c");
        var global = List("x", "y");

        var (merged, _) = RightDockComposition.Compose(perWorkspace, global, null, null);

        merged.Select(p => p.PanelId).ShouldBe(new[] { "a", "b", "c", "x", "y" });
    }
}
