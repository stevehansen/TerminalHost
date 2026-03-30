using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// In-memory collaboration service for MCP-based inter-session communication.
/// Manages topics and messages. Topics auto-create on first use and auto-delete when empty.
/// </summary>
public interface ICollabService : IDisposable
{
    // Sessions
    void EnsureSession(string name, string? workingDir = null);
    List<CollabSession> GetSessions();

    // Topics — auto-create on subscribe/send/read, auto-delete on last unsubscribe
    void Subscribe(string session, string topic, string? description = null);
    (bool ok, string? error) Unsubscribe(string session, string topic);
    List<CollabTopic> GetTopics();

    // Messages — auto-subscribe (and auto-create topic) on send/read
    void SendMessage(string session, string topic, string content);
    (List<CollabMessage> messages, int cursor) ReadMessages(string session, string topic, int sinceId);
    Task<(List<CollabMessage> messages, int cursor)> ReadMessagesAsync(string session, string topic, int sinceId, int timeoutMs, CancellationToken ct);
    Dictionary<string, int> GetUnreadCounts(string session);

    // UI observation
    event Action? StateChanged;
}
