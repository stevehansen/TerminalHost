using System.Diagnostics;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class GitProcessRunner : IGitProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public Task<string?> RunGitCommandAsync(string workingDirectory, string arguments)
    {
        return RunGitCommandAsync(workingDirectory, arguments, DefaultTimeout, CancellationToken.None);
    }

    public async Task<string?> RunGitCommandAsync(string workingDirectory, string arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Use a combined cancellation token with timeout
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                await process.WaitForExitAsync(linkedCts.Token);
                var output = await outputTask;

                return process.ExitCode == 0 ? output : null;
            }
            catch (OperationCanceledException)
            {
                // Kill the process if it's still running
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore errors when killing
                }

                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    public async Task<GitOperationResult> RunGitOperationAsync(string workingDirectory, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new GitOperationResult
            {
                Success = process.ExitCode == 0,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (Exception ex)
        {
            return new GitOperationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<GitOperationResult> RunGitOperationWithStdinAsync(string workingDirectory, string arguments, string stdinContent)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            await process.StandardInput.WriteAsync(stdinContent);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new GitOperationResult
            {
                Success = process.ExitCode == 0,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (Exception ex)
        {
            return new GitOperationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}
