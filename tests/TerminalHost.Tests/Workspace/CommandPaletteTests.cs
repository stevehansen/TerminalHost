using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;
using Xunit;

namespace TerminalHost.Tests.Workspace;

public class CommandPaletteTests
{
    private static PaletteCommand MakeCmd(
        string id,
        string name,
        Action? execute = null,
        string? description = null,
        string category = "General",
        Func<bool>? canExecute = null)
    {
        return new PaletteCommand
        {
            Id = id,
            Name = name,
            Description = description,
            Category = category,
            Execute = execute ?? (() => { }),
            CanExecute = canExecute,
        };
    }

    [Fact]
    public void Commands_AggregatesFromMultipleProviders()
    {
        var p1 = new FakeProvider(MakeCmd("a1", "A1"), MakeCmd("a2", "A2"));
        var p2 = new FakeProvider(MakeCmd("b1", "B1"), MakeCmd("b2", "B2"));
        var palette = new CommandPalette(new[] { p1, p2 }, new FakeContext());

        palette.Commands.Count.ShouldBe(4);
        palette.Commands.Select(c => c.Id).ShouldBe(new[] { "a1", "a2", "b1", "b2" });
    }

    [Fact]
    public void Commands_IsEmpty_WhenNoProvidersOrRegistrations()
    {
        var palette = new CommandPalette(Array.Empty<ICommandProvider>(), new FakeContext());

        palette.Commands.ShouldBeEmpty();
        palette.Filter("").ShouldBeEmpty();
    }

    [Fact]
    public void Filter_MatchesNameSubstring_CaseInsensitive()
    {
        var palette = new CommandPalette(
            new[] { new FakeProvider(
                MakeCmd("1", "Open Settings"),
                MakeCmd("2", "Close Tab"),
                MakeCmd("3", "Git Status")) },
            new FakeContext());

        var setResult = palette.Filter("set");
        setResult.Count.ShouldBe(1);
        setResult[0].Name.ShouldBe("Open Settings");

        var statusResult = palette.Filter("STATUS");
        statusResult.Count.ShouldBe(1);
        statusResult[0].Name.ShouldBe("Git Status");
    }

    [Fact]
    public void Filter_MatchesDescription()
    {
        var palette = new CommandPalette(
            new[] { new FakeProvider(
                MakeCmd("1", "X", description: "Settings editor"),
                MakeCmd("2", "Y", description: "Something else")) },
            new FakeContext());

        var result = palette.Filter("settings");

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("1");
    }

    [Fact]
    public void Filter_MatchesCategory()
    {
        var palette = new CommandPalette(
            new[] { new FakeProvider(
                MakeCmd("1", "Foo", category: "Git"),
                MakeCmd("2", "Bar", category: "General")) },
            new FakeContext());

        var result = palette.Filter("git");

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("1");
    }

    [Fact]
    public void Filter_EmptyOrNullQuery_ReturnsAllCanExecutePassing()
    {
        var palette = new CommandPalette(
            new[] { new FakeProvider(
                MakeCmd("1", "Alpha"),
                MakeCmd("2", "Beta"),
                MakeCmd("3", "Gamma", canExecute: () => false)) },
            new FakeContext());

        var empty = palette.Filter("");
        empty.Count.ShouldBe(2);
        empty.Select(c => c.Id).ShouldBe(new[] { "1", "2" });

        var nullQuery = palette.Filter(null!);
        nullQuery.Count.ShouldBe(2);
        nullQuery.Select(c => c.Id).ShouldBe(new[] { "1", "2" });
    }

    [Fact]
    public void Filter_GatesOnCanExecute_EvenWhenNameMatches()
    {
        var palette = new CommandPalette(
            new[] { new FakeProvider(
                MakeCmd("1", "Disabled Match", canExecute: () => false)) },
            new FakeContext());

        palette.Filter("disabled").ShouldBeEmpty();
        palette.Filter("match").ShouldBeEmpty();
    }

