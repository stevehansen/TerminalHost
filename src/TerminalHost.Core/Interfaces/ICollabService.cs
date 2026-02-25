using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// In-memory collaboration service for MCP-based inter-session communication.
/// Manages topics, messages, file claims, and shared memory.
/// </summary>
public interface ICollabService
{
    // Sessions
    void EnsureSession(string name, string? workingDir = null);
    List<CollabSession> GetSessions();

    // Topics
    (bool ok, string? error) CreateTopic(string session, string name, string? description);
    (bool ok, string? error) Subscribe(string session, string topic);
    (bool ok, string? error) Unsubscribe(string session, string topic);
    List<CollabTopic> GetTopics();

    // Messages
    (bool ok, string? error) SendMessage(string session, string topic, string content);
    (List<CollabMessage> messages, int cursor, string? error) ReadMessages(string session, string topic, int sinceId);
    Task<(List<CollabMessage> messages, int cursor, string? error)> ReadMessagesAsync(string session, string topic, int sinceId, int timeoutMs, CancellationToken ct);
    Dictionary<string, int> GetUnreadCounts(string session);

    // Claims
    (bool ok, string? error) ClaimFile(string session, string filePath, string? description);
    (bool ok, string? error) ReleaseFile(string session, string filePath);
    List<CollabClaim> GetClaims();

    // Shared memory
    void SetShared(string key, string value, string setBy);
    CollabSharedEntry? GetShared(string key);
    List<CollabSharedEntry> ListShared();

    // UI observation
    event Action? StateChanged;
}
