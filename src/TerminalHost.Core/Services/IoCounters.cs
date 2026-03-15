using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace TerminalHost.Core.Services;

/// <summary>
/// Lightweight static counters for I/O operations. Incremented at each call site,
/// read by the UI-thread watchdog when a hang is detected so we can see what the
/// app was doing leading up to the hang.
///
/// All counters use Interlocked for thread-safety with zero contention.
/// </summary>
public static class IoCounters
{
    // ── Config persistence ──────────────────────────────────────────────
    public static long ConfigLoads;
    public static long ConfigSaves;

    // ── File system ─────────────────────────────────────────────────────
    public static long FileReads;
    public static long FileWrites;

    // ── Git processes ───────────────────────────────────────────────────
    public static long GitProcessStarts;

    // ── FileSystemWatcher ───────────────────────────────────────────────
    public static long FsWatcherEvents;

    // ── Named pipe IPC ──────────────────────────────────────────────────
    public static long PipeMessagesReceived;

    // ── Timer callbacks (DispatcherTimer ticks on UI thread) ────────────
    public static long TimerCallbacks;

    // ── Dispatcher invocations ──────────────────────────────────────────
    public static long DispatcherInvokes;
    public static long DispatcherBeginInvokes;

    // ── Output buffer lock acquisitions ─────────────────────────────────
    public static long OutputBufferLocks;

    // ── Current UI-thread operation marker ──────────────────────────────
    /// <summary>
    /// Set to a descriptive string before starting a potentially slow UI-thread
    /// operation, cleared (set to null) when it completes. The watchdog reads
    /// this when a hang is detected.
    /// </summary>
    public static volatile string? CurrentUiOperation;

    // ── Config load caller tracking (temporary diagnostic) ──────────────
    private static readonly ConcurrentDictionary<string, int> _configLoadCallers = new();

    /// <summary>
    /// Called from ConfigurationService.Load(). The caller name is passed through via [CallerMemberName].
    /// </summary>
    public static void TrackConfigLoad(string? caller)
    {
        Interlocked.Increment(ref ConfigLoads);
        if (caller != null)
        {
            _configLoadCallers.AddOrUpdate(caller, 1, (_, count) => count + 1);
        }
    }

    /// <summary>
    /// Called from ConfigurationService.Save(). The caller name is passed through via [CallerMemberName].
    /// </summary>
    public static void TrackConfigSave(string? caller)
    {
        Interlocked.Increment(ref ConfigSaves);
    }

    /// <summary>
    /// Produces a snapshot string suitable for writing into the hang log.
    /// </summary>
    public static string GetSnapshot()
    {
        var sb = new StringBuilder(512);

        var op = CurrentUiOperation;
        if (op != null)
            sb.AppendLine($"  Current UI operation: {op}");

        sb.AppendLine($"  Config: {Interlocked.Read(ref ConfigLoads)} loads, {Interlocked.Read(ref ConfigSaves)} saves");

        // Show top config load callers
        if (!_configLoadCallers.IsEmpty)
        {
            sb.Append("  Config callers:");
            foreach (var kvp in _configLoadCallers.OrderByDescending(x => x.Value))
            {
                sb.Append($" {kvp.Key}={kvp.Value}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"  FileSystem: {Interlocked.Read(ref FileReads)} reads, {Interlocked.Read(ref FileWrites)} writes");
        sb.AppendLine($"  Git processes started: {Interlocked.Read(ref GitProcessStarts)}");
        sb.AppendLine($"  FSWatcher events: {Interlocked.Read(ref FsWatcherEvents)}");
        sb.AppendLine($"  Pipe messages: {Interlocked.Read(ref PipeMessagesReceived)}");
        sb.AppendLine($"  Timer callbacks: {Interlocked.Read(ref TimerCallbacks)}");
        sb.AppendLine($"  Dispatcher: {Interlocked.Read(ref DispatcherInvokes)} invokes, {Interlocked.Read(ref DispatcherBeginInvokes)} beginInvokes");
        sb.Append($"  OutputBuffer locks: {Interlocked.Read(ref OutputBufferLocks)}");

        return sb.ToString();
    }
}
