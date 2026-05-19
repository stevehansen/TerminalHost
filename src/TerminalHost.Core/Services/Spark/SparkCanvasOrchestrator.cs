using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Interfaces.Spark;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Services.Spark;

/// <summary>
/// Owns the Spark canvas's mode state machine, activity-event routing, and
/// command-side glue between the host and the JS canvas. Platform-agnostic —
/// no Dispatcher / Avalonia / WebView2 references.
/// </summary>
/// <remarks>
/// State machine: <see cref="CanvasState.Empty"/> → <see cref="CanvasState.Single"/>
/// → <see cref="CanvasState.Multi"/> | <see cref="CanvasState.Replay"/> and back.
/// Transitions are funneled through <see cref="Reduce"/>, which is a pure function
/// of <c>(state, trigger)</c> and is the single source of truth for the FSM.
///
/// Threading: the orchestrator assumes all callbacks arrive on a single logical
/// thread. Activity-event handlers hop onto that thread via
/// <see cref="ICanvasTransport.Post"/>. The orchestrator never references any
/// platform-specific dispatcher.
/// </remarks>
public sealed class SparkCanvasOrchestrator : IDisposable
{
    private const string LogSource = "SparkCanvasOrchestrator";

    private readonly ISessionCatalog _catalog;
    private readonly ISessionActivityService? _activity;
    private readonly IThemeStore _theme;
    private readonly IDebugLogService? _log;

    private readonly HashSet<string> _enrichedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SessionListItem> _availableSessions = new();
    private readonly object _disposeGate = new();
    private readonly CancellationTokenSource _cts = new();

    private ICanvasTransport? _transport;
    private bool _disposed;
    private bool _readyHandled;

    public SparkCanvasOrchestrator(
        ISessionCatalog catalog,
        ISessionActivityService? activity,
        IThemeStore theme,
        IDebugLogService? log = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _activity = activity;
        _log = log;

        if (_activity != null)
            _activity.ActivityEventProcessed += OnActivityEventBackground;
    }

    /// <summary>The current canvas mode.</summary>
    public CanvasState State { get; private set; } = new CanvasState.Empty();

    /// <summary>Raised when <see cref="State"/> transitions to a new value.</summary>
    public event EventHandler<CanvasState>? StateChanged;

    /// <summary>The most recent session list pushed to the canvas.</summary>
    public IReadOnlyList<SessionListItem> AvailableSessions => _availableSessions;

    /// <summary>Raised when <see cref="AvailableSessions"/> changes.</summary>
    public event EventHandler? AvailableSessionsChanged;

    /// <summary>
    /// Pure FSM transition function. Computes the next <see cref="CanvasState"/>
    /// given a current state and a <see cref="Trigger"/>. No I/O, no side effects —
    /// the command methods invoke this then perform transport sends using the result.
    /// </summary>
    public static CanvasState Reduce(CanvasState current, Trigger trigger) => trigger switch
    {
        Trigger.HostOpen open => new CanvasState.Single(open.SessionId),
        Trigger.HostJsonl jsonl => new CanvasState.Replay(jsonl.FilePath, jsonl.SessionId),
        Trigger.HostMulti multi => new CanvasState.Multi(multi.SessionIds),
        Trigger.HostExitMulti => new CanvasState.Empty(),
        Trigger.ActivityStart start when current is CanvasState.Empty
            => new CanvasState.Single(start.SessionId),
        _ => current
    };

    /// <summary>
    /// Triggers consumed by <see cref="Reduce"/>. Discriminated-union of every
    /// event that can mutate <see cref="State"/>.
    /// </summary>
    public abstract record Trigger
    {
        /// <summary>Host requested opening a session (also covers inbound SelectSession).</summary>
        public sealed record HostOpen(string SessionId) : Trigger;

        /// <summary>Host requested loading a JSONL transcript.</summary>
        public sealed record HostJsonl(string FilePath, string SessionId) : Trigger;

        /// <summary>Host or inbound requested entering multi mode (also covers inbound RequestMultiMode).</summary>
        public sealed record HostMulti(IReadOnlySet<string> SessionIds) : Trigger;

