using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Collaboration service with persistence. Thread-safe via locking.
/// Topics auto-create on first use and auto-delete when empty (after at least one subscriber has joined).
/// State persists to collab-state.json with debounced writes.
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

    // Persistence
    private readonly JsonFileService<CollabPersistedState>? _jsonFileService;
    private readonly Timer? _saveTimer;
    private bool _isDirty;
    private bool _disposed;

    // Retention limits
    private const int MaxMessagesPerTopic = 500;
    private const int MaxMessagesTotal = 5000;
    private static readonly TimeSpan TopicMaxAge = TimeSpan.FromHours(24);

    public event Action? StateChanged;

    private void RaiseChanged() => StateChanged?.Invoke();

    /// <summary>
    /// Creates a CollabService with persistence.
    /// </summary>
    public CollabService(IFileSystem fileSystem)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "TerminalHost");
        fileSystem.CreateDirectory(configDir);
        var filePath = Path.Combine(configDir, "collab-state.json");

        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        _jsonFileService = new JsonFileService<CollabPersistedState>(fileSystem, filePath, options);

        LoadState();

        // Save every 5 seconds if dirty
        _saveTimer = new Timer(_ => SaveIfDirty(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Creates a CollabService without persistence (for testing).
    /// </summary>
    public CollabService()
    {
    }

    private void LoadState()
    {
        if (_jsonFileService == null) return;

        var state = _jsonFileService.Load();
        if (state.Topics.Count == 0 && state.Messages.Count == 0) return;

        lock (_lock)
        {
            _nextMessageId = state.NextMessageId;

            var cutoff = DateTime.UtcNow - TopicMaxAge;

            foreach (var pt in state.Topics)
            {
                // Skip stale topics with no messages
                if (pt.CreatedAt < cutoff)
                {
                    var hasMessages = state.Messages.Any(m =>
                        m.Topic.Equals(pt.Name, StringComparison.OrdinalIgnoreCase) && m.CreatedAt >= cutoff);
                    if (!hasMessages) continue;
                }

                _topics[pt.Name] = new CollabTopic
                {
                    Name = pt.Name,
                    Description = pt.Description,
                    CreatedBy = pt.CreatedBy,
                    CreatedAt = pt.CreatedAt,
                    HasHadSubscriber = false // No subscribers yet after restart
                };
            }

            // Load messages only for topics that survived retention
            foreach (var msg in state.Messages)
            {
                if (_topics.ContainsKey(msg.Topic))
                    _messages.Add(msg);
            }

            // Apply per-topic retention
            EnforceRetention();

            // Load cursors (only for topics that exist)
            foreach (var (session, topicCursors) in state.Cursors)
            {
                var filtered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var (topic, cursor) in topicCursors)
                {
                    if (_topics.ContainsKey(topic))
                        filtered[topic] = cursor;
                }
                if (filtered.Count > 0)
                    _cursors[session] = filtered;
            }
        }
    }

    private CollabPersistedState BuildSnapshot()
    {
        // Must be called under _lock
        var state = new CollabPersistedState
        {
            NextMessageId = _nextMessageId,
            Topics = _topics.Values.Select(t => new PersistedTopic
            {
                Name = t.Name,
                Description = t.Description,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt
            }).ToList(),
            Messages = _messages.ToList(),
            Cursors = _cursors.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, int>(kvp.Value))
        };
        return state;
    }

    private void SaveIfDirty()
    {
        if (!_isDirty || _disposed || _jsonFileService == null) return;

        CollabPersistedState snapshot;
        lock (_lock)
        {
            if (!_isDirty) return;
            _isDirty = false;
            EnforceRetention();
            snapshot = BuildSnapshot();
        }

        // Save outside the lock so disk I/O doesn't block messaging
        try
        {
            _jsonFileService.Save(snapshot);
        }
        catch
        {
            // Mark dirty again so we retry next cycle
            _isDirty = true;
        }
    }

    /// <summary>
    /// Enforces retention limits. Must be called under _lock.
    /// </summary>
    private void EnforceRetention()
    {
        // Per-topic limit
        var topicGroups = _messages
            .GroupBy(m => m.Topic, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > MaxMessagesPerTopic);

        foreach (var group in topicGroups)
        {
            var toKeep = group.OrderByDescending(m => m.Id).Take(MaxMessagesPerTopic).Select(m => m.Id).ToHashSet();
            _messages.RemoveAll(m => m.Topic.Equals(group.Key, StringComparison.OrdinalIgnoreCase) && !toKeep.Contains(m.Id));
        }

        // Global limit
        if (_messages.Count > MaxMessagesTotal)
        {
            var toKeep = _messages.OrderByDescending(m => m.Id).Take(MaxMessagesTotal).Select(m => m.Id).ToHashSet();
            _messages.RemoveAll(m => !toKeep.Contains(m.Id));
        }
    }

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
            _isDirty = true;
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

            // Auto-delete empty topics only if at least one session has subscribed since load
            if (t.Subscribers.Count == 0 && t.HasHadSubscriber)
            {
                _topics.Remove(topic);
                _messages.RemoveAll(m => m.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase));
                _topicWaiters.Remove(topic);
                // Clean up cursors for this topic
                foreach (var c in _cursors.Values)
                    c.Remove(topic);
            }

            _isDirty = true;
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

            _isDirty = true;
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

            _isDirty = true;
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
                _isDirty = true;
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
            _isDirty = true;
            return (msgs, maxId);
        }
    }

    public List<CollabMessage> GetRecentMessages(int count = 20)
    {
        lock (_lock)
        {
            return _messages
                .OrderByDescending(m => m.Id)
                .Take(count)
                .OrderBy(m => m.Id)
                .ToList();
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
        t.HasHadSubscriber = true;
        EnsureCursor(session, topic);
    }

    private void EnsureCursor(string session, string topic)
    {
        if (!_cursors.ContainsKey(session))
            _cursors[session] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!_cursors[session].ContainsKey(topic))
            _cursors[session][topic] = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _saveTimer?.Dispose();
        // Final synchronous save
        if (_jsonFileService != null && _isDirty)
        {
            CollabPersistedState snapshot;
            lock (_lock)
            {
                _isDirty = false;
                EnforceRetention();
                snapshot = BuildSnapshot();
            }
            try { _jsonFileService.Save(snapshot); } catch { }
        }
    }
}
