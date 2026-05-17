using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using TerminalHost.Core.Workspace;
using Xunit;

namespace TerminalHost.Tests.Services;

/// <summary>
/// Tests for <see cref="ContainerConfiguration"/> — the cached, merged,
/// normalized view of <see cref="IContainerConfiguration"/>.
/// </summary>
public class ContainerConfigurationTests
{
    // ---------- Test 1-4: Enabled override semantics ----------

    [Fact]
    public void For_ShouldReturnGlobalEnabled_WhenNoPerDirOverride()
    {
        var (configSvc, _) = BuildMock(BuildConfig(globalEnabled: true));
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For("P:/SomeDir");

        snapshot.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void For_ShouldReturnTrue_WhenGlobalDisabledButPerDirEnabled()
    {
        var dir = "P:/Project";
        var (configSvc, _) = BuildMock(BuildConfig(
            globalEnabled: false,
            dirOverrides: new[] { (dir, (bool?)true) }));
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For(dir);

        snapshot.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void For_ShouldReturnFalse_WhenGlobalEnabledButPerDirDisabled()
    {
        var dir = "P:/Project";
        var (configSvc, _) = BuildMock(BuildConfig(
            globalEnabled: true,
            dirOverrides: new[] { (dir, (bool?)false) }));
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For(dir);

        snapshot.Enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_ShouldFallBackToGlobal_WhenPerDirOverrideIsNull(bool globalEnabled)
    {
        var dir = "P:/Project";
        var (configSvc, _) = BuildMock(BuildConfig(
            globalEnabled: globalEnabled,
            dirOverrides: new[] { (dir, (bool?)null) }));
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For(dir);

        snapshot.Enabled.ShouldBe(globalEnabled);
    }

    // ---------- Test 5: Cache hit ----------

    [Fact]
    public void For_ShouldReturnSameReference_OnRepeatedCallsForSameDir()
    {
        var (configSvc, _) = BuildMock(BuildConfig(globalEnabled: true));
        var sut = new ContainerConfiguration(configSvc.Object);

        var first = sut.For("P:/Project");
        var second = sut.For("P:/Project");

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    // ---------- Test 6: Reload picks up mutations ----------

    [Fact]
    public void Reload_ShouldRefreshSnapshots_WhenUnderlyingConfigChanges()
    {
        var first = BuildConfig(globalEnabled: false);
        first.Settings.Container.ImageTag = "v1";
        var second = BuildConfig(globalEnabled: true);
        second.Settings.Container.ImageTag = "v2";

        var configSvc = new Mock<IConfigurationService>();
        var queue = new Queue<AppConfiguration>(new[] { first, second });
        configSvc.Setup(c => c.Load(It.IsAny<string?>())).Returns(() => queue.Dequeue());

        var sut = new ContainerConfiguration(configSvc.Object);

        var before = sut.For("P:/Project");
        before.Enabled.ShouldBeFalse();
        before.ImageTag.ShouldBe("v1");

        sut.Reload();

        var after = sut.For("P:/Project");
        after.Enabled.ShouldBeTrue();
        after.ImageTag.ShouldBe("v2");
    }

    // ---------- Test 7: Path normalization ----------

    [Theory]
    [InlineData("P:\\Foo")]
    [InlineData("p:/foo/")]
    [InlineData("P:\\Foo\\")]
    [InlineData("P:\\foo\\\\")]
    public void For_ShouldNormalizePathsConsistently(string variant)
    {
        var canonicalKey = DirectorySettingsStore.NormalizeKey("P:\\Foo");
        var (configSvc, _) = BuildMock(BuildConfig(
            globalEnabled: false,
            dirOverrides: new[] { ("P:\\Foo", (bool?)true) }));
        var sut = new ContainerConfiguration(configSvc.Object);

        // Prime cache with canonical form.
        var canonical = sut.For("P:\\Foo");

        var snapshot = sut.For(variant);

        // All variants must resolve to same cached snapshot and same workspace dir key.
        ReferenceEquals(canonical, snapshot).ShouldBeTrue();
        snapshot.WorkspaceDir.ShouldBe(canonicalKey);
        snapshot.Enabled.ShouldBeTrue();
    }

    // ---------- Test 8: Concurrent access stress ----------

    [Fact]
    public void For_ShouldBeThreadSafe_UnderConcurrentAccess()
    {
        var dir = "P:/Project";
        var (configSvc, _) = BuildMock(BuildConfig(
            globalEnabled: true,
            dirOverrides: new[] { (dir, (bool?)true) }));
        var sut = new ContainerConfiguration(configSvc.Object);

        var results = new ResolvedContainerSettings[1000];

        Should.NotThrow(() =>
        {
            Parallel.For(0, 1000, i =>
            {
                results[i] = sut.For(dir);
            });
        });

        results.ShouldAllBe(r => r.Enabled == true);
        // All concurrent accesses should resolve to the same cached snapshot.
        results.ShouldAllBe(r => ReferenceEquals(r, results[0]));
    }

    // ---------- Test 9: Read-only collection enforcement ----------

    [Fact]
    public void For_ShouldExposeCollectionsAsReadOnlyInterfaces()
    {
        var (configSvc, _) = BuildMock(BuildConfig(globalEnabled: true));
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For("P:/Project");

        // Static type check: these MUST be the read-only interface forms.
        IReadOnlyList<ReferenceVolume> refs = snapshot.ReferenceVolumes;
        IReadOnlyList<ExtraMount> mounts = snapshot.ExtraMounts;
        IReadOnlyList<string> args = snapshot.ExtraDockerArgs;
        IReadOnlyDictionary<string, string> env = snapshot.EnvVars;

        refs.ShouldNotBeNull();
        mounts.ShouldNotBeNull();
        args.ShouldNotBeNull();
        env.ShouldNotBeNull();
    }

    [Fact]
    public void For_ShouldRejectMutationViaListDowncast()
    {
        var config = BuildConfig(globalEnabled: true);
        config.Settings.Container.ExtraDockerArgs.Add("--rm");
        var (configSvc, _) = BuildMock(config);
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For("P:/Project");

        // ReadOnlyCollection<T> wraps a List<T> but isn't itself a List<T>;
        // direct cast to List<T> must fail (cast-to-mutable would defeat the contract).
        (snapshot.ReferenceVolumes as List<ReferenceVolume>).ShouldBeNull();
        (snapshot.ExtraMounts as List<ExtraMount>).ShouldBeNull();
        (snapshot.ExtraDockerArgs as List<string>).ShouldBeNull();
    }

    [Fact]
    public void For_ShouldRejectMutationViaDictionaryDowncast()
    {
        var (configSvc, _) = BuildMock(BuildConfig(globalEnabled: true));
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For("P:/Project");

        // Expectation per spec: cast to mutable Dictionary must fail.
        (snapshot.EnvVars as Dictionary<string, string>).ShouldBeNull();
    }

    // ---------- Test 10: Global reference stability ----------

    [Fact]
    public void Global_ShouldReturnSameReference_WithinCacheGeneration()
    {
        var (configSvc, _) = BuildMock(BuildConfig(globalEnabled: true));
        var sut = new ContainerConfiguration(configSvc.Object);

        var first = sut.Global;
        var second = sut.Global;

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void Global_ShouldReturnDifferentReference_AfterReload()
    {
        var first = BuildConfig(globalEnabled: false);
        var second = BuildConfig(globalEnabled: true);

        var configSvc = new Mock<IConfigurationService>();
        var queue = new Queue<AppConfiguration>(new[] { first, second });
        configSvc.Setup(c => c.Load(It.IsAny<string?>())).Returns(() => queue.Dequeue());

        var sut = new ContainerConfiguration(configSvc.Object);

        var before = sut.Global;
        sut.Reload();
        var after = sut.Global;

        ReferenceEquals(before, after).ShouldBeFalse();
        before.Enabled.ShouldBeFalse();
        after.Enabled.ShouldBeTrue();
    }

    // ---------- Test 11: Per-directory ContainerReferenceVolumes override ----------

    [Fact]
    public void For_ShouldUsePerDirReferenceVolumes_WhenOverrideIsSet()
    {
        var dir = "P:/Project";
        var config = BuildConfig(globalEnabled: true);
        config.Settings.Container.ReferenceVolumes.Add(
            new ReferenceVolume { Name = "global-lib", HostPath = "P:/Global" });

        var dirSettings = new DirectorySettings
        {
            ContainerReferenceVolumes = new List<ReferenceVolume>
            {
                new() { Name = "per-dir-lib", HostPath = "P:/PerDir" }
            }
        };
        config.DirectorySettings[DirectorySettingsStore.NormalizeKey(dir)] = dirSettings;

        var (configSvc, _) = BuildMock(config);
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For(dir);

        snapshot.ReferenceVolumes.Count.ShouldBe(1);
        snapshot.ReferenceVolumes[0].Name.ShouldBe("per-dir-lib");
        snapshot.ReferenceVolumes[0].HostPath.ShouldBe("P:/PerDir");
    }

    [Fact]
    public void For_ShouldFallBackToGlobalReferenceVolumes_WhenPerDirOverrideIsNull()
    {
        var dir = "P:/Project";
        var config = BuildConfig(globalEnabled: true);
        config.Settings.Container.ReferenceVolumes.Add(
            new ReferenceVolume { Name = "global-lib", HostPath = "P:/Global" });

        var dirSettings = new DirectorySettings { ContainerReferenceVolumes = null };
        config.DirectorySettings[DirectorySettingsStore.NormalizeKey(dir)] = dirSettings;

        var (configSvc, _) = BuildMock(config);
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For(dir);

        snapshot.ReferenceVolumes.Count.ShouldBe(1);
        snapshot.ReferenceVolumes[0].Name.ShouldBe("global-lib");
    }

    // ---------- Test 12: Key form matches DirectorySettings dictionary ----------

    [Fact]
    public void For_ShouldFindOverride_WhenDictKeyWasProducedByNormalizeKey()
    {
        var canonicalKey = DirectorySettingsStore.NormalizeKey("P:\\Foo");
        var config = BuildConfig(globalEnabled: false);
        config.DirectorySettings[canonicalKey] =
            new DirectorySettings { ContainerEnabled = true };

        var (configSvc, _) = BuildMock(config);
        var sut = new ContainerConfiguration(configSvc.Object);

        // Look up via several spelling variants — all must hit the same entry.
        sut.For("P:/foo").Enabled.ShouldBeTrue();
        sut.For("p:\\foo\\").Enabled.ShouldBeTrue();
        sut.For("P:\\FOO").Enabled.ShouldBeTrue();
    }

    // ---------- Test 13: Snapshot survives mid-flight config mutation ----------

    [Fact]
    public void For_Snapshot_ShouldNotReflectInPlaceConfigMutation_WithoutReload()
    {
        var dir = "P:/Project";
        var config = BuildConfig(globalEnabled: true);
        config.Settings.Container.ImageTag = "v1";

        var (configSvc, _) = BuildMock(config);
        var sut = new ContainerConfiguration(configSvc.Object);

        var snapshot = sut.For(dir);
        snapshot.Enabled.ShouldBeTrue();
        snapshot.ImageTag.ShouldBe("v1");

        // Mutate the underlying AppConfiguration without calling Reload.
        config.Settings.Container.Enabled = false;
        config.Settings.Container.ImageTag = "v2";

        // The captured snapshot must remain unchanged — it's a frozen record.
        snapshot.Enabled.ShouldBeTrue();
        snapshot.ImageTag.ShouldBe("v1");
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Builds a fresh <see cref="AppConfiguration"/> with the requested global
    /// container-enabled flag and the supplied per-directory overrides.
    /// Dictionary keys are produced via <see cref="DirectorySettingsStore.NormalizeKey"/>
    /// to match the canonical write-side key form.
    /// </summary>
    private static AppConfiguration BuildConfig(
        bool globalEnabled,
        (string workdir, bool? containerEnabled)[]? dirOverrides = null)
    {
        var config = new AppConfiguration();
        config.Settings.Container.Enabled = globalEnabled;

        if (dirOverrides != null)
        {
            foreach (var (workdir, containerEnabled) in dirOverrides)
            {
                var key = DirectorySettingsStore.NormalizeKey(workdir);
                config.DirectorySettings[key] =
                    new DirectorySettings { ContainerEnabled = containerEnabled };
            }
        }

        return config;
    }

    /// <summary>
    /// Wires a Mock IConfigurationService that always returns the supplied
    /// (single) AppConfiguration instance.
    /// </summary>
    private static (Mock<IConfigurationService> svc, AppConfiguration config) BuildMock(
        AppConfiguration config)
    {
        var mock = new Mock<IConfigurationService>();
        mock.Setup(c => c.Load(It.IsAny<string?>())).Returns(config);
        return (mock, config);
    }
}
