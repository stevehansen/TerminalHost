using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using TerminalHost.Core.ViewModels;
using TerminalHost.Core.Workspace;

namespace TerminalHost.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    private sealed class Harness
    {
        public Mock<ICommandPalette> Palette { get; } = new(MockBehavior.Strict);
        public Mock<IProfileRegistry> ProfileRegistry { get; } = new(MockBehavior.Strict);
        public Mock<IClaudeCommandService> ClaudeService { get; } = new(MockBehavior.Strict);
        public Mock<IConfigurationService> Config { get; } = new(MockBehavior.Strict);
        public Mock<IDispatcherService> Dispatcher { get; } = new(MockBehavior.Strict);
        public AppConfiguration AppConfig { get; } = new();
        public List<Profile> Profiles { get; } = new();
        public List<ClaudeCommand> ClaudeCommands { get; } = new();
        public List<Profile> OpenedProfiles { get; } = new();
        public List<ClaudeCommand> ExecutedClaude { get; } = new();
        public List<string?> WorkingDirCalls { get; } = new();
        public string? WorkingDir { get; set; }
        public int FilterCallCount { get; private set; }

        public CommandPaletteViewModel Build()
        {
            Dispatcher.Setup(d => d.BeginInvoke(It.IsAny<Action>())).Callback<Action>(a => a());

            ProfileRegistry.Setup(p => p.Profiles).Returns(() => Profiles);
            ClaudeService.SetupAdd(c => c.CommandsChanged += It.IsAny<EventHandler>());
            ClaudeService.SetupRemove(c => c.CommandsChanged -= It.IsAny<EventHandler>());
            ClaudeService.Setup(c => c.GetAllCommands(It.IsAny<string?>())).Returns(() => ClaudeCommands);

            Config.Setup(c => c.Load(It.IsAny<string?>())).Returns(AppConfig);
            Config.Setup(c => c.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()));

            return new CommandPaletteViewModel(
                Palette.Object,
                ProfileRegistry.Object,
                ClaudeService.Object,
                Config.Object,
                Dispatcher.Object,
                () => { WorkingDirCalls.Add(WorkingDir); return WorkingDir; },
                p => OpenedProfiles.Add(p),
                c => ExecutedClaude.Add(c));
        }

        public void StubFilter(params PaletteCommand[] commands)
        {
            Palette.Setup(p => p.Filter(It.IsAny<string>()))
                .Callback(() => FilterCallCount++)
                .Returns(commands);
        }

        public void StubFilterFor(string query, params PaletteCommand[] commands)
        {
            Palette.Setup(p => p.Filter(query))
                .Callback(() => FilterCallCount++)
                .Returns(commands);
        }
    }

    private static PaletteCommand MakeCommand(string id, string name, Action? execute = null) => new()
    {
        Id = id,
        Name = name,
        Execute = execute ?? (() => { }),
    };

    private static ClaudeCommand MakeClaudeCommand(string id, string name, ClaudeCommandSource source, string? pluginName = null) => new()
    {
        Id = id,
        Name = name,
        FilePath = $"/path/{name}.md",
        Source = source,
        PluginName = pluginName,
    };

    // -------- Construction + lifecycle --------

    [Fact]
    public void Ctor_SubscribesToCommandsChanged_RefilterRunsOnEvent()
    {
        var h = new Harness();
        var cmd = MakeCommand("c1", "C1");
        h.StubFilter(cmd);

        var vm = h.Build();

        h.ClaudeService.Raise(c => c.CommandsChanged += null, EventArgs.Empty);

        vm.Filtered.Count.ShouldBe(1);
        vm.Filtered[0].Id.ShouldBe("c1");
        h.Dispatcher.Verify(d => d.BeginInvoke(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void Dispose_UnsubscribesFromCommandsChanged()
    {
        var h = new Harness();
        h.StubFilter();
        var vm = h.Build();

        // Raise once before dispose to confirm wiring works
        h.ClaudeService.Raise(c => c.CommandsChanged += null, EventArgs.Empty);
        h.Dispatcher.Verify(d => d.BeginInvoke(It.IsAny<Action>()), Times.Exactly(1));

        vm.Dispose();

        // Raise after dispose — should NOT trigger refilter
        h.ClaudeService.Raise(c => c.CommandsChanged += null, EventArgs.Empty);
        h.Dispatcher.Verify(d => d.BeginInvoke(It.IsAny<Action>()), Times.Exactly(1));
    }

    // -------- Refilter --------

    [Fact]
    public void Refilter_StaticCommandsFlowThrough_SortedByName()
    {
        var h = new Harness();
        var cmdA = MakeCommand("id-a", "Bravo");
        var cmdB = MakeCommand("id-b", "Alpha");
        h.StubFilterFor("foo", cmdA, cmdB);

        var vm = h.Build();
        vm.SearchText = "foo";

        vm.Filtered.Count.ShouldBe(2);
        // empty MRU -> sorted by name alphabetically
        vm.Filtered[0].Name.ShouldBe("Alpha");
        vm.Filtered[1].Name.ShouldBe("Bravo");
        h.Palette.Verify(p => p.Filter("foo"), Times.Once);
    }

    [Fact]
    public void Refilter_AppendsProfileLauncherCommand()
    {
        var h = new Harness();
        h.StubFilter();
        h.Profiles.Add(new Profile { Id = "p1", Name = "MyProfile", Command = "cmd", Icon = "🚀" });

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Filtered.Count.ShouldBe(1);
        vm.Filtered[0].Id.ShouldBe("launch-profile-p1");
        vm.Filtered[0].Name.ShouldBe("Launch: MyProfile");
        vm.Filtered[0].Icon.ShouldBe("🚀");
        vm.Filtered[0].Category.ShouldBe("Profile");
    }

    [Fact]
    public void Refilter_ProfileLauncherExecute_CallsOpenProfileTabWithOriginalProfile()
    {
        var h = new Harness();
        h.StubFilter();
        var profile = new Profile { Id = "p1", Name = "MyProfile", Command = "cmd" };
        h.Profiles.Add(profile);

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Filtered.Count.ShouldBe(1);
        vm.Filtered[0].Execute();

        h.OpenedProfiles.Count.ShouldBe(1);
        h.OpenedProfiles[0].ShouldBeSameAs(profile);
    }

    [Fact]
    public void Refilter_ClaudeCommands_ResolveCategoriesPerSource()
    {
        var h = new Harness();
        h.StubFilter();
        h.ClaudeCommands.Add(MakeClaudeCommand("g1", "global1", ClaudeCommandSource.Global));
        h.ClaudeCommands.Add(MakeClaudeCommand("p1", "project1", ClaudeCommandSource.Project));
        h.ClaudeCommands.Add(MakeClaudeCommand("pl1", "plug1", ClaudeCommandSource.Plugin, pluginName: "foo"));

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Filtered.Count.ShouldBe(3);
        var byId = vm.Filtered.ToDictionary(c => c.Id);
        byId["claude-cmd-g1"].Category.ShouldBe("Claude (Global)");
        byId["claude-cmd-p1"].Category.ShouldBe("Claude (Project)");
        byId["claude-cmd-pl1"].Category.ShouldBe("Claude (Plugin: foo)");
    }

    [Fact]
    public void Refilter_ClaudeCommandExecute_CallsExecuteClaudeCommandCallback()
    {
        var h = new Harness();
        h.StubFilter();
        var claude = MakeClaudeCommand("g1", "global1", ClaudeCommandSource.Global);
        h.ClaudeCommands.Add(claude);

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Filtered.Count.ShouldBe(1);
        vm.Filtered[0].Execute();

        h.ExecutedClaude.Count.ShouldBe(1);
        h.ExecutedClaude[0].ShouldBeSameAs(claude);
    }

    [Fact]
    public void Refilter_InvokesWorkingDirectoryCallbackAndPassesItToClaudeService()
    {
        var h = new Harness();
        h.StubFilter();
        h.WorkingDir = @"P:\proj";

        var vm = h.Build();
        vm.IsOpen = true;

        h.WorkingDirCalls.Count.ShouldBeGreaterThanOrEqualTo(1);
        h.WorkingDirCalls.Last().ShouldBe(@"P:\proj");
        h.ClaudeService.Verify(c => c.GetAllCommands(@"P:\proj"), Times.AtLeastOnce);
    }

    [Fact]
    public void Refilter_EmptySearch_MatchesAllSources()
    {
        var h = new Harness();
        var cmdA = MakeCommand("cmd-a", "AlphaCmd");
        h.StubFilter(cmdA);
        h.Profiles.Add(new Profile { Id = "p1", Name = "MyProfile", Command = "cmd" });
        h.ClaudeCommands.Add(MakeClaudeCommand("g1", "global1", ClaudeCommandSource.Global));

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Filtered.Count.ShouldBe(3);
    }

    [Fact]
    public void Refilter_ProfileMatchesViaLaunchKeyword()
    {
        var h = new Harness();
        h.StubFilter();
        h.Profiles.Add(new Profile { Id = "p1", Name = "MyProfile", Command = "cmd" });

        var vm = h.Build();
        vm.SearchText = "launch";

        vm.Filtered.ShouldContain(c => c.Id == "launch-profile-p1");
    }

    [Fact]
    public void Refilter_MruSortTakesPrecedenceOverName()
    {
        var h = new Harness();
        var cmdA = MakeCommand("cmd-a", "A");
        var cmdB = MakeCommand("cmd-b", "B");
        h.StubFilter(cmdA, cmdB);
        h.AppConfig.CommandPaletteMru.Add("cmd-b");

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Filtered.Count.ShouldBe(2);
        vm.Filtered[0].Id.ShouldBe("cmd-b");
        vm.Filtered[1].Id.ShouldBe("cmd-a");
    }

    [Fact]
    public void Refilter_FirstFilteredCommandAutoSelected()
    {
        var h = new Harness();
        var cmdA = MakeCommand("cmd-a", "A");
        var cmdB = MakeCommand("cmd-b", "B");
        h.StubFilter(cmdA, cmdB);

        var vm = h.Build();
        vm.IsOpen = true;

        vm.Selected.ShouldNotBeNull();
        vm.Selected!.Id.ShouldBe("cmd-a");
    }

    [Fact]
    public void Refilter_EmptyList_SelectedIsNull()
    {
        var h = new Harness();
        h.StubFilter();

        var vm = h.Build();
        vm.SearchText = "anything";

        vm.Filtered.ShouldBeEmpty();
        vm.Selected.ShouldBeNull();
    }

    // -------- RecordMru (via ExecuteSelected) --------

    [Fact]
    public void RecordMru_FirstRecord_AddsToFrontOfEmptyList()
    {
        var h = new Harness();
        var executed = false;
        var cmd = MakeCommand("cmd-1", "C1", () => executed = true);
        h.StubFilter(cmd);
        var vm = h.Build();
        vm.IsOpen = true;
        vm.Selected = cmd;

        vm.ExecuteSelectedCommand.Execute(null);

        executed.ShouldBeTrue();
        h.AppConfig.CommandPaletteMru.ShouldBe(new[] { "cmd-1" });
        h.Config.Verify(c => c.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void RecordMru_ReRecordSameId_MovesToFront()
    {
        var h = new Harness();
        var cmd = MakeCommand("c", "C");
        h.StubFilter(cmd);
        h.AppConfig.CommandPaletteMru.AddRange(new[] { "a", "b", "c" });

        var vm = h.Build();
        vm.IsOpen = true;
        vm.Selected = cmd;

        vm.ExecuteSelectedCommand.Execute(null);

        h.AppConfig.CommandPaletteMru.ShouldBe(new[] { "c", "a", "b" });
    }

    [Fact]
    public void RecordMru_CapsAt30()
    {
        var h = new Harness();
        var cmd = MakeCommand("new", "New");
        h.StubFilter(cmd);
        for (var i = 0; i < 30; i++)
            h.AppConfig.CommandPaletteMru.Add($"existing-{i}");

        var vm = h.Build();
        vm.IsOpen = true;
        vm.Selected = cmd;

        vm.ExecuteSelectedCommand.Execute(null);

        h.AppConfig.CommandPaletteMru.Count.ShouldBe(30);
        h.AppConfig.CommandPaletteMru[0].ShouldBe("new");
        h.AppConfig.CommandPaletteMru[29].ShouldBe("existing-28");
    }

    // -------- ExecuteSelected --------

    [Fact]
    public void ExecuteSelected_NullSelected_IsNoOp()
    {
        var h = new Harness();
        h.StubFilter();
        var vm = h.Build();
        vm.IsOpen = true;
        vm.Selected = null;

        vm.ExecuteSelectedCommand.Execute(null);

        h.Config.Verify(c => c.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Never);
        vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void ExecuteSelected_RecordsMruClosesPaletteAndExecutesCommand()
    {
        var h = new Harness();
        var executed = 0;
        var cmd = MakeCommand("cmd-x", "X", () => executed++);
        h.StubFilter(cmd);

        var vm = h.Build();
        vm.IsOpen = true; // triggers refilter with first selected
        vm.Selected.ShouldNotBeNull();
        vm.Selected!.Id.ShouldBe("cmd-x");

        vm.ExecuteSelectedCommand.Execute(null);

        executed.ShouldBe(1);
        vm.IsOpen.ShouldBeFalse();
        h.Config.Verify(c => c.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
        h.AppConfig.CommandPaletteMru[0].ShouldBe("cmd-x");
    }

    // -------- Partial change handlers --------

    [Fact]
    public void OnSearchTextChanged_TriggersRefilterWithNewQuery()
    {
        var h = new Harness();
        h.Palette.Setup(p => p.Filter("")).Returns(Array.Empty<PaletteCommand>());
        var fooCmd = MakeCommand("foo", "Foo");
        h.Palette.Setup(p => p.Filter("foo")).Returns(new[] { fooCmd });

        var vm = h.Build();
        vm.Filtered.ShouldBeEmpty();

        vm.SearchText = "foo";

        vm.Filtered.Count.ShouldBe(1);
        vm.Filtered[0].Id.ShouldBe("foo");
        h.Palette.Verify(p => p.Filter("foo"), Times.Once);
    }

    [Fact]
    public void OnIsOpenChanged_True_ClearsSearchRefiltersAndSelectsFirst()
    {
        var h = new Harness();
        var cmdA = MakeCommand("a", "A");
        h.Palette.Setup(p => p.Filter("")).Returns(new[] { cmdA });
        h.Palette.Setup(p => p.Filter("stale")).Returns(Array.Empty<PaletteCommand>());

        var vm = h.Build();
        vm.SearchText = "stale";
        vm.Selected = null;

        vm.IsOpen = true;

        vm.SearchText.ShouldBe("");
        vm.Selected.ShouldNotBeNull();
        vm.Selected!.Id.ShouldBe("a");
        h.Palette.Verify(p => p.Filter(""), Times.AtLeastOnce);
    }

    [Fact]
    public void OnIsOpenChanged_False_DoesNotRefilter()
    {
        var h = new Harness();
        h.Palette.Setup(p => p.Filter(It.IsAny<string>()))
            .Callback<string>(_ => { /* counted via FilterCallCount */ })
            .Returns(Array.Empty<PaletteCommand>());

        var vm = h.Build();

        // Capture baseline count after ctor (no refilter happens in ctor; defensive)
        var baselineFilterCalls = h.Palette.Invocations.Count(i => i.Method.Name == nameof(ICommandPalette.Filter));

        vm.IsOpen = false;

        var afterFilterCalls = h.Palette.Invocations.Count(i => i.Method.Name == nameof(ICommandPalette.Filter));
        afterFilterCalls.ShouldBe(baselineFilterCalls);
    }
}
