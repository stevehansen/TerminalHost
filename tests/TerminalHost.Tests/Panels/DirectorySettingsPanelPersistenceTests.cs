using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Panels;

public class DirectorySettingsPanelPersistenceTests
{
    private static (DirectorySettingsPanelPersistence Persistence, AppConfiguration Config, Mock<IConfigurationService> Mock) Build()
    {
        var config = new AppConfiguration();
        var mock = new Mock<IConfigurationService>();
        mock.Setup(m => m.Load(It.IsAny<string?>())).Returns(config);
        mock.Setup(m => m.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()));
        return (new DirectorySettingsPanelPersistence(mock.Object), config, mock);
    }

    [Fact]
    public void Save_AppShellScope_PersistsEntriesToAppConfiguration()
    {
        var (persistence, config, _) = Build();
        var snapshot = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, PanelScope.AppShell, IsOpen: true),
            new PanelLayoutEntry("explorer", PanelZone.LeftDock, PanelScope.AppShell, IsOpen: true),
        });

        persistence.Save(PanelScope.AppShell, snapshot);

        config.Settings.AppShellPanels.Count.ShouldBe(2);
        config.Settings.AppShellPanels.ShouldContain(e => e.PanelId == "git" && e.Zone == "RightDock" && e.IsOpen);
        config.Settings.AppShellPanels.ShouldContain(e => e.PanelId == "explorer" && e.Zone == "LeftDock" && e.IsOpen);
    }

    [Fact]
    public void Save_FiltersOutPopupZoneEntries()
    {
        var (persistence, config, _) = Build();
        var snapshot = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, PanelScope.AppShell, IsOpen: true),
            new PanelLayoutEntry("help", PanelZone.Popup, PanelScope.AppShell, IsOpen: true),
            new PanelLayoutEntry("commandPalette", PanelZone.Popup, PanelScope.AppShell, IsOpen: true),
        });

        persistence.Save(PanelScope.AppShell, snapshot);

        config.Settings.AppShellPanels.Count.ShouldBe(1);
        config.Settings.AppShellPanels[0].PanelId.ShouldBe("git");
        config.Settings.AppShellPanels.ShouldNotContain(e => e.Zone == "Popup");
    }

    [Fact]
    public void Save_AppShellScope_CallsConfigurationSave()
    {
        var (persistence, _, mock) = Build();
        persistence.Save(PanelScope.AppShell, new PanelLayoutSnapshot(Array.Empty<PanelLayoutEntry>()));
        mock.Verify(m => m.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void Save_TabScope_IsNoOp_InPhase1()
    {
        var (persistence, _, mock) = Build();
        var snapshot = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, PanelScope.ForTab("tab-1"), IsOpen: true),
        });

        persistence.Save(PanelScope.ForTab("tab-1"), snapshot);

        mock.Verify(m => m.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Load_AppShellScope_RoundTrip()
    {
        var (persistence, _, _) = Build();
        var saved = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, PanelScope.AppShell, IsOpen: true),
            new PanelLayoutEntry("explorer", PanelZone.LeftDock, PanelScope.AppShell, IsOpen: true),
        });

        persistence.Save(PanelScope.AppShell, saved);
        var loaded = persistence.Load(PanelScope.AppShell);

        loaded.Entries.Count.ShouldBe(2);
        loaded.Entries.ShouldContain(e => e.PanelId == "git" && e.Zone == PanelZone.RightDock && e.IsOpen);
        loaded.Entries.ShouldContain(e => e.PanelId == "explorer" && e.Zone == PanelZone.LeftDock && e.IsOpen);
    }

    [Fact]
    public void Load_AppShellScope_EmptyWhenNothingSaved()
    {
        var (persistence, _, _) = Build();
        var loaded = persistence.Load(PanelScope.AppShell);
        loaded.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Load_TabScope_ReturnsEmpty_InPhase1()
    {
        var (persistence, _, _) = Build();
        var loaded = persistence.Load(PanelScope.ForTab("tab-1"));
        loaded.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Save_PreservesWindowZoneEntries()
    {
        var (persistence, config, _) = Build();
        var snapshot = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("fileViewer", PanelZone.Window, PanelScope.AppShell, IsOpen: true),
            new PanelLayoutEntry("help", PanelZone.Popup, PanelScope.AppShell, IsOpen: true),
        });

        persistence.Save(PanelScope.AppShell, snapshot);

        config.Settings.AppShellPanels.Count.ShouldBe(1);
        config.Settings.AppShellPanels[0].PanelId.ShouldBe("fileViewer");
        config.Settings.AppShellPanels[0].Zone.ShouldBe("Window");
        config.Settings.AppShellPanels[0].IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void RoundTrip_WindowZone_SurvivesConfigReload()
    {
        var (persistence, _, _) = Build();
        var saved = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("fileViewer", PanelZone.Window, PanelScope.AppShell, IsOpen: true),
        });

        persistence.Save(PanelScope.AppShell, saved);
        var loaded = persistence.Load(PanelScope.AppShell);

        loaded.Entries.Count.ShouldBe(1);
        var entry = loaded.Entries[0];
        entry.PanelId.ShouldBe("fileViewer");
        entry.Zone.ShouldBe(PanelZone.Window);
        entry.IsOpen.ShouldBeTrue();
    }
}
