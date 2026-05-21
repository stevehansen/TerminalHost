using System;
using System.Collections.Generic;
using Shouldly;
using TerminalHost.Core.Services.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Tests.Spark;

/// <summary>
/// Direct tests for <see cref="WebViewCanvasTransportBase"/>'s pre-ready queue,
/// ready-handshake, malformed-input swallowing, and disposal idempotency. Uses a
/// synchronous in-test subclass so behavior is deterministic.
/// </summary>
public class WebViewCanvasTransportBaseTests
{
    /// <summary>
    /// Minimal subclass: synchronous Post, records every outbound JSON envelope,
    /// exposes IngestJson to drive the base's inbound code path, and counts
    /// OnDispose invocations for idempotency tests.
    /// </summary>
    private sealed class FakeWebViewCanvasTransport : WebViewCanvasTransportBase
    {
        public List<string> PostedJson { get; } = new();
        public int OnDisposeCalls { get; private set; }

        public override void Post(Action action) => action();

        protected override void PostOutboundJson(string json) => PostedJson.Add(json);

        public void IngestJson(string json) => OnInboundJson(json);

        protected override void OnDispose() => OnDisposeCalls++;
    }

    private const string ReadyJson = "{\"action\":\"ready\"}";
    private const string RefreshJson = "{\"action\":\"refreshSessions\"}";

    [Fact]
    public void SendAsync_BeforeReady_QueuesInsteadOfPosting()
    {
        var t = new FakeWebViewCanvasTransport();

        t.SendAsync(new CanvasOutbound.Clear());

        t.PostedJson.ShouldBeEmpty();
        t.IsReady.ShouldBeFalse();
    }

    [Fact]
    public void Inbound_Ready_FlipsIsReadyAndRaisesReadyOnce()
    {
        var t = new FakeWebViewCanvasTransport();
        var readyCount = 0;
        t.Ready += (_, _) => readyCount++;

        t.IngestJson(ReadyJson);
        t.IngestJson(ReadyJson);

        readyCount.ShouldBe(1);
        t.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void Inbound_Ready_FlushesPreReadyQueueInOrder()
    {
        var t = new FakeWebViewCanvasTransport();

        t.SendAsync(new CanvasOutbound.SetTheme("a"));
        t.SendAsync(new CanvasOutbound.SetTheme("b"));
        t.IngestJson(ReadyJson);

        t.PostedJson.Count.ShouldBe(2);
        t.PostedJson[0].ShouldContain("\"a\"");
        t.PostedJson[1].ShouldContain("\"b\"");
    }

    [Fact]
    public void SendAsync_AfterReady_PostsImmediately()
    {
        var t = new FakeWebViewCanvasTransport();
        t.IngestJson(ReadyJson);

        t.SendAsync(new CanvasOutbound.Clear());

        t.PostedJson.Count.ShouldBe(1);
        t.PostedJson[0].ShouldContain("clear");
    }

    [Fact]
    public void Inbound_NonReadyMessage_RaisesReceived_NotReady()
    {
        var t = new FakeWebViewCanvasTransport();
        var receivedCount = 0;
        var readyCount = 0;
        CanvasInbound? lastReceived = null;
        t.Received += (_, m) => { receivedCount++; lastReceived = m; };
        t.Ready += (_, _) => readyCount++;

        t.IngestJson(RefreshJson);

        receivedCount.ShouldBe(1);
        lastReceived.ShouldBeOfType<CanvasInbound.RefreshSessions>();
        readyCount.ShouldBe(0);
        t.IsReady.ShouldBeFalse();
    }

    [Fact]
    public void Inbound_MalformedJson_IsSwallowed()
    {
        var t = new FakeWebViewCanvasTransport();
        var receivedCount = 0;
        var readyCount = 0;
        t.Received += (_, _) => receivedCount++;
        t.Ready += (_, _) => readyCount++;

        Should.NotThrow(() =>
        {
            t.IngestJson("not json at all");
            t.IngestJson("{}");
            t.IngestJson("{\"action\":\"bogus\"}");
            t.IngestJson(string.Empty);
            t.IngestJson("   ");
        });

        receivedCount.ShouldBe(0);
        readyCount.ShouldBe(0);
        t.IsReady.ShouldBeFalse();

        // Transport remains usable: a subsequent ready still works.
        t.IngestJson(ReadyJson);
        t.IsReady.ShouldBeTrue();
        readyCount.ShouldBe(1);
    }

    [Fact]
    public void SendAsync_AfterDispose_IsNoop()
    {
        var t = new FakeWebViewCanvasTransport();
        t.Dispose();

        t.SendAsync(new CanvasOutbound.Clear());

        t.PostedJson.ShouldBeEmpty();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var t = new FakeWebViewCanvasTransport();

        t.Dispose();
        t.Dispose();

        t.OnDisposeCalls.ShouldBe(1);
    }

    [Fact]
    public void Dispose_AfterReady_SuppressesFurtherEventsAndPosts()
    {
        var t = new FakeWebViewCanvasTransport();
        t.IngestJson(ReadyJson);

        var receivedCount = 0;
        t.Received += (_, _) => receivedCount++;
        t.PostedJson.Clear();

        t.Dispose();

        Should.NotThrow(() =>
        {
            t.IngestJson(RefreshJson);
            t.SendAsync(new CanvasOutbound.Clear());
        });

        receivedCount.ShouldBe(0, "no Received raised after Dispose");
        t.PostedJson.ShouldBeEmpty();
    }

    [Fact]
    public void SendAsync_RacingWithDispose_DoesNotPost()
    {
        // Simulates Dispose() landing between PostSerialized's _disposed check and
        // the marshaled outbound lambda — the lambda must re-check and bail.
        var t = new DisposeOnSecondPostFake();
        t.IngestJson(ReadyJson);
        t.PostedJson.ShouldBeEmpty();

        t.SendAsync(new CanvasOutbound.Clear());

        t.PostedJson.ShouldBeEmpty("Dispose mid-Post must skip PostOutboundJson");
    }

    private sealed class DisposeOnSecondPostFake : WebViewCanvasTransportBase
    {
        public List<string> PostedJson { get; } = new();
        private int _postCount;

        public override void Post(Action action)
        {
            _postCount++;
            // First Post call is the inbound Ready handshake; second is the outbound
            // send we want to interleave Dispose into.
            if (_postCount == 2) Dispose();
            action();
        }

        protected override void PostOutboundJson(string json) => PostedJson.Add(json);

        public void IngestJson(string json) => OnInboundJson(json);
    }

    [Fact]
    public void Inbound_Ready_AfterAlreadyReady_DoesNothing()
    {
        var t = new FakeWebViewCanvasTransport();
        var readyCount = 0;
        t.Ready += (_, _) => readyCount++;

        t.IngestJson(ReadyJson);
        t.PostedJson.Clear();
        t.IngestJson(ReadyJson);

        t.PostedJson.ShouldBeEmpty();
        readyCount.ShouldBe(1);
    }
}
