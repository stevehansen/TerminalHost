using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.Tests.Services;

public class WorkspaceStateStoreTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IStatisticsService> _statistics = new();
    private AppConfiguration _saved = new();

    public WorkspaceStateStoreTests()
    {
        _config.Setup(c => c.Load(It.IsAny<string?>())).Returns(() => new AppConfiguration());
        _config.Setup(c => c.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()))
            .Callback<AppConfiguration, string?>((cfg, _) => _saved = cfg);
    }

    private TerminalPairTabViewModel BuildProjectTab(string workingDirectory)
    {
        var pair = new TerminalPair(workingDirectory, new Profile(), new Profile(), _statistics.Object);
        var assistant = new AiAssistant
        {
            Id = "claude",
            Name = "Claude Code",
            Command = "claude.exe",
            Icon = "Claude",
            Enabled = true,
            IsDefault = true,
        };
        var tasks = new Mock<ITaskService>();
        tasks.Setup(t => t.GetAllTasks()).Returns([]);
        return new TerminalPairTabViewModel(
            pair, assistant, [assistant], "💻",
            _statistics.Object, Mock.Of<IGitStatusService>(), Mock.Of<IToastService>(),
            duplicateIndex: 0, taskService: tasks.Object);
    }

    [Fact]
    public void SaveOpenFolders_PersistsProjectFolders()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");
        var b = BuildProjectTab("P:\\RepoB");

        store.SaveOpenFolders([a, b], selectedTab: a);

        _saved.OpenFolders.ShouldBe(["P:\\RepoA", "P:\\RepoB"]);
    }

    [Fact]
    public void SaveOpenFolders_ProjectTab_RecordsProjectKindAndWorkingDirectory()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var tab = BuildProjectTab("P:\\RepoA");

        store.SaveOpenFolders([tab], selectedTab: tab);

        _saved.LastSelectedTabType.ShouldBe("Project");
        _saved.LastSelectedFolder.ShouldBe("P:\\RepoA");
    }

    [Fact]
    public void SaveOpenFolders_NonProjectSelection_FallsBackToFirstOpenFolder()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var projectTab = BuildProjectTab("P:\\RepoA");

        // A tab that is not one of the five dispatched types — default branch applies.
        var otherTab = new Mock<ITabViewModel>().Object;

        store.SaveOpenFolders([projectTab], selectedTab: otherTab);

        _saved.LastSelectedTabType.ShouldBe("Project");
        _saved.LastSelectedFolder.ShouldBe("P:\\RepoA");
    }

    [Fact]
    public void SaveOpenFolders_NullSelection_FallsBackToFirstOpenFolder()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var projectTab = BuildProjectTab("P:\\RepoA");

        store.SaveOpenFolders([projectTab], selectedTab: null);

        _saved.LastSelectedTabType.ShouldBe("Project");
        _saved.LastSelectedFolder.ShouldBe("P:\\RepoA");
    }

    [Fact]
    public void SaveOpenFolders_NoProjectTabs_LastFolderIsNull()
    {
        var store = new WorkspaceStateStore(_config.Object);

        store.SaveOpenFolders([], selectedTab: null);

        _saved.OpenFolders.ShouldBeEmpty();
        _saved.LastSelectedFolder.ShouldBeNull();
    }

    [Fact]
    public void SaveOpenFolders_OnlySavesOnceRegardlessOfTabCount()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");
        var b = BuildProjectTab("P:\\RepoB");
        var c = BuildProjectTab("P:\\RepoC");

        store.SaveOpenFolders([a, b, c], selectedTab: b);

        _config.Verify(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
        _config.Verify(x => x.Load(It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void FindLastSelectedTab_ProjectKind_ReturnsTabWithMatchingFolder()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");
        var b = BuildProjectTab("P:\\RepoB");

        var result = store.FindLastSelectedTab([a, b], lastTabType: "Project", lastSelectedFolder: "P:\\RepoB");

        result.ShouldBe(b);
    }

    [Fact]
    public void FindLastSelectedTab_ProjectKind_MatchIsCaseInsensitive()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");

        var result = store.FindLastSelectedTab([a], lastTabType: "Project", lastSelectedFolder: "p:\\repoa");

        result.ShouldBe(a);
    }

    [Fact]
    public void FindLastSelectedTab_ProjectKind_NoMatch_ReturnsNull()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");

        var result = store.FindLastSelectedTab([a], lastTabType: "Project", lastSelectedFolder: "P:\\Missing");

        result.ShouldBeNull();
    }

    [Fact]
    public void FindLastSelectedTab_ProjectKind_EmptyFolder_ReturnsNull()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");

        var result = store.FindLastSelectedTab([a], lastTabType: "Project", lastSelectedFolder: null);

        result.ShouldBeNull();
    }

    [Fact]
    public void FindLastSelectedTab_UnknownKind_FallsThroughToProjectMatch()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");

        var result = store.FindLastSelectedTab([a], lastTabType: "WhoKnows", lastSelectedFolder: "P:\\RepoA");

        result.ShouldBe(a);
    }

    [Fact]
    public void FindLastSelectedTab_NullKind_FallsThroughToProjectMatch()
    {
        var store = new WorkspaceStateStore(_config.Object);
        var a = BuildProjectTab("P:\\RepoA");

        var result = store.FindLastSelectedTab([a], lastTabType: null, lastSelectedFolder: "P:\\RepoA");

        result.ShouldBe(a);
    }
}
