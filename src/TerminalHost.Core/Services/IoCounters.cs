using System;
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
        sb.AppendLine($"  OutputBuffer locks: {Interlocked.Read(ref OutputBufferLocks)}");

        // GC statistics
        var isServerGC = System.Runtime.GCSettings.IsServerGC;
        var gcLatencyMode = System.Runtime.GCSettings.LatencyMode;
        sb.AppendLine($"  GC mode: {(isServerGC ? "Server" : "Workstation")}, latency={gcLatencyMode}");
        sb.AppendLine($"  GC collections: gen0={GC.CollectionCount(0)}, gen1={GC.CollectionCount(1)}, gen2={GC.CollectionCount(2)}");
        sb.AppendLine($"  GC heap: {GC.GetTotalMemory(false) / (1024 * 1024)}MB managed");
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            sb.AppendLine($"  GC memory: heap={gcInfo.HeapSizeBytes / (1024 * 1024)}MB, committed={gcInfo.TotalCommittedBytes / (1024 * 1024)}MB, fragmented={gcInfo.FragmentedBytes / (1024 * 1024)}MB");
            sb.AppendLine($"  GC last: gen={gcInfo.Generation}, concurrent={gcInfo.Concurrent}, compacted={gcInfo.Compacted}, finalizationPending={gcInfo.FinalizationPendingCount}");
            // Pause durations from the last GC (if available)
            var pauseDurations = gcInfo.PauseDurations;
            if (pauseDurations.Length > 0)
            {
                var totalPauseMs = 0.0;
                foreach (var pause in pauseDurations)
                    totalPauseMs += pause.TotalMilliseconds;
                sb.AppendLine($"  GC pause: {totalPauseMs:F1}ms total ({pauseDurations.Length} pauses), pause%={gcInfo.PauseTimePercentage:F1}%");
            }
        }
        catch
        {
            sb.Append("  GC memory info: unavailable");
        }

        return sb.ToString();
    }
}
