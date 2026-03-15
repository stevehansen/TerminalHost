using System.Diagnostics;
using System.IO;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Monitors UI thread responsiveness. A background timer pings the UI thread
/// via BeginInvoke every few seconds. If the ping isn't acknowledged within
/// a threshold, it triggers a debugger break (if attached) or logs diagnostics.
/// This helps catch deadlocks and hangs in production.
/// </summary>
public sealed class UiThreadWatchdog : IDisposable
{
    private readonly IDispatcherService _dispatcher;
    private readonly System.Threading.Timer _timer;
    private readonly string _logPath;

    /// <summary>How often we ping the UI thread.</summary>
    private const int PingIntervalMs = 3_000;

    /// <summary>How long we wait for the UI thread to respond before flagging a hang.</summary>
    private const int HangThresholdMs = 5_000;

    /// <summary>Cooldown after a hang is detected to avoid spamming breaks/logs.</summary>
    private const int CooldownAfterHangMs = 30_000;

    private long _lastPingSentTicks;
    private long _lastPongReceivedTicks;
    private volatile bool _pingOutstanding;
    private volatile bool _disposed;
    private long _hangDetectedTicks;

    public UiThreadWatchdog(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logPath = Path.Combine(appData, "TerminalHost", "ui-hang.log");

        _lastPongReceivedTicks = Environment.TickCount64;
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _timer.Change(PingIntervalMs, PingIntervalMs);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnTick(object? state)
    {
        if (_disposed) return;

        var now = Environment.TickCount64;

        // Check if a previous ping is still outstanding
        if (_pingOutstanding)
        {
            var elapsed = now - _lastPingSentTicks;
            if (elapsed >= HangThresholdMs)
            {
                // Cooldown: don't fire repeatedly for the same hang episode
                if (now - _hangDetectedTicks < CooldownAfterHangMs)
                    return;

                _hangDetectedTicks = now;
                OnHangDetected(elapsed);
            }
            return;
        }

        // Send a new ping
        _lastPingSentTicks = now;
        _pingOutstanding = true;

        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                // This runs on the UI thread — the thread is alive
                _lastPongReceivedTicks = Environment.TickCount64;
                _pingOutstanding = false;
            });
        }
        catch
        {
            // Dispatcher may be shutting down
            _pingOutstanding = false;
        }
    }

    private void OnHangDetected(long elapsedMs)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = $"[{timestamp}] UI THREAD HANG DETECTED — unresponsive for {elapsedMs}ms";

        // If a debugger is attached, break so all thread stacks are visible
        if (Debugger.IsAttached)
        {
            Debug.WriteLine(message);
            Debug.WriteLine("Breaking into debugger — inspect thread stacks (Debug > Windows > Threads)");
            Debugger.Break();
            return;
        }

        // Otherwise log to file for post-mortem analysis
        try
        {
            var logDir = Path.GetDirectoryName(_logPath)!;
            Directory.CreateDirectory(logDir);

            var process = Process.GetCurrentProcess();
            var threadInfo = $"  Process threads: {process.Threads.Count}";
            var memInfo = $"  Working set: {process.WorkingSet64 / 1024 / 1024}MB";
            var poolInfo = GetThreadPoolInfo();

            var ioSnapshot = IoCounters.GetSnapshot();

            var entry = $"""
                {message}
                {threadInfo}
                {memInfo}
                {poolInfo}
                {ioSnapshot}
                  Hint: Attach Visual Studio to host.exe and reproduce to get full stack traces.
                  Or run: dotnet-dump collect -p {process.Id}
                ---
                """;

            File.AppendAllText(_logPath, entry + Environment.NewLine);
        }
        catch
        {
            // Best-effort logging
        }
    }

    private static string GetThreadPoolInfo()
    {
        ThreadPool.GetAvailableThreads(out var workerAvail, out var ioAvail);
        ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);
        var workerBusy = workerMax - workerAvail;
        var ioBusy = ioMax - ioAvail;
        return $"  ThreadPool: {workerBusy} busy workers (of {workerMax}), {ioBusy} busy IO (of {ioMax})";
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
