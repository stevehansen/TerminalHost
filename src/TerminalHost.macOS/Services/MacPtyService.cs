using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PtySharp.macOS;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.macOS.Services;

/// <summary>
/// macOS PTY implementation using PtySharp native PTY library.
/// </summary>
public class MacPtyService : IPtyService
{
    private PtySession? _session;
    private bool _disposed;

    public event EventHandler<int>? ProcessExited;

    public Stream? ReaderStream => _session?.ReaderStream;
    public Stream? WriterStream => _session?.WriterStream;
    public bool IsRunning => _session?.IsRunning ?? false;
    public int? ProcessId => null;

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
            $"{homeDir}/.local/bin",           // Claude CLI, user-installed tools
            "/opt/homebrew/bin",               // Homebrew on Apple Silicon
            "/opt/homebrew/sbin",
            "/usr/local/bin",                  // Homebrew on Intel, user tools
            "/usr/local/sbin",
            "/usr/bin",
            "/bin",
            "/usr/sbin",
            "/sbin",
            $"{homeDir}/.cargo/bin",           // Rust tools
            $"{homeDir}/.npm-global/bin",      // npm global packages
            "/opt/local/bin",                  // MacPorts
        });

        // Pass custom paths to shell via environment variable
        if (customPaths != null && customPaths.Any())
        {
            SetOrReplace(env, "TERMINALHOST_CUSTOM_PATHS", string.Join(":", customPaths.Where(p => !string.IsNullOrWhiteSpace(p))));
        }

        // Add NVM node paths if available (find latest version using semantic versioning)
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

        // Build comprehensive PATH, avoiding duplicates
        var pathParts = currentPath.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = additionalPaths.Count - 1; i >= 0; i--)
        {
            var path = additionalPaths[i];
            if (!pathParts.Contains(path) && Directory.Exists(path))
                pathParts.Insert(0, path);
        }

        env.RemoveAll(e => e.Item1 == "PATH");
        env.Add(("PATH", string.Join(":", pathParts)));

        // Override terminal-specific environment variables
        SetOrReplace(env, "TERM", "xterm-256color");
        SetOrReplace(env, "COLORTERM", "truecolor");
        SetOrReplace(env, "COLUMNS", columns.ToString());
        SetOrReplace(env, "LINES", rows.ToString());
        SetOrReplace(env, "HOME", homeDir);

        // Set up NVM environment so it initializes correctly in the shell
        var nvmDir = Path.Combine(homeDir, ".nvm");
        if (Directory.Exists(nvmDir))
            SetOrReplace(env, "NVM_DIR", nvmDir);

        // Ensure SHELL is set (needed for some tools)
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell))
        {
            var defaultShell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
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

        // Default to user's shell with interactive/login flags
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return (shell, "-i -l");

        return ("/bin/zsh", "-i -l");
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

    /// <summary>
    /// Gets a valid working directory, avoiding app bundles and DMG mounts.
    /// </summary>
    private static string GetValidWorkingDirectory(string? requestedDir, string homeDir)
    {
        var invalidPatterns = new[]
        {
            "/Volumes/",              // DMG mount points
            ".app/Contents/",         // Inside app bundles
        };

        if (!string.IsNullOrEmpty(requestedDir) && Directory.Exists(requestedDir))
        {
            var fullPath = Path.GetFullPath(requestedDir);
            var isInvalid = invalidPatterns.Any(pattern =>
                fullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (!isInvalid)
                return fullPath;
        }

        if (Directory.Exists(homeDir))
            return homeDir;

        return "/tmp";
    }

    /// <summary>
    /// Parses a node version string like "v24.6.0" into a comparable tuple.
    /// </summary>
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
