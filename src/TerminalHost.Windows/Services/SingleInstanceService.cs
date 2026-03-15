using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Services;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

public sealed class SingleInstanceService : ISingleInstanceService
{
    private const string MutexName = "TerminalHost_SingleInstance_Mutex";
    private const string PipeName = "TerminalHost_IPC_Pipe";

    // Message type prefixes for IPC protocol
    private const string MessageTypeCommand = "CMD:";
    private const string MessageTypeHook = "HOOK:";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeServerCts;
    private Task? _pipeServerTask;

    public event EventHandler<CommandLineArgs>? CommandReceived;
    public event EventHandler<HookEvent>? HookEventReceived;

    public bool TryAcquireLock()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        return createdNew;
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
                var message = await reader.ReadToEndAsync(cancellationToken);

                if (string.IsNullOrEmpty(message))
                    continue;

                Interlocked.Increment(ref IoCounters.PipeMessagesReceived);

                // Parse message based on type prefix
                if (message.StartsWith(MessageTypeHook))
                {
                    var json = message[MessageTypeHook.Length..];
                    var hookEvent = JsonSerializer.Deserialize<HookEvent>(json);
                    if (hookEvent != null)
                    {
                        HookEventReceived?.Invoke(this, hookEvent);
                    }
                }
                else if (message.StartsWith(MessageTypeCommand))
                {
                    var json = message[MessageTypeCommand.Length..];
                    var args = JsonSerializer.Deserialize<CommandLineArgs>(json);
                    if (args != null)
                    {
                        CommandReceived?.Invoke(this, args);
                    }
                }
                else
                {
                    // Legacy: assume it's a CommandLineArgs for backwards compatibility
                    var args = JsonSerializer.Deserialize<CommandLineArgs>(message);
                    if (args != null)
                    {
                        CommandReceived?.Invoke(this, args);
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

    public static bool SendToRunningInstance(CommandLineArgs args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);

            using var writer = new StreamWriter(client);
            var json = JsonSerializer.Serialize(args);
            // Use message prefix for protocol versioning
            writer.Write(MessageTypeCommand + json);
            writer.Flush();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a hook event to the running main instance.
    /// </summary>
    /// <param name="hookEvent">The hook event to send.</param>
    /// <returns>True if sent successfully, false if main instance not available.</returns>
    public static bool SendHookEventToRunningInstance(HookEvent hookEvent)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 1000); // Shorter timeout for hooks

            using var writer = new StreamWriter(client);
            var json = JsonSerializer.Serialize(hookEvent);
            writer.Write(MessageTypeHook + json);
            writer.Flush();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the main application instance is running by attempting to acquire the mutex.
    /// </summary>
    public bool IsMainInstanceRunning()
    {
        try
        {
            using var mutex = new Mutex(false, MutexName, out bool createdNew);
            if (createdNew)
            {
                // We created it, so no other instance has it - release immediately
                mutex.ReleaseMutex();
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Static version to check if main instance is running without creating a service instance.
    /// </summary>
    public static bool IsMainInstanceRunningStatic()
    {
        try
        {
            using var mutex = new Mutex(false, MutexName, out bool createdNew);
            if (createdNew)
            {
                mutex.ReleaseMutex();
                return false;
            }
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
    }
}
