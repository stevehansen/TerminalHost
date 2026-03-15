using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TerminalHost.Core.Services;

/// <summary>
/// Lightweight startup profiler that writes timestamped milestones to startup.log.
/// Measures wall-clock time from first creation to identify where startup time is spent.
/// </summary>
public sealed class StartupProfiler
{
    private static StartupProfiler? _instance;
    public static StartupProfiler Instance => _instance ??= new StartupProfiler();

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly StringBuilder _buffer = new(4096);
    private readonly string _logPath;
    private long _lastMilestoneMs;

    private StartupProfiler()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logPath = Path.Combine(appData, "TerminalHost", "startup.log");
        _lastMilestoneMs = 0;

        // Clear previous log
        try
        {
            var dir = Path.GetDirectoryName(_logPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_logPath, $"=== Startup {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>
    /// Logs a milestone with elapsed time since start and delta since last milestone.
    /// </summary>
    public void Log(string message)
    {
        var elapsedMs = _sw.ElapsedMilliseconds;
        var deltaMs = elapsedMs - _lastMilestoneMs;
        _lastMilestoneMs = elapsedMs;

        var line = $"[{elapsedMs,7}ms +{deltaMs,5}ms] {message}";

        lock (_buffer)
        {
            _buffer.AppendLine(line);
        }
    }

    /// <summary>
    /// Logs a milestone and returns a disposable that logs completion with duration.
    /// Usage: using (StartupProfiler.Instance.Measure("CreateTerminals")) { ... }
    /// </summary>
    public MeasureScope Measure(string operation)
    {
        Log($"{operation} — start");
        return new MeasureScope(this, operation, _sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Flushes the buffered log to disk. Call once at end of startup.
    /// </summary>
    public void Flush()
    {
        Log("=== Flush (startup complete) ===");
        string content;
        lock (_buffer)
        {
            content = _buffer.ToString();
            _buffer.Clear();
        }

        try
        {
            File.AppendAllText(_logPath, content);
        }
        catch { }
    }

    public readonly struct MeasureScope : IDisposable
    {
        private readonly StartupProfiler _profiler;
        private readonly string _operation;
        private readonly long _startMs;

        internal MeasureScope(StartupProfiler profiler, string operation, long startMs)
        {
            _profiler = profiler;
            _operation = operation;
            _startMs = startMs;
        }

        public void Dispose()
        {
            var durationMs = _profiler._sw.ElapsedMilliseconds - _startMs;
            _profiler.Log($"{_operation} — done ({durationMs}ms)");
        }
    }
}