        /// <summary>Host or inbound requested exiting multi mode (also covers inbound ExitMultiMode).</summary>
        public sealed record HostExitMulti : Trigger;

        /// <summary>Activity feed reported a SessionStart for a session not yet known to the canvas.</summary>
        public sealed record ActivityStart(string SessionId) : Trigger;
    }

    /// <summary>
    /// Attaches a transport. Idempotent across panel reloads — detaches any
    /// previously-attached transport before subscribing.
    /// </summary>
    public void Attach(ICanvasTransport transport)
    {
        if (transport == null) throw new ArgumentNullException(nameof(transport));

        // Same instance? bail — avoids re-running the ready handshake on no-op re-Attach (S4).
        if (ReferenceEquals(_transport, transport)) return;

        if (_transport != null)
        {
            _transport.Received -= OnTransportReceived;
            _transport.Ready -= OnTransportReady;
        }

        _transport = transport;
        _readyHandled = false;
        _transport.Received += OnTransportReceived;
        _transport.Ready += OnTransportReady;

        if (_transport.IsReady)
        {
            // Transport already-ready (e.g. NullCanvasTransport, late-attached test transport).
            OnTransportReady(this, EventArgs.Empty);
        }
    }

    // -------- Host-initiated commands --------

    /// <summary>Opens a single session in <see cref="CanvasState.Single"/> mode.</summary>
    public async Task OpenSessionAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        var snapshot = _catalog.GetSnapshot(sessionId);
        if (snapshot != null && snapshot.Agents.Values.Any(a => a.IsMain && a.Model == null))
        {
            await EnrichOnceAsync(sessionId);
            snapshot = _catalog.GetSnapshot(sessionId) ?? snapshot;
        }

        if (snapshot == null)
        {
            // S7: still transition (canvas may receive snapshot data later via activity events),
            // but send a SetSession placeholder so the user sees "waiting for data".
            TransitionTo(Reduce(State, new Trigger.HostOpen(sessionId)));
            await SendAsync(new CanvasOutbound.Clear());
            await SendAsync(new CanvasOutbound.SetSession(sessionId, "Waiting for session data..."));
            return;
        }

