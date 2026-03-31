using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PtySharp.Linux;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Linux.Services;

/// <summary>
/// Linux PTY implementation that delegates to PtySharp.Linux's PtySession
/// and adds TerminalHost-specific environment setup and command resolution.
/// </summary>
public class LinuxPtyService : IPtyService
{
    private PtySession? _session;
    private bool _disposed;

    public event EventHandler<int>? ProcessExited;

    public Stream? ReaderStream => _session?.ReaderStream;
    public Stream? WriterStream => _session?.WriterStream;
    public bool IsRunning => _session?.IsRunning ?? false;
    public int? ProcessId => null; // setsid --fork detaches; PID not trackable

    public Task StartAsync(int columns, int rows, string? workingDirectory = null, string? command = null, IEnumerable<string>? customPaths = null, CancellationToken cancellationToken = default)
    {
        var (executable, args) = GetCommandAndArgs(command);
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workDir = GetValidWorkingDirectory(workingDirectory, homeDir);
        var env = BuildEnvironment(columns, rows, homeDir, customPaths);

        _session = new PtySession();
        _session.Exited += exitCode => ProcessExited?.Invoke(this, exitCode);

        var envDict = new Dictionary<string, string>(env.Count);
        foreach (var (key, value) in env)
            envDict[key] = value;

        _session.Start(
            command: executable,
            arguments: args,
            workingDirectory: workDir,
            environment: envDict,
            rows: (ushort)rows,
            columns: (ushort)columns);

        return Task.CompletedTask;
    }

    public void Resize(int columns, int rows)
    {
        _session?.Resize((ushort)rows, (ushort)columns);
    }

    public void Kill()
    {
        _session?.Kill();
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (_session?.WriterStream != null && IsRunning)
        {
            try
            {
                await _session.WriterStream.WriteAsync(data, cancellationToken);
                await _session.WriterStream.FlushAsync(cancellationToken);
            }
            catch { }
        }
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        await WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Environment and command building

    private static List<(string Key, string Value)> BuildEnvironment(int columns, int rows, string homeDir, IEnumerable<string>? customPaths)
    {
        var env = new List<(string, string)>();

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            var value = entry.Value?.ToString();
            if (!string.IsNullOrEmpty(key))
                env.Add((key, value ?? ""));
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var additionalPaths = new List<string>();

        if (customPaths != null)
            additionalPaths.AddRange(customPaths.Where(p => !string.IsNullOrWhiteSpace(p)));

        additionalPaths.AddRange(new[]
        {
            $"{homeDir}/.local/bin",
            "/usr/local/bin",
            "/usr/local/sbin",
            "/usr/bin",
            "/bin",
            "/usr/sbin",
            "/sbin",
            "/snap/bin",
            $"{homeDir}/.cargo/bin",
            $"{homeDir}/.npm-global/bin",
        });

        var nvmVersionsDir = Path.Combine(homeDir, ".nvm", "versions", "node");
        if (Directory.Exists(nvmVersionsDir))
        {
            try
            {
                var versions = Directory.GetDirectories(nvmVersionsDir)
                    .Select(Path.GetFileName)
                    .Where(v => v != null)
                    .OrderByDescending(v => ParseNodeVersion(v!))
                    .ToList();

                foreach (var version in versions)
                {
                    var nodeBin = Path.Combine(nvmVersionsDir, version!, "bin");
                    if (Directory.Exists(nodeBin))
                    {
                        additionalPaths.Insert(0, nodeBin);
                        break;
                    }
                }
            }
            catch { }
        }

        var pathParts = currentPath.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = additionalPaths.Count - 1; i >= 0; i--)
        {
            var path = additionalPaths[i];
            if (!pathParts.Contains(path) && Directory.Exists(path))
                pathParts.Insert(0, path);
        }

        env.RemoveAll(e => e.Item1 == "PATH");
        env.Add(("PATH", string.Join(":", pathParts)));

        SetOrReplace(env, "TERM", "xterm-256color");
        SetOrReplace(env, "COLORTERM", "truecolor");
        SetOrReplace(env, "COLUMNS", columns.ToString());
        SetOrReplace(env, "LINES", rows.ToString());
        SetOrReplace(env, "HOME", homeDir);

        var nvmDir = Path.Combine(homeDir, ".nvm");
        if (Directory.Exists(nvmDir))
            SetOrReplace(env, "NVM_DIR", nvmDir);

        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell))
        {
            var defaultShell = File.Exists("/bin/bash") ? "/bin/bash" : (FindExecutable("bash") ?? "/bin/sh");
            SetOrReplace(env, "SHELL", defaultShell);
        }

        return env;
    }

    private static void SetOrReplace(List<(string Key, string Value)> env, string key, string value)
    {
        env.RemoveAll(e => e.Item1 == key);
        env.Add((key, value));
    }

    private static (string executable, string? args) GetCommandAndArgs(string? command)
    {
        if (!string.IsNullOrEmpty(command))
        {
            var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var executable = parts[0];
            var args = parts.Length > 1 ? parts[1] : null;

            if (File.Exists(executable))
                return (executable, args);

            var resolvedPath = FindInPath(executable);
            if (resolvedPath != null)
                return (resolvedPath, args);

            return (executable, args);
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return (shell, "-i -l");

        if (File.Exists("/bin/bash"))
            return ("/bin/bash", "-i -l");

        var bashPath = FindExecutable("bash");
        if (bashPath != null)
            return (bashPath, "-i -l");

        return ("/bin/sh", "-i -l");
    }

    private static string? FindInPath(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var path in pathEnv.Split(':'))
        {
            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }

    private static string? FindExecutable(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("which", name)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                    return output;
            }
        }
        catch { }
        return null;
    }

    private static string GetValidWorkingDirectory(string? requestedDir, string homeDir)
    {
        if (!string.IsNullOrEmpty(requestedDir) && Directory.Exists(requestedDir))
            return Path.GetFullPath(requestedDir);
        if (Directory.Exists(homeDir))
            return homeDir;
        return "/tmp";
    }

    private static (int major, int minor, int patch) ParseNodeVersion(string version)
    {
        try
        {
            var clean = version.TrimStart('v');
            var parts = clean.Split('.');
            var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
            var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
            return (major, minor, patch);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    #endregion
}
