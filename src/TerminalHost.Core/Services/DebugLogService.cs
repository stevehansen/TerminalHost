using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Ring-buffer backed debug log service. Thread-safe.
/// </summary>
public class DebugLogService : IDebugLogService
{
    private readonly DebugLogEntry[] _buffer;
    private readonly object _lock = new();
    private int _head;
    private int _count;

    public DebugLogService(int capacity = 500)
    {
        _buffer = new DebugLogEntry[capacity];
    }

    public void Log(string source, string message) => Add(DebugLogLevel.Info, source, message);
    public void Warn(string source, string message) => Add(DebugLogLevel.Warning, source, message);
    public void Error(string source, string message) => Add(DebugLogLevel.Error, source, message);

    public IReadOnlyList<DebugLogEntry> RecentEntries
    {
        get
        {
            lock (_lock)
            {
                var result = new DebugLogEntry[_count];
                for (var i = 0; i < _count; i++)
                {
                    var idx = (_head - 1 - i + _buffer.Length) % _buffer.Length;
                    result[i] = _buffer[idx];
                }
                return result;
            }
        }
    }

    public event Action<DebugLogEntry>? EntryAdded;

    public void Clear()
    {
        lock (_lock)
        {
            _count = 0;
            _head = 0;
        }
    }

    private void Add(DebugLogLevel level, string source, string message)
    {
        var entry = new DebugLogEntry { Level = level, Source = source, Message = message };
        lock (_lock)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
        EntryAdded?.Invoke(entry);
    }
}
