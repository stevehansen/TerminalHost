using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for pseudo-terminal (PTY) process management.
/// Platform-specific implementations handle the actual PTY creation.
/// </summary>
public interface IPtyService : IDisposable
{
    /// <summary>
    /// Fired when the PTY process exits.
    /// </summary>
    event EventHandler<int>? ProcessExited;

    /// <summary>
    /// Stream for reading output from the PTY.
    /// </summary>
    Stream? ReaderStream { get; }

    /// <summary>
    /// Stream for writing input to the PTY.
    /// </summary>
    Stream? WriterStream { get; }

    /// <summary>
    /// Whether the PTY process is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// The process ID of the PTY process.
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    /// Starts the PTY process.
    /// </summary>
    /// <param name="columns">Initial terminal width in columns.</param>
    /// <param name="rows">Initial terminal height in rows.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="command">Command to execute (null for default shell).</param>
    /// <param name="customPaths">Additional paths to add to PATH environment variable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(
        int columns,
        int rows,
        string? workingDirectory = null,
        string? command = null,
        IEnumerable<string>? customPaths = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resizes the PTY terminal.
    /// </summary>
    /// <param name="columns">New width in columns.</param>
    /// <param name="rows">New height in rows.</param>
    void Resize(int columns, int rows);

    /// <summary>
    /// Kills the PTY process.
    /// </summary>
    void Kill();

    /// <summary>
    /// Writes binary data to the PTY.
    /// </summary>
    Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes text to the PTY.
    /// </summary>
    Task WriteAsync(string text, CancellationToken cancellationToken = default);
}
