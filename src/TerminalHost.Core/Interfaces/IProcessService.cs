using System.Diagnostics;
using System.Text;

namespace TerminalHost.Core.Interfaces;

public interface IProcessService
{
    void Start(string fileName);
    void Start(string fileName, string arguments) => Start(new ProcessStartInfo(fileName, arguments));
    void Start(ProcessStartInfo startInfo);

    /// <summary>
    /// Opens a folder in the system file manager.
    /// </summary>
    void OpenFolder(string path) { }

    /// <summary>
    /// Opens the folder containing the specified file and selects it in the file manager.
    /// </summary>
    void RevealInFolder(string filePath) { }

    /// <summary>
    /// Runs a command asynchronously and captures output.
    /// </summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="workingDirectory">Working directory (optional).</param>
    /// <param name="stdin">Content to write to stdin (optional).</param>
    /// <param name="timeout">Timeout (defaults to 60 seconds).</param>
    /// <returns>Tuple of (exitCode, stdout, stderr).</returns>
    async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string arguments, string? workingDirectory = null,
        string? stdin = null, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return (-1, "", "Process timed out");
        }
    }

    /// <summary>
    /// Runs a command asynchronously with streaming stdout callback.
    /// Each line of stdout triggers <paramref name="onOutput"/>.
    /// Returns the same tuple as <see cref="RunAsync"/>.
    /// </summary>
    async Task<(int ExitCode, string Output, string Error)> RunStreamingAsync(
        string fileName, string arguments, string? workingDirectory = null,
        string? stdin = null, TimeSpan? timeout = null,
        Action<string>? onOutput = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var outputBuilder = new System.Text.StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onOutput?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, outputBuilder.ToString(), await errorTask);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return (-1, outputBuilder.ToString(), "Process timed out");
        }
    }
}
