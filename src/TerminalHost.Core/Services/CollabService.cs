using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// In-memory collaboration service. Thread-safe via locking.
/// Topics auto-create on first use and auto-delete when empty.
/// State resets on app restart (no persistence).
/// </summary>
public class CollabService : ICollabService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, CollabSession> _sessions = new();
    private readonly Dictionary<string, CollabTopic> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CollabMessage> _messages = new();
    // session → topic → last read message ID
    private readonly Dictionary<string, Dictionary<string, int>> _cursors = new();
    private readonly Dictionary<string, List<TaskCompletionSource<bool>>> _topicWaiters = new(StringComparer.OrdinalIgnoreCase);
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

    public void Subscribe(string session, string topic, string? description = null)
    {
        lock (_lock)
        {
            EnsureTopicAndSubscribe(session, topic, description);
        }
        RaiseChanged();
    }

    public (bool ok, string? error) Unsubscribe(string session, string topic)
    {
        lock (_lock)
        {
            if (!_topics.TryGetValue(topic, out var t))
                return (false, $"Topic '{topic}' does not exist.");

            t.Subscribers.Remove(session);

            // Auto-delete empty topics
            if (t.Subscribers.Count == 0)
            {
                _topics.Remove(topic);
                _messages.RemoveAll(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase));
                _topicWaiters.Remove(topic);
                // Clean up cursors for this topic
                foreach (var c in _cursors.Values)
                    c.Remove(topic);
            }
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

    public void SendMessage(string session, string topic, string content)
    {
        lock (_lock)
        {
            EnsureTopicAndSubscribe(session, topic);

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

            // Wake any long-polling readers on this topic
            if (_topicWaiters.TryGetValue(topic, out var waiters))
            {
                foreach (var tcs in waiters)
                    tcs.TrySetResult(true);
                waiters.Clear();
            }
        }
        RaiseChanged();
    }

    public (List<CollabMessage> messages, int cursor) ReadMessages(string session, string topic, int sinceId)
    {
        lock (_lock)
        {
            EnsureTopicAndSubscribe(session, topic);

            var msgs = _messages
                .Where(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase) && m.Id > sinceId)
                .ToList();

            var maxId = msgs.Count > 0 ? msgs.Max(m => m.Id) : sinceId;

            // Update cursor
            EnsureCursor(session, topic);
            _cursors[session][topic] = maxId;

            return (msgs, maxId);
        }
    }

    public async Task<(List<CollabMessage> messages, int cursor)> ReadMessagesAsync(
        string session, string topic, int sinceId, int timeoutMs, CancellationToken ct)
    {
        // Fast path: check for messages immediately
        TaskCompletionSource<bool>? tcs = null;
        lock (_lock)
        {
            EnsureTopicAndSubscribe(session, topic);

            var msgs = _messages
                .Where(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase) && m.Id > sinceId)
                .ToList();

            if (msgs.Count > 0 || timeoutMs <= 0)
            {
                var maxId = msgs.Count > 0 ? msgs.Max(m => m.Id) : sinceId;
                EnsureCursor(session, topic);
                _cursors[session][topic] = maxId;
                return (msgs, maxId);
            }

            // No messages — register a waiter
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_topicWaiters.TryGetValue(topic, out var waiters))
            {
                waiters = new List<TaskCompletionSource<bool>>();
                _topicWaiters[topic] = waiters;
            }
            waiters.Add(tcs);
        }

        // Wait outside the lock for a signal or timeout
        using var ctsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ctsTimeout.CancelAfter(timeoutMs);
        try
        {
            await tcs.Task.WaitAsync(ctsTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout or caller cancelled — remove our waiter and return whatever we have
            lock (_lock)
            {
                if (_topicWaiters.TryGetValue(topic, out var waiters))
                    waiters.Remove(tcs);
            }
        }

        // Re-read messages under lock
        lock (_lock)
        {
            // Topic may have been deleted while waiting (all unsubscribed)
            if (!_topics.ContainsKey(topic))
            {
                EnsureTopicAndSubscribe(session, topic);
                return (new(), sinceId);
            }

            var msgs = _messages
                .Where(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase) && m.Id > sinceId)
                .ToList();

            var maxId = msgs.Count > 0 ? msgs.Max(m => m.Id) : sinceId;
            EnsureCursor(session, topic);
            _cursors[session][topic] = maxId;
            return (msgs, maxId);
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

    /// <summary>
    /// Ensures a topic exists and the session is subscribed. Must be called under _lock.
    /// </summary>
    private void EnsureTopicAndSubscribe(string session, string topic, string? description = null)
    {
        if (!_topics.TryGetValue(topic, out var t))
        {
            t = new CollabTopic
            {
                Name = topic,
                CreatedBy = session,
            };
            _topics[topic] = t;
        }

        // Update description if provided (allows changing it after creation)
        if (description != null)
            t.Description = description;

        t.Subscribers.Add(session);
        EnsureCursor(session, topic);
    }

    private void EnsureCursor(string session, string topic)
    {
        if (!_cursors.ContainsKey(session))
            _cursors[session] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!_cursors[session].ContainsKey(topic))
            _cursors[session][topic] = 0;
    }
}
