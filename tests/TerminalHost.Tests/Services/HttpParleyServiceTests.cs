using System.Net;
using System.Net.Http;
using System.Text;
using Shouldly;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class HttpParleyServiceTests
{
    private const string TopicsJson = """
        [{"name":"api-contract","description":"DTO changes","subscribers":["backend","frontend"],
          "createdAt":"2026-09-24T10:00:00Z","createdBy":"backend","messageCount":3}]
        """;

    private const string SessionsJson = """
        [{"name":"backend","workingDir":"P:\\api","projectName":"api","lastSeen":"2026-09-24T10:05:00Z","listeners":1},
         {"name":"frontend","workingDir":null,"projectName":null,"lastSeen":"2026-09-24T10:01:00Z","listeners":0}]
        """;

    private const string MessagesJson = """
        [{"id":41,"topic":"api-contract","sender":"backend","content":"hello","createdAt":"2026-09-24T10:02:00Z"},
         {"id":42,"topic":"api-contract","sender":"frontend","content":"hi","createdAt":"2026-09-24T10:03:00Z"}]
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage HubResponse(HttpRequestMessage req) => req.RequestUri!.AbsolutePath switch
    {
        "/api/topics" => Json(TopicsJson),
        "/api/sessions" => Json(SessionsJson),
        "/api/messages" => Json(MessagesJson),
        "/api/health" => Json("""{"name":"parley","version":"1.2.3"}"""),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    private static HttpResponseMessage Unreachable(HttpRequestMessage _) =>
        throw new HttpRequestException("connection refused");

    private static (HttpParleyService svc, FakeHandler handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, bool enabled = true)
    {
        var config = new AppConfiguration();
        config.Settings.Parley.Enabled = enabled;
        var configService = new Mock<IConfigurationService>();
        configService.Setup(c => c.Load(It.IsAny<string>())).Returns(config);
        var handler = new FakeHandler(respond);
        return (new HttpParleyService(configService.Object, handler), handler);
    }

    [Fact]
    public async Task GetTopics_ParsesAndJoinsSubscriberSessions()
    {
        var (svc, handler) = Create(HubResponse);

        var topics = await svc.GetTopicsAsync();

        var topic = topics.ShouldHaveSingleItem();
        topic.Name.ShouldBe("api-contract");
        topic.Description.ShouldBe("DTO changes");
        topic.MessageCount.ShouldBe(3);
        topic.Subscribers.ShouldBe(new[] { "backend", "frontend" });
        topic.SubscriberDetails.Count.ShouldBe(2);
        topic.SubscriberDetails[0].IsConnected.ShouldBeTrue();
        topic.SubscriberDetails[0].ProjectName.ShouldBe("api");
        topic.SubscriberDetails[1].IsConnected.ShouldBeFalse();
        handler.Requests.ShouldContain(r => r.StartsWith("/api/topics"));
        svc.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task GetSessions_MapsListenersToConnected()
    {
        var (svc, _) = Create(HubResponse);

        var sessions = await svc.GetSessionsAsync();

        sessions.Count.ShouldBe(2);
        sessions[0].WorkingDir.ShouldBe("P:\\api");
        sessions[0].IsConnected.ShouldBeTrue();
        sessions[1].IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task GetRecentMessages_ParsesAndClampsCount()
    {
        var (svc, handler) = Create(HubResponse);

        var messages = await svc.GetRecentMessagesAsync(10_000);

        messages.Select(m => m.Id).ShouldBe(new[] { 41, 42 });
        messages[1].Sender.ShouldBe("frontend");
        messages[1].IsNew.ShouldBeFalse();
        handler.Requests.ShouldContain("/api/messages?count=500");
    }

    [Fact]
    public async Task HubUnreachable_ReturnsEmpty_AndReportsUnavailable()
    {
        var (svc, _) = Create(Unreachable);

        (await svc.GetTopicsAsync()).ShouldBeEmpty();
        (await svc.GetSessionsAsync()).ShouldBeEmpty();
        (await svc.GetRecentMessagesAsync()).ShouldBeEmpty();
        (await svc.GetHubVersionAsync()).ShouldBeNull();
        svc.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Disabled_MakesNoRequests()
    {
        var (svc, handler) = Create(HubResponse, enabled: false);

        (await svc.GetTopicsAsync()).ShouldBeEmpty();
        (await svc.GetRecentMessagesAsync()).ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
        svc.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task NonParleyServer_ReturnsEmpty()
    {
        var (svc, _) = Create(_ => Json("<html>not parley</html>"));

        (await svc.GetTopicsAsync()).ShouldBeEmpty();
        (await svc.GetHubVersionAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task GetHubVersion_ReturnsVersion_EvenWhenDisabled()
    {
        var (svc, handler) = Create(HubResponse, enabled: false);

        (await svc.GetHubVersionAsync("http://127.0.0.1:1234/")).ShouldBe("1.2.3");
        handler.Requests.ShouldBe(new[] { "/api/health" });
    }

    [Fact]
    public async Task Watcher_RaisesStateChangedOnConnectAndMessage()
    {
        const string feed = "event: ready\ndata: {\"lastId\":0}\n\n: heartbeat\n\nid: 1\nevent: message\ndata: {}\n\n";
        var (svc, _) = Create(req => req.RequestUri!.AbsolutePath == "/api/events"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feed, Encoding.UTF8, "text/event-stream") }
            : HubResponse(req));
        var raised = 0;
        var twice = new TaskCompletionSource();
        svc.StateChanged += () => { if (Interlocked.Increment(ref raised) == 2) twice.TrySetResult(); };

        try
        {
            svc.ApplySettings();
            await twice.Task.WaitAsync(TimeSpan.FromSeconds(5)); // connected + message
        }
        finally
        {
            svc.Dispose();
        }
    }

    [Fact]
    public async Task Watcher_WhileHubDown_SkipsQueries()
    {
        var (svc, handler) = Create(Unreachable);
        try
        {
            svc.ApplySettings();
            await Task.Delay(200); // let the first feed attempt fail
            lock (handler.Requests) handler.Requests.Clear();

            (await svc.GetTopicsAsync()).ShouldBeEmpty();

            lock (handler.Requests) handler.Requests.ShouldNotContain(r => r.StartsWith("/api/topics"));
            svc.IsAvailable.ShouldBeFalse();
        }
        finally
        {
            svc.Dispose();
        }
    }
}
