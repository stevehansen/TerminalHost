using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace TerminalHost.Services;

public class SingleInstanceService : IDisposable
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
        _pipeServerTask?.Wait(TimeSpan.FromSeconds(2));
        _pipeServerCts?.Dispose();
        _mutex?.Dispose();
    }
}

public class CommandLineArgs
{
    public string? ProfileId { get; set; }
    public string? Command { get; set; }
    public string? WorkingDir { get; set; }

    public static CommandLineArgs Parse(string[] args)
    {
        var result = new CommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Handle named arguments
            switch (arg.ToLowerInvariant())
            {
                case "--profile":
                case "-p":
                    if (i + 1 < args.Length)
                        result.ProfileId = args[++i];
                    continue;

                case "--command":
                case "-c":
                    if (i + 1 < args.Length)
                        result.Command = args[++i];
                    continue;

                case "--workdir":
                case "-w":
                    if (i + 1 < args.Length)
                        result.WorkingDir = ResolveDirectory(args[++i]);
                    continue;
            }

            // Handle positional argument (first non-flag argument is treated as directory)
            if (!arg.StartsWith("-") && string.IsNullOrEmpty(result.WorkingDir))
            {
                result.WorkingDir = ResolveDirectory(arg);
            }
        }

        return result;
    }

    private static string ResolveDirectory(string path)
    {
        // Handle "." and relative paths
        if (path == ".")
        {
            return System.IO.Directory.GetCurrentDirectory();
        }

        // Check if it's a relative path and make it absolute
        if (!System.IO.Path.IsPathRooted(path))
        {
            return System.IO.Path.GetFullPath(path);
        }

        return path;
    }

    public bool HasValidRequest()
    {
        return !string.IsNullOrEmpty(WorkingDir);
    }
}
