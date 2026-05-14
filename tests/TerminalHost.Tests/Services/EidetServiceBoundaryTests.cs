using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Tests.TestAdapters;
using Xunit;

namespace TerminalHost.Tests.Services;

/// <summary>
/// Contract tests for IEidetService. These exercise the in-memory adapter to
/// pin down the port's invariants — the same tests should pass for any
/// production adapter once it is integration-verified against a real Eidet.
/// </summary>
public class EidetServiceBoundaryTests
{
    private static EidetMemoryEntry MakeEntry(
        string id, string repoId, MemoryType type, string content, string? summary = null) =>
        new()
        {
            Id = id,
            RepoId = repoId,
            Type = type.ToString().ToLowerInvariant(),
            Content = content,
            Summary = summary,
            Importance = 0.5f,
            CreatedAt = DateTime.UtcNow,
            Source = "test",
            Provenance = "agent-inferred",
        };

    [Fact]
    public async Task Status_StartsDisabled()
    {
        var svc = new InMemoryEidetService();

        svc.Status.ConnectionStatus.ShouldBe(MemoryConnectionStatus.Disabled);
        svc.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task TryConnect_TransitionsThroughConnectingToConnected()
    {
        var svc = new InMemoryEidetService();
        var transitions = new List<MemoryConnectionStatus>();
        svc.StatusChanged += (_, s) => transitions.Add(s.ConnectionStatus);

        var ok = await svc.TryConnectAsync();

        ok.ShouldBeTrue();
        transitions.ShouldBe(new[] { MemoryConnectionStatus.Connecting, MemoryConnectionStatus.Connected });
        svc.IsConnected.ShouldBeTrue();
        svc.Status.ConnectedSince.ShouldNotBeNull();
    }

    [Fact]
    public async Task TryConnect_OnHealthFailure_TransitionsToError_ThenRecovers()
    {
        var svc = new InMemoryEidetService { SimulatedHealthFailure = "boom" };
        var transitions = new List<MemoryConnectionStatus>();
        svc.StatusChanged += (_, s) => transitions.Add(s.ConnectionStatus);

        var firstAttempt = await svc.TryConnectAsync();

        firstAttempt.ShouldBeFalse();
        transitions.ShouldBe(new[] { MemoryConnectionStatus.Connecting, MemoryConnectionStatus.Error });
        svc.Status.ErrorMessage.ShouldBe("boom");
        svc.IsConnected.ShouldBeFalse();

        // Recover: clear failure, reconnect succeeds
        svc.SimulatedHealthFailure = null;
        transitions.Clear();

        var secondAttempt = await svc.TryConnectAsync();

        secondAttempt.ShouldBeTrue();
        transitions.ShouldBe(new[] { MemoryConnectionStatus.Connecting, MemoryConnectionStatus.Connected });
        svc.IsConnected.ShouldBeTrue();
        svc.Status.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task TryConnect_WhenDisabled_DisconnectsAndReturnsFalse()
    {
        var svc = new InMemoryEidetService { Enabled = false };

        var result = await svc.TryConnectAsync();

        result.ShouldBeFalse();
        svc.Status.ConnectionStatus.ShouldBe(MemoryConnectionStatus.Disabled);
    }

    [Fact]
    public async Task OpenProjectPaths_TriggerIntake_OnConnect()
    {
        var svc = new InMemoryEidetService();

        await svc.TryConnectAsync(new[] { "/repo/a", "/repo/b" });

        svc.IntakeCallCounts["/repo/a"].ShouldBe(1);
        svc.IntakeCallCounts["/repo/b"].ShouldBe(1);
    }

    [Fact]
    public async Task OnProjectOpened_WhenDisconnected_IsNoOp()
    {
        var svc = new InMemoryEidetService();

        await svc.OnProjectOpenedAsync("/repo/x");

        svc.IntakeCallCounts.ShouldNotContainKey("/repo/x");
    }

    [Fact]
    public async Task BrowseAndSearch_RoundTrip_PreservesEntryTypes()
    {
        var svc = new InMemoryEidetService();
        await svc.TryConnectAsync();
        const string repo = "repo-1";
        svc.Seed(MakeEntry("o1", repo, MemoryType.Observation, "saw something quirky"));
        svc.Seed(MakeEntry("i1", repo, MemoryType.Insight, "deeper pattern"));
        svc.Seed(MakeEntry("p1", repo, MemoryType.Procedure, "do these steps", summary: "deploy"));
        svc.Seed(MakeEntry("h1", repo, MemoryType.Heuristic, "rule of thumb"));

        var browseAll = await svc.BrowseAsync(repo);
        var browseInsights = await svc.BrowseAsync(repo, type: "insight");
        var searchDeploy = await svc.SearchAsync(repo, "deploy");

        browseAll!.Entries.Select(e => e.Id).ShouldBe(new[] { "o1", "i1", "p1", "h1" }, ignoreOrder: true);
        browseAll.Entries.Select(e => e.ParsedType).ShouldBe(
            new[] { MemoryType.Observation, MemoryType.Insight, MemoryType.Procedure, MemoryType.Heuristic },
            ignoreOrder: true);

        browseInsights!.Entries.Single().Id.ShouldBe("i1");
        searchDeploy!.Results.Single().Id.ShouldBe("p1");
    }

    [Fact]
    public async Task Stats_AggregatesByType_PerRepo()
    {
        var svc = new InMemoryEidetService();
        await svc.TryConnectAsync();
        svc.Seed(MakeEntry("a", "r1", MemoryType.Observation, "x"));
        svc.Seed(MakeEntry("b", "r1", MemoryType.Observation, "y"));
        svc.Seed(MakeEntry("c", "r1", MemoryType.Insight, "z"));
        svc.Seed(MakeEntry("d", "r2", MemoryType.Observation, "other repo"));

        var stats = await svc.GetStatsAsync("r1");

        stats!.Total.ShouldBe(3);
        stats.Counts["observation"].ShouldBe(2);
        stats.Counts["insight"].ShouldBe(1);
        stats.Counts.ShouldNotContainKey("heuristic");
    }

    [Fact]
    public async Task Forget_RemovesEntry_AndIsIdempotent()
    {
        var svc = new InMemoryEidetService();
        await svc.TryConnectAsync();
        svc.Seed(MakeEntry("victim", "r1", MemoryType.Observation, "doomed"));

        var first = await svc.ForgetAsync("victim");
        var second = await svc.ForgetAsync("victim");

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        (await svc.BrowseAsync("r1"))!.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProxyGet_Returns503_WhenDisconnected()
    {
        var svc = new InMemoryEidetService();

        var (statusCode, _, _) = await svc.ProxyGetAsync("/api/eidet/anything");

        statusCode.ShouldBe(503);
    }

    [Fact]
    public async Task ProxyGet_Returns200_WhenConnected()
    {
        var svc = new InMemoryEidetService();
        await svc.TryConnectAsync();

        var (statusCode, body, contentType) = await svc.ProxyGetAsync("/api/eidet/something");

        statusCode.ShouldBe(200);
        contentType.ShouldBe("application/json");
        body.ShouldContain("something");
    }

    [Fact]
    public async Task TestConnection_DoesNotChangeStatus()
    {
        var svc = new InMemoryEidetService();

        var result = await svc.TestConnectionAsync("http://probe");

        result.ShouldNotBeNull();
        result!.IsRunning.ShouldBeTrue();
        svc.Status.ConnectionStatus.ShouldBe(MemoryConnectionStatus.Disabled);
        svc.TestConnectionUrls.ShouldContain("http://probe");
    }

    [Fact]
    public async Task Disconnect_ResetsToDisabled()
    {
        var svc = new InMemoryEidetService();
        await svc.TryConnectAsync();
        svc.IsConnected.ShouldBeTrue();

        svc.Disconnect();

        svc.IsConnected.ShouldBeFalse();
        svc.Status.ConnectionStatus.ShouldBe(MemoryConnectionStatus.Disabled);
        svc.Status.ConnectedSince.ShouldBeNull();
    }

    [Fact]
    public async Task RunIntake_WhenDisconnected_ReportsNotConnected()
    {
        var svc = new InMemoryEidetService();

        var msg = await svc.RunIntakeAsync("/repo");

        msg.ShouldBe("Eidet is not connected.");
        svc.IntakeCallCounts.ShouldNotContainKey("/repo");
    }

    [Fact]
    public async Task RunIntake_WhenConnected_RecordsCallAndReturnsSummary()
    {
        var svc = new InMemoryEidetService { IntakeNewCount = 7 };
        await svc.TryConnectAsync();

        var msg = await svc.RunIntakeAsync("/repo/a");

        msg.ShouldContain("7 new");
        svc.IntakeCallCounts["/repo/a"].ShouldBe(1);
    }
}
