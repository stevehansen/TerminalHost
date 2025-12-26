using System.IO;
using System.Text;
using System.Text.Json;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Persists Claude Code hook events to a JSONL file for offline processing.
/// Uses file locking to support concurrent writes from multiple hook invocations.
/// </summary>
public sealed class HookEventQueue : IHookEventQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string QueueFilePath { get; }

    public HookEventQueue() : this(null) { }

    public HookEventQueue(string? customPath)
    {
        QueueFilePath = customPath ?? GetDefaultQueuePath();
        EnsureDirectoryExists();
    }

    private static string GetDefaultQueuePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TerminalHost", "hook-queue.jsonl");
    }

    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(QueueFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task EnqueueAsync(HookEvent hookEvent)
    {
        var json = JsonSerializer.Serialize(hookEvent, JsonOptions);
        var line = json + Environment.NewLine;

        // Use file locking for concurrent access from multiple hook invocations
        await WithFileLockAsync(QueueFilePath, async stream =>
        {
            stream.Seek(0, SeekOrigin.End);
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        });
    }

    /// <summary>
    /// Synchronous version of EnqueueAsync for use in hook handlers where async is problematic.
    /// </summary>
    public void EnqueueSync(HookEvent hookEvent)
    {
        var json = JsonSerializer.Serialize(hookEvent, JsonOptions);
        var line = json + Environment.NewLine;

        // Simple synchronous file append with retry
        const int maxRetries = 5;
        const int baseDelayMs = 50;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                using var stream = new FileStream(
                    QueueFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: false);

                stream.Seek(0, SeekOrigin.End);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                return;
            }
            catch (IOException) when (retry < maxRetries - 1)
            {
                // File is locked, wait and retry
                Thread.Sleep(baseDelayMs * (1 << retry));
            }
        }
    }

    public async Task<IReadOnlyList<HookEvent>> DequeueAllAsync()
    {
        var events = new List<HookEvent>();

        if (!File.Exists(QueueFilePath))
            return events;

        await WithFileLockAsync(QueueFilePath, async stream =>
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var hookEvent = JsonSerializer.Deserialize<HookEvent>(line, JsonOptions);
                    if (hookEvent != null)
                    {
                        events.Add(hookEvent);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            // Clear the file after reading
            stream.SetLength(0);
            await stream.FlushAsync();
        });

        return events;
    }

    public async Task<int> GetPendingCountAsync()
    {
        if (!File.Exists(QueueFilePath))
            return 0;

        var count = 0;
        await WithFileLockAsync(QueueFilePath, async stream =>
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    count++;
            }
        });

        return count;
    }

    public async Task ClearAsync()
    {
        if (!File.Exists(QueueFilePath))
            return;

        await WithFileLockAsync(QueueFilePath, stream =>
        {
            stream.SetLength(0);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Executes an action with exclusive file access using a lock file.
    /// Retries on contention with exponential backoff.
    /// </summary>
    private static async Task WithFileLockAsync(string filePath, Func<FileStream, Task> action)
    {
        const int maxRetries = 10;
        const int baseDelayMs = 50;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                await using var stream = new FileStream(
                    filePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,  // Exclusive access
                    bufferSize: 4096,
                    useAsync: true);

                await action(stream);
                return;
            }
            catch (IOException) when (retry < maxRetries - 1)
            {
                // File is locked by another process, wait and retry
                var delay = baseDelayMs * (1 << retry); // Exponential backoff
                await Task.Delay(Math.Min(delay, 1000)); // Cap at 1 second
            }
        }

        throw new IOException($"Failed to acquire lock on {filePath} after {maxRetries} retries");
    }
}
