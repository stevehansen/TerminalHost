using System;
using System.Collections.Generic;
using System.Linq;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// In-memory collaboration service. Thread-safe via locking.
/// State resets on app restart (no persistence).
/// </summary>
public class CollabService : ICollabService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, CollabSession> _sessions = new();
    private readonly Dictionary<string, CollabTopic> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CollabMessage> _messages = new();
    private readonly Dictionary<string, CollabClaim> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CollabSharedEntry> _shared = new(StringComparer.OrdinalIgnoreCase);
    // session → topic → last read message ID
    private readonly Dictionary<string, Dictionary<string, int>> _cursors = new();
    private int _nextMessageId;

    public event Action? StateChanged;

    private void RaiseChanged() => StateChanged?.Invoke();

    #region Sessions

    public void EnsureSession(string name, string? workingDir = null)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(name, out var existing))
            {
                existing.LastSeen = DateTime.UtcNow;
                if (workingDir != null) existing.WorkingDir = workingDir;
            }
            else
            {
                _sessions[name] = new CollabSession { Name = name, WorkingDir = workingDir };
            }
        }
    }

    public List<CollabSession> GetSessions()
    {
        lock (_lock) return _sessions.Values.ToList();
    }

    #endregion

    #region Topics

    public (bool ok, string? error) CreateTopic(string session, string name, string? description)
    {
        lock (_lock)
        {
            if (_topics.ContainsKey(name))
                return (false, $"Topic '{name}' already exists.");

            var topic = new CollabTopic
            {
                Name = name,
                Description = description,
                CreatedBy = session,
                Subscribers = { session }
            };
            _topics[name] = topic;
            EnsureCursor(session, name);
        }
        RaiseChanged();
        return (true, null);
    }

    public (bool ok, string? error) Subscribe(string session, string topic)
    {
        lock (_lock)
        {
            if (!_topics.TryGetValue(topic, out var t))
                return (false, $"Topic '{topic}' does not exist.");

            t.Subscribers.Add(session);
            EnsureCursor(session, topic);
        }
        RaiseChanged();
        return (true, null);
    }

    public (bool ok, string? error) Unsubscribe(string session, string topic)
    {
        lock (_lock)
        {
            if (!_topics.TryGetValue(topic, out var t))
                return (false, $"Topic '{topic}' does not exist.");

            t.Subscribers.Remove(session);
        }
        RaiseChanged();
        return (true, null);
    }

    public List<CollabTopic> GetTopics()
    {
        lock (_lock)
        {
            return _topics.Values.Select(t => new CollabTopic
            {
                Name = t.Name,
                Description = t.Description,
                Subscribers = new HashSet<string>(t.Subscribers),
                CreatedAt = t.CreatedAt,
                CreatedBy = t.CreatedBy,
                MessageCount = _messages.Count(m => m.Topic.Equals(t.Name, StringComparison.OrdinalIgnoreCase))
            }).ToList();
        }
    }

    #endregion

    #region Messages

    public (bool ok, string? error) SendMessage(string session, string topic, string content)
    {
        lock (_lock)
        {
            if (!_topics.TryGetValue(topic, out var t))
                return (false, $"Topic '{topic}' does not exist.");

            if (!t.Subscribers.Contains(session))
                return (false, $"Session '{session}' is not subscribed to topic '{topic}'.");

            var msg = new CollabMessage
            {
                Id = ++_nextMessageId,
                Topic = topic,
                Sender = session,
                Content = content
            };
            _messages.Add(msg);

            // Auto-advance sender's cursor so their own messages don't show as unread
            EnsureCursor(session, topic);
            _cursors[session][topic] = msg.Id;
        }
        RaiseChanged();
        return (true, null);
    }

    public (List<CollabMessage> messages, int cursor, string? error) ReadMessages(string session, string topic, int sinceId)
    {
        lock (_lock)
        {
            if (!_topics.TryGetValue(topic, out var t))
                return (new(), 0, $"Topic '{topic}' does not exist.");

            if (!t.Subscribers.Contains(session))
                return (new(), 0, $"Session '{session}' is not subscribed to topic '{topic}'.");

            var msgs = _messages
                .Where(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase) && m.Id > sinceId)
                .ToList();

            var maxId = msgs.Count > 0 ? msgs.Max(m => m.Id) : sinceId;

            // Update cursor
            EnsureCursor(session, topic);
            _cursors[session][topic] = maxId;

            return (msgs, maxId, null);
        }
    }

    public Dictionary<string, int> GetUnreadCounts(string session)
    {
        lock (_lock)
        {
            var result = new Dictionary<string, int>();
            foreach (var (topicName, topic) in _topics)
            {
                if (!topic.Subscribers.Contains(session)) continue;

                var cursor = 0;
                if (_cursors.TryGetValue(session, out var topicCursors) &&
                    topicCursors.TryGetValue(topicName, out var c))
                    cursor = c;

                var unread = _messages.Count(m =>
                    m.Topic.Equals(topicName, StringComparison.OrdinalIgnoreCase) && m.Id > cursor);

                if (unread > 0)
                    result[topicName] = unread;
            }
            return result;
        }
    }

    #endregion

    #region Claims

    public (bool ok, string? error) ClaimFile(string session, string filePath, string? description)
    {
        var normalizedPath = NormalizePath(filePath);
        lock (_lock)
        {
            if (_claims.TryGetValue(normalizedPath, out var existing))
            {
                if (existing.Session != session)
                    return (false, $"File '{filePath}' is already claimed by session '{existing.Session}'.");
                // Re-claiming own file is OK (update description)
                existing.Description = description;
                existing.ClaimedAt = DateTime.UtcNow;
            }
            else
            {
                _claims[normalizedPath] = new CollabClaim
                {
                    FilePath = filePath,
                    Session = session,
                    Description = description
                };
            }
        }
        RaiseChanged();
        return (true, null);
    }

    public (bool ok, string? error) ReleaseFile(string session, string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        lock (_lock)
        {
            if (!_claims.TryGetValue(normalizedPath, out var existing))
                return (false, $"File '{filePath}' is not claimed.");

            if (existing.Session != session)
                return (false, $"File '{filePath}' is claimed by session '{existing.Session}', not '{session}'.");

            _claims.Remove(normalizedPath);
        }
        RaiseChanged();
        return (true, null);
    }

    public List<CollabClaim> GetClaims()
    {
        lock (_lock) return _claims.Values.ToList();
    }

    #endregion

    #region Shared Memory

    public void SetShared(string key, string value, string setBy)
    {
        lock (_lock)
        {
            _shared[key] = new CollabSharedEntry
            {
                Key = key,
                Value = value,
                SetBy = setBy,
                UpdatedAt = DateTime.UtcNow
            };
        }
        RaiseChanged();
    }

    public CollabSharedEntry? GetShared(string key)
    {
        lock (_lock)
        {
            return _shared.TryGetValue(key, out var entry) ? entry : null;
        }
    }

    public List<CollabSharedEntry> ListShared()
    {
        lock (_lock) return _shared.Values.ToList();
    }

    #endregion

    private void EnsureCursor(string session, string topic)
    {
        if (!_cursors.ContainsKey(session))
            _cursors[session] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!_cursors[session].ContainsKey(topic))
            _cursors[session][topic] = 0;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }
}
