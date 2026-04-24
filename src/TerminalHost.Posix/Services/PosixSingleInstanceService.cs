using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Posix.Services;

/// <summary>
/// Base class for POSIX single instance services (macOS, Linux).
/// Uses a named pipe for IPC and a file lock (with Mutex fallback) to
/// ensure only one application instance runs at a time.
/// </summary>
public class PosixSingleInstanceService : ISingleInstanceService
{
    private const string MutexName = "TerminalHost_SingleInstance_Mutex";

    private static readonly string PipeName = Path.Combine(
        Path.GetTempPath(),
        "TerminalHost_IPC_Pipe");

    private static readonly string LockFilePath = Path.Combine(
        Path.GetTempPath(),
        "TerminalHost.lock");

    private Mutex? _mutex;
    private FileStream? _lockFileStream;
    private CancellationTokenSource? _pipeServerCts;
    private Task? _pipeServerTask;

    public event EventHandler<CommandLineArgs>? CommandReceived;
    public event EventHandler<HookEvent>? HookEventReceived;

    /// <summary>
    /// Tries a named Mutex first, falling back to an exclusive file lock
    /// if the runtime doesn't support named mutexes.
    /// </summary>
    public bool TryAcquireLock()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            return createdNew;
        }
        catch (PlatformNotSupportedException)
        {
            return TryAcquireFileLock();
        }
    }

    /// <summary>
    /// Checks whether the main instance holds the lock file.
    /// </summary>
    public bool IsMainInstanceRunning()
    {
        try
        {
            using var fs = new FileStream(
                LockFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// Starts the named pipe server loop to receive commands and hook events
    /// from secondary instances.
    /// </summary>
    public void StartPipeServer()
    {
        _pipeServerCts = new CancellationTokenSource();
        _pipeServerTask = Task.Run(() => PipeServerLoop(_pipeServerCts.Token));
    }

    /// <summary>
    /// Sends command line arguments to the running instance via named pipe.
    /// </summary>
    public static bool SendToRunningInstance(CommandLineArgs args)
        => SendJson(JsonSerializer.Serialize(args));

    /// <summary>
    /// Sends a hook event to the running instance via named pipe.
    /// </summary>
    public static bool SendHookEvent(HookEvent hookEvent)
        => SendJson(JsonSerializer.Serialize(hookEvent));

    public void Dispose()
    {
        _pipeServerCts?.Cancel();

        // Unblock WaitForConnectionAsync with a dummy connection
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 100);
        }
        catch { }

        _pipeServerTask?.Wait(TimeSpan.FromSeconds(2));
        _pipeServerCts?.Dispose();
        _mutex?.Dispose();
        _lockFileStream?.Dispose();

        try
        {
            if (_lockFileStream != null && File.Exists(LockFilePath))
                File.Delete(LockFilePath);
        }
        catch { }
    }

    #region Private helpers

    private bool TryAcquireFileLock()
    {
        try
        {
            _lockFileStream = new FileStream(
                LockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task PipeServerLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server);
                var json = await reader.ReadToEndAsync(cancellationToken);

                if (!string.IsNullOrEmpty(json))
                    DispatchMessage(json);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private void DispatchMessage(string json)
    {
        // Try HookEvent first — it has a distinguishing EventType property
        try
        {
            var hookEvent = JsonSerializer.Deserialize<HookEvent>(json);
            if (hookEvent != null && hookEvent.EventType != default)
            {
                HookEventReceived?.Invoke(this, hookEvent);
                return;
            }
        }
        catch (JsonException) { }

        // Fall back to CommandLineArgs
        try
        {
            var args = JsonSerializer.Deserialize<CommandLineArgs>(json);
            if (args != null)
                CommandReceived?.Invoke(this, args);
        }
        catch (JsonException) { }
    }

    private static bool SendJson(string json)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);

            using var writer = new StreamWriter(client);
            writer.Write(json);
            writer.Flush();

            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
