using System.IO;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Workspace;
using Xunit;

namespace TerminalHost.Tests.Workspace;

public class DirectorySettingsStoreTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private AppConfiguration _appConfig = new();

    private DirectorySettingsStore Build()
    {
        _config.Setup(x => x.Load(It.IsAny<string?>())).Returns(() => _appConfig);
        _config.Setup(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()))
            .Callback<AppConfiguration, string?>((cfg, _) => _appConfig = cfg);
        return new DirectorySettingsStore(_config.Object);
    }

    private static string TempPath(string suffix) => Path.Combine(Path.GetTempPath(), suffix);

    [Fact]
    public void Get_ReturnsNullForUnknownDirectory()
    {
        var store = Build();

        store.Get(TempPath("Project")).ShouldBeNull();
    }

    [Fact]
    public void Get_FindsEntryUsingNormalizedKey()
    {
        var path = TempPath("Project");
        _appConfig.DirectorySettings[DirectorySettingsStore.NormalizeKey(path)] = new DirectorySettings { SplitRatio = 0.42 };
        var store = Build();

        // Different casing + trailing separator should still resolve.
        store.Get(path.ToUpperInvariant() + Path.DirectorySeparatorChar)
            .ShouldNotBeNull().SplitRatio.ShouldBe(0.42);
    }

    [Fact]
    public void Get_ReturnsNullForNullOrEmptyOrInvalidInput()
    {
        var store = Build();

        store.Get(null).ShouldBeNull();
        store.Get("").ShouldBeNull();
        store.Get("   ").ShouldBeNull();
        store.Get("bad\0path").ShouldBeNull();
    }

    [Fact]
    public void Update_CreatesNewSettingsAndPersists()
    {
        var store = Build();
        var path = TempPath("NewProject");

        store.Update(path, settings => settings.SplitRatio = 0.75);

        var key = DirectorySettingsStore.NormalizeKey(path);
        _appConfig.DirectorySettings.ShouldContainKey(key);
        _appConfig.DirectorySettings[key].SplitRatio.ShouldBe(0.75);
        _config.Verify(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void Update_MutatesExistingSettingsWithoutOverwritingUnrelatedFields()
    {
        var path = TempPath("Existing");
        var key = DirectorySettingsStore.NormalizeKey(path);
        _appConfig.DirectorySettings[key] = new DirectorySettings
        {
            SplitRatio = 0.5,
            ActiveTerminal = "Shell",
            IsRunTerminalVisible = true,
        };
        var store = Build();

        store.Update(path, settings => settings.SplitRatio = 0.9);

        var updated = _appConfig.DirectorySettings[key];
        updated.SplitRatio.ShouldBe(0.9);
        updated.ActiveTerminal.ShouldBe("Shell");           // untouched
        updated.IsRunTerminalVisible.ShouldBeTrue();         // untouched
    }

    [Fact]
    public void Update_NoOpForInvalidPath_DoesNotSave()
    {
        var store = Build();

        store.Update("bad\0path", _ => throw new InvalidOperationException("mutator should not run"));

        _config.Verify(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Update_ThrowsForNullMutator()
    {
        var store = Build();

        Should.Throw<ArgumentNullException>(() => store.Update(TempPath("X"), null!));
    }

    [Fact]
    public void AddRecent_InsertsCanonicalPathAtFrontAndRemovesCaseInsensitiveDuplicate()
    {
        var path = TempPath("Repo");
        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _appConfig.Settings.Repositories.RecentPaths.Add(canonical.ToUpperInvariant());
        _appConfig.Settings.Repositories.RecentPaths.Add(TempPath("Other"));
        var store = Build();

        store.AddRecent(path + Path.DirectorySeparatorChar);

        var recent = _appConfig.Settings.Repositories.RecentPaths;
        recent.Count.ShouldBe(2);
        recent[0].ShouldBe(canonical);                              // case-preserving canonical form at front
        recent[1].ShouldBe(TempPath("Other"));                     // unrelated entry kept
    }

    [Fact]
    public void AddRecent_TrimsToMaxRecentItems()
    {
        _appConfig.Settings.Repositories.MaxRecentItems = 3;
        _appConfig.Settings.Repositories.RecentPaths.AddRange(new[]
        {
            TempPath("A"), TempPath("B"), TempPath("C"),
        });
        var store = Build();

        store.AddRecent(TempPath("D"));

        var recent = _appConfig.Settings.Repositories.RecentPaths;
        recent.Count.ShouldBe(3);
        recent[0].ShouldBe(Path.GetFullPath(TempPath("D")).TrimEnd(Path.DirectorySeparatorChar));
        recent.ShouldNotContain(TempPath("C")); // oldest dropped
    }

    [Fact]
    public void AddRecent_NoOpForInvalidPath()
    {
        var store = Build();

        store.AddRecent("bad\0path");

        _appConfig.Settings.Repositories.RecentPaths.ShouldBeEmpty();
        _config.Verify(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void NormalizeKey_LowercasesAndCanonicalizes()
    {
        var path = TempPath("Mixed") + Path.DirectorySeparatorChar;
        var expected = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

        DirectorySettingsStore.NormalizeKey(path).ShouldBe(expected);
    }

    [Fact]
    public void NormalizeKey_EmptyForNullEmptyOrInvalid()
    {
        DirectorySettingsStore.NormalizeKey(null).ShouldBe(string.Empty);
        DirectorySettingsStore.NormalizeKey("").ShouldBe(string.Empty);
        DirectorySettingsStore.NormalizeKey("   ").ShouldBe(string.Empty);
        DirectorySettingsStore.NormalizeKey("bad\0path").ShouldBe(string.Empty);
    }
}
