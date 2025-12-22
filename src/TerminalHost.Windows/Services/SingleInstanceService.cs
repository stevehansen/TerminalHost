using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

public sealed class SingleInstanceService : ISingleInstanceService
{
    private const string MutexName = "TerminalHost_SingleInstance_Mutex";
    private const string PipeName = "TerminalHost_IPC_Pipe";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeServerCts;
    private Task? _pipeServerTask;

    public event EventHandler<CommandLineArgs>? CommandReceived;

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
                var json = await reader.ReadToEndAsync(cancellationToken);

                if (!string.IsNullOrEmpty(json))
                {
                    var args = JsonSerializer.Deserialize<CommandLineArgs>(json);
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
    }
}
