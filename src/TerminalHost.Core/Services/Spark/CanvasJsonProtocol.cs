using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// JSON envelope for the C#↔JS canvas protocol. Owns the verb-name mapping and
/// the camelCase shape the JS canvas expects. Shared by every <c>ICanvasTransport</c>
/// adapter so all platforms speak the exact same wire format.
/// </summary>
public static class CanvasJsonProtocol
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Serializes an outbound message into the JSON envelope JS already consumes.</summary>
    public static string Serialize(CanvasOutbound message)
    {
        object envelope = message switch
        {
            CanvasOutbound.Clear =>
                new { action = "clear" },
            CanvasOutbound.LoadState s =>
                new { action = "loadState", state = ProjectEnvelope(s.Session) },
            CanvasOutbound.LoadReplay r =>
                new { action = "loadReplay", state = ProjectReplay(r.Session), events = r.Events },
            CanvasOutbound.Event e =>
                new { action = "event", @event = e.Payload },
            CanvasOutbound.SetTheme t =>
                new { action = "setTheme", theme = t.Theme },
            CanvasOutbound.SetSession s =>
                new { action = "setSession", sessionId = s.Id, sessionName = s.DisplayName },
            CanvasOutbound.SessionList l =>
                new { action = "sessionList", sessions = l.Sessions },
            CanvasOutbound.LoadMultiState m =>
                new { action = "loadMultiState", sessions = ProjectMulti(m.Sessions) },
            _ => throw new ArgumentOutOfRangeException(nameof(message), $"Unknown outbound: {message.GetType().Name}")
        };

        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <summary>
    /// Parses an inbound JSON envelope into a <see cref="CanvasInbound"/> record.
    /// Returns null if the envelope is malformed or the action is unknown.
    /// </summary>
    public static CanvasInbound? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("action", out var actionElement))
                return null;
            var action = actionElement.GetString();
            if (action == null) return null;

            switch (action)
            {
                case "ready":
                    return new CanvasInbound.Ready();
                case "selectSession":
                    if (doc.RootElement.TryGetProperty("sessionId", out var idEl)
                        && idEl.GetString() is { } id && id.Length > 0)
                        return new CanvasInbound.SelectSession(id);
                    return null;
                case "refreshSessions":
                    return new CanvasInbound.RefreshSessions();
                case "requestMultiMode":
                    return new CanvasInbound.RequestMultiMode();
                case "exitMultiMode":
                    return new CanvasInbound.ExitMultiMode();
                case "themeChanged":
                    if (doc.RootElement.TryGetProperty("theme", out var thEl)
                        && thEl.GetString() is { } th && th.Length > 0)
                        return new CanvasInbound.ThemeChanged(th);
                    return null;
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    // -------- Per-variant projection --------
    //
    // Each variant emits the exact field set the JS canvas consumes. The legacy
    // `isReplay` boolean is omitted: JS never reads it (verified by grep over
    // web/spark/*.js). LoadMultiState uses a polymorphic projection so each list
    // element serializes with its own variant's fields, not just the abstract base.

    private static object ProjectLive(LiveSessionSnapshot s) => new
    {
        sessionId = s.SessionId,
        workingDirectory = s.WorkingDirectory,
        startTime = s.StartTime,
        endTime = s.EndTime,
        lifecycle = s.Lifecycle,
        displayState = s.DisplayState,
        agents = s.Agents,
        toolCalls = s.ToolCalls,
        fileActivities = s.FileActivities,
        messages = s.Messages
    };

    private static object ProjectReplay(ReplaySessionSnapshot s) => new
    {
        sessionId = s.SessionId,
        workingDirectory = s.WorkingDirectory,
        startTime = s.StartTime,
        endTime = s.EndTime,
        lifecycle = s.Lifecycle,
        displayState = s.DisplayState,
        agents = s.Agents,
        toolCalls = s.ToolCalls,
        fileActivities = s.FileActivities
    };

    private static object ProjectEnvelope(SnapshotEnvelope s) => s switch
    {
        LiveSessionSnapshot live => ProjectLive(live),
        ReplaySessionSnapshot replay => ProjectReplay(replay),
        PlaceholderSessionSnapshot placeholder => ProjectPlaceholder(placeholder),
        _ => throw new ArgumentOutOfRangeException(nameof(s), $"Unknown snapshot variant: {s.GetType().Name}")
    };

    private static object ProjectPlaceholder(PlaceholderSessionSnapshot s) => new
    {
        sessionId = s.SessionId,
        workingDirectory = s.WorkingDirectory,
        startTime = s.StartTime,
        lifecycle = s.Lifecycle,
        displayState = s.DisplayState,
        agents = s.Agents,
        fileActivities = s.FileActivities
    };

    private static IReadOnlyList<object> ProjectMulti(IReadOnlyList<SnapshotEnvelope> sessions)
    {
        var list = new List<object>(sessions.Count);
        foreach (var s in sessions)
            list.Add(ProjectEnvelope(s));
        return list;
    }
}
