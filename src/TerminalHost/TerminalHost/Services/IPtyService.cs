using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalHost.Services;

public interface IPtyService : IDisposable
{
    event EventHandler<int>? ProcessExited;

    Stream? ReaderStream { get; }
    Stream? WriterStream { get; }
    bool IsRunning { get; }
    int? ProcessId { get; }

    Task StartAsync(int columns, int rows, string? workingDirectory = null, string? command = null, CancellationToken cancellationToken = default);
    void Resize(int columns, int rows);
    void Kill();
    Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);
    Task WriteAsync(string text, CancellationToken cancellationToken = default);
}
