using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Linux.Services;

/// <summary>
/// Linux implementation of single instance service using named pipes and file locks.
/// </summary>
public sealed class LinuxSingleInstanceService : ISingleInstanceService
{
    private const string MutexName = "TerminalHost_SingleInstance_Mutex";

    // Linux: Use /tmp for named pipes
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

    public bool TryAcquireLock()
    {
        try
        {
            // On Linux, named Mutex may not be supported depending on the runtime
            // Use a file-based lock as fallback
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            return createdNew;
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback: use a lock file
            return TryAcquireFileLock();
        }
    }

    private bool TryAcquireFileLock()
    {
        try
        {
            // Try to create exclusive lock file
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

    public bool IsMainInstanceRunning()
    {
        // Try to open the lock file - if we can't, main instance is running
        try
        {
            using var fs = new FileStream(
                LockFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false; // We got the lock, so no main instance
        }
        catch (FileNotFoundException)
        {
            return false; // No lock file, no main instance
        }
        catch (IOException)
        {
            return true; // Lock held by main instance
        }
    }

    public void StartPipeServer()
    {
        _pipeServerCts = new CancellationTokenSource();
        _pipeServerTask = Task.Run(() => PipeServerLoop(_pipeServerCts.Token));
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
                {
                    // Try to parse as CommandLineArgs first
                    try
                    {
                        var args = JsonSerializer.Deserialize<CommandLineArgs>(json);
                        if (args != null)
                        {
                            CommandReceived?.Invoke(this, args);
                            continue;
                        }
                    }
                    catch (JsonException)
                    {
                        // Try as HookEvent
                    }

                    // Try to parse as HookEvent
                    try
                    {
                        var hookEvent = JsonSerializer.Deserialize<HookEvent>(json);
                        if (hookEvent != null)
                        {
                            HookEventReceived?.Invoke(this, hookEvent);
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore unrecognized messages
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue listening on error
            }
        }
    }

    /// <summary>
    /// Sends command line arguments to the running instance.
    /// </summary>
    public static bool SendToRunningInstance(CommandLineArgs args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);

            using var writer = new StreamWriter(client);
            var json = JsonSerializer.Serialize(args);
            writer.Write(json);
            writer.Flush();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a hook event to the running instance.
    /// </summary>
    public static bool SendHookEvent(HookEvent hookEvent)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);

            using var writer = new StreamWriter(client);
            var json = JsonSerializer.Serialize(hookEvent);
            writer.Write(json);
            writer.Flush();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _pipeServerCts?.Cancel();

        // Unblock the waiting pipe server by making a dummy connection to itself.
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 100); // Use a short timeout
        }
        catch
        {
            // Ignore any exceptions. This is just to unblock the listener.
        }

        _pipeServerTask?.Wait(TimeSpan.FromSeconds(2)); // This should now complete without timing out.
        _pipeServerCts?.Dispose();
        _mutex?.Dispose();
        _lockFileStream?.Dispose();

        // Clean up the lock file when disposing
        try
        {
            if (_lockFileStream != null && File.Exists(LockFilePath))
            {
                File.Delete(LockFilePath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}