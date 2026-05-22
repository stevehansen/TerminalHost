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
    public void Save_TabScope_PersistsToDirectorySettings()
    {
        // Updated for Phase 3: tab-scope persistence now writes to DirectorySettings.OpenRightPanels.
        var (persistence, config, mock) = Build();
        var scope = PanelScope.ForTab("tab-1");
        config.DirectorySettings["tab-1"] = new DirectorySettings();
        var snapshot = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, scope, IsOpen: true),
        });

        persistence.Save(scope, snapshot);

        mock.Verify(m => m.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
        config.DirectorySettings["tab-1"].OpenRightPanels.ShouldBe(new[] { "git" });
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
    public void Load_TabScope_ReturnsEmpty_WhenNoDirectorySettingsKey()
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

    // ---- Phase 3: Tab-scope persistence round-trip with IsActive + path normalization ----

    [Fact]
    public void DirectorySettingsPanelPersistence_TabScope_RoundTrip_RestoresOrderAndActive()
    {
        var (persistence, config, _) = Build();
        var scope = TabPanelScope.ForTab("P:\\Foo");
        var key = scope.TabId!;
        config.DirectorySettings[key] = new DirectorySettings();

        var saved = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("a", PanelZone.RightDock, scope, IsOpen: true, IsActive: false),
            new PanelLayoutEntry("b", PanelZone.RightDock, scope, IsOpen: true, IsActive: true),
            new PanelLayoutEntry("c", PanelZone.RightDock, scope, IsOpen: true, IsActive: false),
        });

        persistence.Save(scope, saved);
        var loaded = persistence.Load(scope);

        loaded.Entries.Select(e => e.PanelId).ShouldBe(new[] { "a", "b", "c" });
        loaded.Entries.ShouldAllBe(e => e.Zone == PanelZone.RightDock);
        loaded.Entries.ShouldAllBe(e => e.IsOpen);
        loaded.Entries.Single(e => e.IsActive).PanelId.ShouldBe("b");
    }

    [Fact]
    public void DirectorySettingsPanelPersistence_TabScope_KeyNormalization_MatchesDirectorySettings()
    {
        var (persistence, config, _) = Build();
        // TabPanelScope.ForTab normalizes + lowercases via WorkspaceService.NormalizeWorkingDirectory.
        var scope = TabPanelScope.ForTab("P:\\FOO\\bar\\");
        var expectedKey = scope.TabId!;
        expectedKey.ShouldContain("foo");
        expectedKey.ShouldNotEndWith("\\");
        config.DirectorySettings[expectedKey] = new DirectorySettings();

        var snapshot = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("git", PanelZone.RightDock, scope, IsOpen: true, IsActive: true),
        });
        persistence.Save(scope, snapshot);

        config.DirectorySettings.ShouldContainKey(expectedKey);
        config.DirectorySettings[expectedKey].OpenRightPanels.ShouldBe(new[] { "git" });
        config.DirectorySettings[expectedKey].ActiveRightPanel.ShouldBe("git");
    }

    [Fact]
    public void DirectorySettingsPanelPersistence_TabScope_NoActive_RoundTrips()
    {
        var (persistence, config, _) = Build();
        var scope = TabPanelScope.ForTab("P:\\NoActive");
        config.DirectorySettings[scope.TabId!] = new DirectorySettings();

        var saved = new PanelLayoutSnapshot(new[]
        {
            new PanelLayoutEntry("a", PanelZone.RightDock, scope, IsOpen: true, IsActive: false),
            new PanelLayoutEntry("b", PanelZone.RightDock, scope, IsOpen: true, IsActive: false),
        });
        persistence.Save(scope, saved);

        config.DirectorySettings[scope.TabId!].ActiveRightPanel.ShouldBeNull();

        var loaded = persistence.Load(scope);
        loaded.Entries.Select(e => e.PanelId).ShouldBe(new[] { "a", "b" });
        loaded.Entries.Any(e => e.IsActive).ShouldBeFalse();
    }

    // ---- F. PanelLayoutEntry.IsActive default ----

    [Fact]
    public void PanelLayoutEntry_DefaultIsActive_IsFalse()
    {
        var entry = new PanelLayoutEntry("p", PanelZone.RightDock, PanelScope.AppShell, IsOpen: true);
        entry.IsActive.ShouldBeFalse();
    }
}