        TransitionTo(Reduce(State, new Trigger.HostOpen(sessionId)));
        await SendAsync(new CanvasOutbound.Clear());
        await SendAsync(new CanvasOutbound.LoadState(snapshot));
    }

    /// <summary>Opens a JSONL transcript file in <see cref="CanvasState.Replay"/> mode.</summary>
    public async Task OpenJsonlAsync(string jsonlPath)
    {
        if (string.IsNullOrEmpty(jsonlPath)) return;

        ReplayLoadResult? result;
        try
        {
            result = await _catalog.LoadReplayAsync(jsonlPath, _cts.Token);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (result == null) return;

        TransitionTo(Reduce(State, new Trigger.HostJsonl(jsonlPath, result.Snapshot.SessionId)));
        await SendAsync(new CanvasOutbound.Clear());
        await SendAsync(new CanvasOutbound.LoadReplay(result.Snapshot, result.Events));
    }

    /// <summary>Enters multi-session observatory mode.</summary>
    public async Task EnterMultiModeAsync()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = new List<SessionSnapshot>();

        foreach (var item in _availableSessions)
        {
            var snap = _catalog.GetSnapshot(item.SessionId);
            if (snap != null && ids.Add(snap.SessionId))
                snapshots.Add(snap);
        }

        TransitionTo(Reduce(State, new Trigger.HostMulti(ids)));
        await SendAsync(new CanvasOutbound.Clear());
        await SendAsync(new CanvasOutbound.LoadMultiState(snapshots));
    }

    /// <summary>Leaves multi-session mode. Returns to <see cref="CanvasState.Empty"/>.</summary>
    public Task ExitMultiModeAsync()
    {
        TransitionTo(Reduce(State, new Trigger.HostExitMulti()));
        return Task.CompletedTask;
    }

    /// <summary>Refreshes the session list and pushes it to the canvas.</summary>
    public async Task RefreshSessionsAsync()
    {
        // S2: snapshot the new list into a local before the await; concurrent
        // RefreshSessions cannot interleave a partial mutation.
        var fresh = _catalog.List();
        _availableSessions.Clear();
        foreach (var item in fresh)
            _availableSessions.Add(item);

        var toSend = _availableSessions.ToArray();
        AvailableSessionsChanged?.Invoke(this, EventArgs.Empty);
        await SendAsync(new CanvasOutbound.SessionList(toSend));
    }

    // -------- Transport callbacks (on UI thread) --------

    private async void OnTransportReady(object? sender, EventArgs e)
    {
        // S4: dedupe — IsReady on attach + the transport's own Ready event can both fire.
        if (_readyHandled) return;
        _readyHandled = true;

        try
        {
            // Push saved theme first so the canvas styles its first paint correctly.
            await SendAsync(new CanvasOutbound.SetTheme(_theme.Load()));

            // Push the initial session list.
            await RefreshSessionsAsync();

            // Auto-connect if we already have a session selected (state surviving panel reload).
            if (State is CanvasState.Single s)
            {
                // Force a re-push of the snapshot so the canvas reflects the existing state.
                await OpenSessionAsync(s.SessionId);
            }
            else if (State is CanvasState.Empty)
            {
                var first = _availableSessions.FirstOrDefault(x => x.IsLive)
                    ?? _availableSessions.FirstOrDefault();
                if (first != null)
                {
                    await OpenSessionAsync(first.SessionId);
                }
                else
                {
                    await SendAsync(new CanvasOutbound.SetSession(null, "Waiting for session..."));
                }
            }
        }
        catch (Exception ex)
        {
            // B1: log instead of swallowing silently.
            _log?.Error(LogSource, $"OnTransportReady failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnTransportReceived(object? sender, CanvasInbound message)
    {
        try
        {
            switch (message)
            {
                case CanvasInbound.SelectSession s:
                    await OpenSessionAsync(s.SessionId);
                    break;
                case CanvasInbound.RefreshSessions:
                    await RefreshSessionsAsync();
                    break;
                case CanvasInbound.RequestMultiMode:
                    await EnterMultiModeAsync();
                    break;
                case CanvasInbound.ExitMultiMode:
                    await ExitMultiModeAsync();
                    break;
                case CanvasInbound.ThemeChanged t:
                    // Persist only — do NOT echo back as SetTheme (JS already has the value).
                    _theme.Save(t.Theme);
                    break;
                case CanvasInbound.Ready:
                    // The transport raised Ready separately; nothing to do here.
                    break;
            }
        }
        catch (Exception ex)
        {
            // B1: log instead of swallowing silently.
            _log?.Error(LogSource, $"OnTransportReceived ({message?.GetType().Name}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------- Activity event subscription --------

    private void OnActivityEventBackground(object? sender, ActivityEvent evt)
    {
        // Hop to the orchestrator's logical thread via the transport.
        var t = _transport;
        if (t == null)
        {
            // No transport attached yet: drop. Re-attach will re-push state.
            return;
        }
        t.Post(() => HandleActivityEvent(evt));
    }

    private void HandleActivityEvent(ActivityEvent evt)
    {
        // B2: Post may arrive after Dispose has unsubscribed and nulled the transport.
        if (_disposed) return;

        try
        {
            // Auto-connect: first SessionStart while Empty → Single.
            // S3: transition state synchronously BEFORE awaiting the snapshot push,
            // so a second concurrent SessionStart can't also pass the Empty gate.
            if (State is CanvasState.Empty && evt.Type == ActivityEventType.SessionStart)
            {
                TransitionTo(Reduce(State, new Trigger.ActivityStart(evt.SessionId)));
                _ = OpenSessionAsyncFireAndForget(evt.SessionId);
                return;
            }

            var shouldForward = State switch
            {
                CanvasState.Single s => string.Equals(s.SessionId, evt.SessionId, StringComparison.Ordinal),
                CanvasState.Multi => true,
                CanvasState.Replay => false,
                _ => false
            };
            if (!shouldForward) return;

            var payload = ToEventPayload(evt);
            _ = SendAsyncFireAndForget(new CanvasOutbound.Event(payload));
        }
        catch (Exception ex)
        {
            // B1: log instead of swallowing silently.
            _log?.Error(LogSource, $"HandleActivityEvent ({evt?.Type}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Wrappers so fire-and-forget paths surface failures through the log instead of vanishing.
    private async Task OpenSessionAsyncFireAndForget(string sessionId)
    {
        try
        {
            // S3: the state transition already happened synchronously; this just pushes the snapshot.
            // We need to re-do the catalog lookup + send sequence WITHOUT another transition.
            var snapshot = _catalog.GetSnapshot(sessionId);
            if (snapshot != null && snapshot.Agents.Values.Any(a => a.IsMain && a.Model == null))
            {
                await EnrichOnceAsync(sessionId);
                snapshot = _catalog.GetSnapshot(sessionId) ?? snapshot;
            }

            await SendAsync(new CanvasOutbound.Clear());
            if (snapshot != null)
                await SendAsync(new CanvasOutbound.LoadState(snapshot));
            else
                await SendAsync(new CanvasOutbound.SetSession(sessionId, "Waiting for session data..."));
        }
        catch (Exception ex)
        {
            _log?.Error(LogSource, $"OpenSessionAsync (auto-connect '{sessionId}') failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task SendAsyncFireAndForget(CanvasOutbound message)
    {
        try
        {
            await SendAsync(message);
        }
        catch (Exception ex)
        {
            _log?.Error(LogSource, $"SendAsync ({message?.GetType().Name}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------- Helpers --------

    private async Task EnrichOnceAsync(string sessionId)
    {
        if (!_enrichedSessions.Add(sessionId)) return;
        try
        {
            await _catalog.EnrichAsync(sessionId, _cts.Token);
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed mid-flight; safe to ignore.
        }
        catch (OperationCanceledException)
        {
            // Cancellation during Dispose — expected.
        }
        catch (Exception ex)
        {
            // best-effort, but make the failure observable.
            _log?.Warn(LogSource, $"EnrichAsync ('{sessionId}') failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TransitionTo(CanvasState next)
    {
        if (Equals(State, next)) return;
        State = next;
        StateChanged?.Invoke(this, next);
    }

    private Task SendAsync(CanvasOutbound message)
    {
        var t = _transport;
        if (t == null) return Task.CompletedTask;
        return t.SendAsync(message);
    }

    internal static EventPayload ToEventPayload(ActivityEvent evt)
    {
        return new EventPayload
        {
            Type = evt.Type.ToString(),
            SessionId = evt.SessionId,
            AgentId = evt.AgentId,
            Timestamp = evt.Timestamp,
            // Defensive deep clone — the source may mutate evt.Data after raising the event,
            // and we want the EventPayload snapshot to be stable for downstream serialization.
            Data = DeepCloneDictionary(evt.Data)
        };
    }

    private static Dictionary<string, object?> DeepCloneDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count);
        foreach (var kv in source)
            clone[kv.Key] = DeepCloneValue(kv.Value);
        return clone;
    }

    private static object? DeepCloneValue(object? value) => value switch
    {
        null => null,
        string or bool or int or long or double or decimal or float or short or byte or DateTime or DateTimeOffset or Guid
            => value,
        IReadOnlyDictionary<string, object?> dict => DeepCloneDictionary(dict),
        IEnumerable<object?> list => list.Select(DeepCloneValue).ToList(),
        _ => value
    };

    public void Dispose()
    {
        // B2: ordering matters. (1) unsubscribe from the activity stream so no new
        // Posts get scheduled, (2) flip the _disposed flag under a lock so any
        // already-in-flight Post sees it and early-returns, (3) null the transport.
        lock (_disposeGate)
        {
            if (_disposed) return;

            if (_activity != null)
                _activity.ActivityEventProcessed -= OnActivityEventBackground;

            _disposed = true;

            if (_transport != null)
            {
                _transport.Received -= OnTransportReceived;
                _transport.Ready -= OnTransportReady;
                _transport = null;
            }
        }

        // S1: cancel any in-flight catalog operations.
        try { _cts.Cancel(); } catch { /* CTS already disposed */ }
        _cts.Dispose();
    }
}
