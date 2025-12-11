using System.Diagnostics;
using System.IO;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class GitStatusService
{
    public async Task<GitStatus> GetGitStatusAsync(string workingDirectory)
    {
        var status = new GitStatus();

        if (!Directory.Exists(workingDirectory))
            return status;

        // Check if it's a git repository
        var gitDir = await RunGitCommandAsync(workingDirectory, "rev-parse --git-dir");
        if (gitDir == null)
            return status;

        status.IsGitRepository = true;

        // Get branch name
        var branch = await RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD");
        status.BranchName = branch?.Trim() ?? "";

        // Handle detached HEAD
        if (status.BranchName == "HEAD")
        {
            var shortSha = await RunGitCommandAsync(workingDirectory, "rev-parse --short HEAD");
            status.BranchName = shortSha?.Trim() ?? "HEAD";
        }

        // Check dirty status
        var porcelain = await RunGitCommandAsync(workingDirectory, "status --porcelain");
        status.IsDirty = !string.IsNullOrWhiteSpace(porcelain);

        // Get ahead/behind counts (may fail if no upstream)
        var ahead = await RunGitCommandAsync(workingDirectory, "rev-list --count @{u}..HEAD");
        if (ahead != null && int.TryParse(ahead.Trim(), out var aheadCount))
            status.AheadCount = aheadCount;

        var behind = await RunGitCommandAsync(workingDirectory, "rev-list --count HEAD..@{u}");
        if (behind != null && int.TryParse(behind.Trim(), out var behindCount))
            status.BehindCount = behindCount;

        return status;
    }

    private static async Task<string?> RunGitCommandAsync(string workingDirectory, string arguments)
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

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