    [Fact]
    public void Filter_IsCaseInsensitive_AcrossAllThreeFields()
    {
        var palette = new CommandPalette(
            new[] { new FakeProvider(
                MakeCmd("name", "MixEdCaSeName"),
                MakeCmd("desc", "Other", description: "DeScRiPtIoN-HiT"),
                MakeCmd("cat", "Other2", category: "CaTeGoRyHiT")) },
            new FakeContext());

        palette.Filter("mixedcasename").Select(c => c.Id).ShouldBe(new[] { "name" });
        palette.Filter("MIXEDCASENAME").Select(c => c.Id).ShouldBe(new[] { "name" });

        palette.Filter("description-hit").Select(c => c.Id).ShouldBe(new[] { "desc" });
        palette.Filter("DESCRIPTION-HIT").Select(c => c.Id).ShouldBe(new[] { "desc" });

        palette.Filter("categoryhit").Select(c => c.Id).ShouldBe(new[] { "cat" });
        palette.Filter("CATEGORYHIT").Select(c => c.Id).ShouldBe(new[] { "cat" });
    }

    [Fact]
    public async Task InvokeAsync_CallsExecute_ExactlyOnce()
    {
        var count = 0;
        var cmd = MakeCmd("x", "X", execute: () => count++);
        var palette = new CommandPalette(Array.Empty<ICommandProvider>(), new FakeContext());

        await palette.InvokeAsync(cmd);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsCompletedTask()
    {
        var cmd = MakeCmd("x", "X");
        var palette = new CommandPalette(Array.Empty<ICommandProvider>(), new FakeContext());

        var task = palette.InvokeAsync(cmd);
        await task;

        task.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public void Register_AddsCommand()
    {
        var p = new FakeProvider(MakeCmd("1", "One"));
        var palette = new CommandPalette(new[] { p }, new FakeContext());
        var before = palette.Commands.Count;

        var extra = MakeCmd("extra", "ExtraCmd");
        palette.Register(extra);

        palette.Commands.Count.ShouldBe(before + 1);
        palette.Filter("extracmd").Select(c => c.Id).ShouldBe(new[] { "extra" });
    }

    [Fact]
    public void Register_DisposeHandle_RemovesCommand()
    {
        var p = new FakeProvider(MakeCmd("1", "One"));
        var palette = new CommandPalette(new[] { p }, new FakeContext());
        var before = palette.Commands.Count;

        using (palette.Register(MakeCmd("extra", "Extra")))
        {
            palette.Commands.Count.ShouldBe(before + 1);
        }

        palette.Commands.Count.ShouldBe(before);
    }

    [Fact]
    public void Register_DoubleDispose_IsIdempotent()
    {
        var palette = new CommandPalette(Array.Empty<ICommandProvider>(), new FakeContext());
        var handle = palette.Register(MakeCmd("extra", "Extra"));

        handle.Dispose();
        Should.NotThrow(() => handle.Dispose());

        palette.Commands.ShouldBeEmpty();
    }

    [Fact]
    public void Commands_ReflectsProviderChanges_AtQueryTime()
    {
        // CommandPalette.Commands recomputes on every access — no caching.
        // Mutating a provider's backing list between reads should be observable.
        var mutable = new List<PaletteCommand> { MakeCmd("1", "One") };
        var provider = new FakeProvider(mutable);
        var palette = new CommandPalette(new[] { provider }, new FakeContext());

        palette.Commands.Count.ShouldBe(1);

        mutable.Add(MakeCmd("2", "Two"));

        palette.Commands.Count.ShouldBe(2);
        palette.Commands.Select(c => c.Id).ShouldBe(new[] { "1", "2" });
    }

    [Fact]
    public void Context_IsPassedToProviders()
    {
        var ctx = new FakeContext();
        var provider = new FakeProvider(MakeCmd("1", "One"));
        var palette = new CommandPalette(new[] { provider }, ctx);

        _ = palette.Commands;

        provider.LastContext.ShouldBeSameAs(ctx);
        provider.CallCount.ShouldBeGreaterThan(0);
    }

    // ---- Fakes ----

    private sealed class FakeProvider : ICommandProvider
    {
        private readonly List<PaletteCommand> _commands;
        public int CallCount { get; private set; }
        public ICommandContext? LastContext { get; private set; }

        public FakeProvider(params PaletteCommand[] commands)
        {
            _commands = commands.ToList();
        }

        public FakeProvider(List<PaletteCommand> commands)
        {
            _commands = commands;
        }

        public IEnumerable<PaletteCommand> GetCommands(ICommandContext ctx)
        {
            CallCount++;
            LastContext = ctx;
            return _commands;
        }
    }

    private sealed class FakeContext : ICommandContext
    {
        public ITabViewModel? ActiveTab => null;
        public bool HasService<T>() where T : class => false;
    }
}
