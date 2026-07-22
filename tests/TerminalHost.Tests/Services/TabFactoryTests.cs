using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.Tests.Services;

public class TabFactoryTests
{
    private readonly Mock<IStatisticsService> _statistics = new();
    private readonly Mock<IGitStatusService> _gitStatus = new();
    private readonly Mock<IToastService> _toasts = new();
    private readonly Mock<ITaskService> _tasks = new();
    private readonly Mock<IPanelRouter> _panelRouter = new();

    public TabFactoryTests()
    {
        _tasks.Setup(t => t.GetAllTasks()).Returns([]);
    }

    private TerminalPair BuildPair() =>
        new("P:\\Repo", new Profile(), new Profile(), _statistics.Object);

    private static AiAssistant SampleAssistant() => new()
    {
        Id = "claude",
        Name = "Claude Code",
        Command = "claude.exe",
        Icon = "Claude",
        Enabled = true,
        IsDefault = true,
    };

    private TabFactory BuildFactory(bool registerTaskService = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_statistics.Object);
        services.AddSingleton(_gitStatus.Object);
        services.AddSingleton(_toasts.Object);
        services.AddSingleton(_panelRouter.Object);
        if (registerTaskService) services.AddSingleton(_tasks.Object);
        return new TabFactory(services.BuildServiceProvider());
    }

    [Fact]
    public void CreateTerminalPairTab_PropagatesInputs()
    {
        var factory = BuildFactory();
        var pair = BuildPair();
        var assistant = SampleAssistant();

        var vm = factory.CreateTerminalPairTab(pair, assistant, [assistant], "💻", duplicateIndex: 2);

        vm.ShouldNotBeNull();
        vm.Pair.ShouldBe(pair);
        vm.ActiveAiAssistant.ShouldBe(assistant);
        vm.ShellIcon.ShouldBe("💻");
        vm.DuplicateIndex.ShouldBe(2);
    }

    [Fact]
    public void CreateTerminalPairTab_SucceedsWhenTaskServiceMissing()
    {
        // ITaskService is optional — factory must resolve it via GetService (nullable),
        // not GetRequiredService, so construction succeeds when no implementation is registered.
        var factory = BuildFactory(registerTaskService: false);
        var pair = BuildPair();
        var assistant = SampleAssistant();

        var vm = factory.CreateTerminalPairTab(pair, assistant, [assistant], "💻", 0);

        vm.ShouldNotBeNull();
    }

    [Fact]
    public void CreateTerminalPairTab_PopulatesAvailableAssistants()
    {
        var factory = BuildFactory();
        var pair = BuildPair();
        var claude = SampleAssistant();
        var gemini = new AiAssistant { Id = "gemini", Name = "Gemini", Command = "gemini", Icon = "G", Enabled = true };

        var vm = factory.CreateTerminalPairTab(pair, claude, [claude, gemini], "💻", 0);

        vm.AvailableAiAssistants.Count.ShouldBe(2);
        vm.AvailableAiAssistants.ShouldContain(claude);
        vm.AvailableAiAssistants.ShouldContain(gemini);
    }
}
