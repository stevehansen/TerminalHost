using Shouldly;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;
using Xunit;

namespace TerminalHost.Tests.Workspace;

public class WorkspaceServiceTests
{
    private readonly WorkspaceService _sut = new();

    [Fact]
    public void Move_ReordersTab()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var c = AddTab("c");

        _sut.Move(0, 2);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { b, c, a });
    }

    [Fact]
    public void Move_ClampsOutOfRangeNewIndex()
    {
        var a = AddTab("a");
        var b = AddTab("b");

        _sut.Move(0, 99);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { b, a });
    }

    [Fact]
    public void Move_IgnoresOutOfRangeOldIndex()
    {
        var a = AddTab("a");
        var b = AddTab("b");

        _sut.Move(5, 0);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { a, b });
    }

    [Fact]
    public void MoveToFront_PromotesTab()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var c = AddTab("c");

        _sut.MoveToFront(c);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { c, a, b });
    }

    [Fact]
    public void MoveToFront_NoOp_WhenAlreadyFirstOrMissing()
    {
        var a = AddTab("a");
        var b = AddTab("b");

        _sut.MoveToFront(a);                  // already first
        _sut.MoveToFront(null);                // null
        _sut.MoveToFront(new FakeTab("z"));    // not in list

        _sut.Tabs.ShouldBe(new ITabViewModel[] { a, b });
    }

    [Fact]
    public void MoveToEnd_DemotesTab()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var c = AddTab("c");

        _sut.MoveToEnd(a);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { b, c, a });
    }

    [Fact]
    public void GetTabsToCloseExcept_OmitsKeepAndUncloseable()
    {
        var a = AddTab("a");
        var b = AddTab("b", closeable: false);
        var c = AddTab("c");

        var result = _sut.GetTabsToCloseExcept(c);

        result.ShouldBe(new ITabViewModel[] { a });
    }

    [Fact]
    public void GetTabsToCloseExcept_EmptyWhenKeepIsNull()
    {
        AddTab("a");
        AddTab("b");

        _sut.GetTabsToCloseExcept(null).ShouldBeEmpty();
    }

    [Fact]
    public void GetTabsToCloseToRightOf_ReturnsOnlyCloseableAfter()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var c = AddTab("c", closeable: false);
        var d = AddTab("d");

        var result = _sut.GetTabsToCloseToRightOf(a);

        result.ShouldBe(new ITabViewModel[] { b, d });
    }

    [Fact]
    public void GetTabsToCloseToRightOf_EmptyForUnknownOrLast()
    {
        var a = AddTab("a");
        var b = AddTab("b");

        _sut.GetTabsToCloseToRightOf(b).ShouldBeEmpty();          // last
        _sut.GetTabsToCloseToRightOf(new FakeTab("z")).ShouldBeEmpty(); // missing
        _sut.GetTabsToCloseToRightOf(null).ShouldBeEmpty();
    }

    [Fact]
    public void SelectedTab_TogglesIsSelectedOnOldAndNew()
    {
        var a = AddTab("a");
        var b = AddTab("b");

        _sut.SelectedTab = a;
        a.IsSelected.ShouldBeTrue();
        b.IsSelected.ShouldBeFalse();

        _sut.SelectedTab = b;
        a.IsSelected.ShouldBeFalse();
        b.IsSelected.ShouldBeTrue();

        _sut.SelectedTab = null;
        a.IsSelected.ShouldBeFalse();
        b.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void SelectedTab_RaisesSelectedTabChangedWithOldAndNew()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var events = new List<(ITabViewModel? Old, ITabViewModel? New)>();
        _sut.SelectedTabChanged += (_, e) => events.Add((e.OldValue, e.NewValue));

        _sut.SelectedTab = a;
        _sut.SelectedTab = b;
        _sut.SelectedTab = null;

        events.ShouldBe(new (ITabViewModel? Old, ITabViewModel? New)[]
        {
            (null, a),
            (a, b),
            (b, null),
        });
    }

    [Fact]
    public void SelectedTab_NoOpAndNoEventWhenUnchanged()
    {
        var a = AddTab("a");
        var fired = 0;
        _sut.SelectedTabChanged += (_, _) => fired++;

        _sut.SelectedTab = a;
        _sut.SelectedTab = a;
        _sut.SelectedTab = a;

        fired.ShouldBe(1);
    }

    [Fact]
    public void RemoveAndPickNext_RemovesTabAndKeepsSelectionWhenUnaffected()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var c = AddTab("c");
        _sut.SelectedTab = a;

        _sut.RemoveAndPickNext(b);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { a, c });
        _sut.SelectedTab.ShouldBe(a);
    }

    [Fact]
    public void RemoveAndPickNext_PicksLastTabWhenSelectedWasRemoved()
    {
        var a = AddTab("a");
        var b = AddTab("b");
        var c = AddTab("c");
        _sut.SelectedTab = b;

        _sut.RemoveAndPickNext(b);

        _sut.Tabs.ShouldBe(new ITabViewModel[] { a, c });
        _sut.SelectedTab.ShouldBe(c);
    }

    [Fact]
    public void RemoveAndPickNext_SelectionBecomesNullWhenLastTabRemoved()
    {
        var a = AddTab("a");
        _sut.SelectedTab = a;

        _sut.RemoveAndPickNext(a);

        _sut.Tabs.ShouldBeEmpty();
        _sut.SelectedTab.ShouldBeNull();
    }

    [Fact]
    public void RemoveAndPickNext_NoOpForNullOrUnknown()
    {
        var a = AddTab("a");
        _sut.SelectedTab = a;
        var fired = 0;
        _sut.SelectedTabChanged += (_, _) => fired++;

        _sut.RemoveAndPickNext(null);
        _sut.RemoveAndPickNext(new FakeTab("z"));

        _sut.Tabs.ShouldBe(new ITabViewModel[] { a });
        _sut.SelectedTab.ShouldBe(a);
        fired.ShouldBe(0);
    }

    private FakeTab AddTab(string label, bool closeable = true)
    {
        var tab = new FakeTab(label) { IsCloseable = closeable };
        _sut.Tabs.Add(tab);
        return tab;
    }

    private sealed class FakeTab : ITabViewModel
    {
        public FakeTab(string label) { Title = label; }
        public string Title { get; }
        public string TabIcon => "T";
        public string WorkingDirectory => "";
        public bool IsCloseable { get; set; } = true;
        public bool IsAnyTerminalActive => false;
        public bool HasUnreadActivity => false;
        public bool IsSelected { get; set; }
        public string DisplayTitle => Title;
        public bool IsVisibleInFocusMode => true;
        public bool ShowActivitySpinner => false;
        public bool ShowCompletedIndicator => false;
        public bool IsWaitingForInput => false;
        public bool ShowWaitingIndicator => false;
        public bool ShowClaudeTaskIndicator => false;
        public bool IsTerminalInitialized => true;
        public bool CanDuplicate => false;
        public void ClearUnreadActivity() { }
        public Task InitializeTerminalsAsync() => Task.CompletedTask;
        public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler? CloseRequested { add { } remove { } }
        public override string ToString() => Title;
    }
}
