using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Shouldly;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;
using Xunit;

namespace TerminalHost.Tests.Workspace;

public class TabRouterTests
{
    private readonly ObservableCollection<ITabViewModel> _tabs = new();
    private readonly System.Collections.Generic.List<ITabViewModel> _selectedLog = new();
    private readonly TabRouter _router;

    public TabRouterTests()
    {
        _router = new TabRouter(_tabs, tab => _selectedLog.Add(tab));
    }

    [Fact]
    public void OpenSingleton_CreatesAndSelects_WhenNoneExists()
    {
        _router.Register<FakeTabA>(() => new FakeTabA());

        var tab = _router.OpenSingleton<FakeTabA>();

        _tabs.Count.ShouldBe(1);
        _tabs[0].ShouldBeSameAs(tab);
        _selectedLog.Count.ShouldBe(1);
        _selectedLog[0].ShouldBeSameAs(tab);
    }

    [Fact]
    public void OpenSingleton_FocusesExisting_InsteadOfCreating()
    {
        _router.Register<FakeTabA>(() => new FakeTabA());

        var first = _router.OpenSingleton<FakeTabA>();
        var second = _router.OpenSingleton<FakeTabA>();

        _tabs.Count.ShouldBe(1);
        first.ShouldBeSameAs(second);
        _selectedLog.Count.ShouldBe(2);
        _selectedLog[0].ShouldBeSameAs(first);
        _selectedLog[1].ShouldBeSameAs(first);
    }

    [Fact]
    public void OnCreated_RunsOnce_OnlyOnCreation()
    {
        var counter = 0;
        _router.Register<FakeTabA>(() => new FakeTabA(), _ => counter++);

        _router.OpenSingleton<FakeTabA>();
        counter.ShouldBe(1);

        _router.OpenSingleton<FakeTabA>();
        counter.ShouldBe(1);
    }

    [Fact]
    public void Configure_RunsOnBothFocusAndCreatePaths()
    {
        var counter = 0;
        _router.Register<FakeTabA>(() => new FakeTabA());

        _router.OpenSingleton<FakeTabA>(_ => counter++);
        _router.OpenSingleton<FakeTabA>(_ => counter++);
        _router.OpenSingleton<FakeTabA>(_ => counter++);

        counter.ShouldBe(3);
    }

    [Fact]
    public void Configure_RunsAfterOnCreated_OnCreationPath()
    {
        _router.Register<FakeTabA>(
            factory: () => new FakeTabA(),
            onCreated: t => t.Label = "default");

        var tab = _router.OpenSingleton<FakeTabA>(t => t.Label = "override");

        tab.Label.ShouldBe("override");
    }

    [Fact]
    public void Close_RemovesTab()
    {
        _router.Register<FakeTabA>(() => new FakeTabA());
        _router.OpenSingleton<FakeTabA>();

        _router.Close<FakeTabA>();

        _tabs.ShouldBeEmpty();
        _router.IsOpen<FakeTabA>().ShouldBeFalse();
    }

    [Fact]
    public void Close_IsNoOp_WhenNotOpen()
    {
        // No throw; no registration required (Close doesn't construct).
        Should.NotThrow(() => _router.Close<FakeTabA>());
        _tabs.ShouldBeEmpty();
    }

    [Fact]
    public void IsOpen_ReflectsCurrentState()
    {
        _router.Register<FakeTabA>(() => new FakeTabA());

        _router.IsOpen<FakeTabA>().ShouldBeFalse();

        _router.OpenSingleton<FakeTabA>();
        _router.IsOpen<FakeTabA>().ShouldBeTrue();

        _router.Close<FakeTabA>();
        _router.IsOpen<FakeTabA>().ShouldBeFalse();
    }

    [Fact]
    public void OpenSingleton_Throws_WhenFactoryMissing()
    {
        var ex = Should.Throw<InvalidOperationException>(() => _router.OpenSingleton<FakeTabA>());

        ex.Message.ShouldContain(nameof(FakeTabA));
    }

    [Fact]
    public void RegisteredTypes_AreIsolated()
    {
        _router.Register<FakeTabA>(() => new FakeTabA());
        _router.Register<FakeTabB>(() => new FakeTabB());

        _router.OpenSingleton<FakeTabA>();

        _router.IsOpen<FakeTabA>().ShouldBeTrue();
        _router.IsOpen<FakeTabB>().ShouldBeFalse();
        _tabs.Count.ShouldBe(1);
    }

    [Fact]
    public void Register_ReplacesPriorFactory()
    {
        // TabRouter.Register uses dictionary indexer assignment, so re-registering
        // overwrites the prior factory (last writer wins).
        _router.Register<FakeTabA>(() => new FakeTabA { Label = "first" });
        _router.Register<FakeTabA>(() => new FakeTabA { Label = "second" });

        var tab = _router.OpenSingleton<FakeTabA>();

        tab.Label.ShouldBe("second");
    }

    // Minimal stub of ITabViewModel for routing tests. The router never inspects
    // these members; it only deals in identity + type.
    private class FakeTabA : ITabViewModel
    {
        public string Label { get; set; } = "";
        public string Title => nameof(FakeTabA);
        public string TabIcon => "A";
        public string WorkingDirectory => "";
        public bool IsCloseable => true;
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
    }

    private class FakeTabB : ITabViewModel
    {
        public string Title => nameof(FakeTabB);
        public string TabIcon => "B";
        public string WorkingDirectory => "";
        public bool IsCloseable => true;
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
    }
}
