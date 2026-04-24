using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PtySharp;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Posix.Services;

/// <summary>
/// Arguments for starting a PTY session.
/// </summary>
public readonly record struct PtyStartArgs(
    string Executable,
    string[]? Arguments,
    string WorkingDirectory,
    Dictionary<string, string> Environment,
    ushort Rows,
    ushort Columns);

/// <summary>
/// Base class for POSIX PTY implementations (macOS, Linux).
/// Manages the PtySession lifecycle directly; subclasses provide
/// platform-specific environment configuration.
/// </summary>
public abstract class PosixPtyServiceBase<TSession, TSyscalls> : IPtyService
    where TSession : PtySession<TSyscalls>
    where TSyscalls : IPtySyscalls<TSyscalls>
{
    private TSession? _session;
    private bool _disposed;

    public event EventHandler<int>? ProcessExited;

    public Stream? ReaderStream => _session?.ReaderStream;
    public Stream? WriterStream => _session?.WriterStream;
    public bool IsRunning => _session?.IsRunning ?? false;
    public int? ProcessId => null;

    /// <summary>
    /// Creates a new PTY session instance. Subclasses implement this to call the
    /// platform-specific PtySession constructor.
    /// </summary>
    protected abstract TSession CreateSession(PtyStartArgs args);

    public Task StartAsync(int columns, int rows, string? workingDirectory = null, string? command = null, IEnumerable<string>? customPaths = null, CancellationToken cancellationToken = default)
    {
        ushort cols = (ushort)columns;
        ushort rowsU = (ushort)rows;

        var (executable, arguments) = GetCommandAndArgs(command);
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workDir = BuildWorkingDir(workingDirectory, homeDir);
        var env = BuildEnvironment(cols, rowsU, homeDir, customPaths);

        var envDict = new Dictionary<string, string>(env.Count);
        foreach (var (key, value) in env)
            envDict[key] = value;

        var args = new PtyStartArgs(executable, arguments, workDir, envDict, rowsU, cols);

        OnSessionStarting(ref args);

        _session = CreateSession(args);
        _session.Exited += exitCode => ProcessExited?.Invoke(this, exitCode);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called before the PtySession is created and started.
    /// Override to modify the start arguments.
    /// </summary>
    protected virtual void OnSessionStarting(ref PtyStartArgs args) { }

    public void Resize(int columns, int rows) => _session?.Resize((ushort)rows, (ushort)columns);

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (WriterStream != null && IsRunning)
        {
            try
            {
                await WriterStream.WriteAsync(data, cancellationToken);
                await WriterStream.FlushAsync(cancellationToken);
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

    #region Abstract environment hooks

    /// <summary>
    /// Returns platform-specific paths to prepend to PATH (e.g. Homebrew, Snap).
    /// </summary>
    protected abstract IEnumerable<string> GetPlatformPathDirectories(string homeDir);

    /// <summary>
    /// The default shell path, used when SHELL env var is not set.
    /// </summary>
    protected abstract string DefaultShell { get; }

    /// <summary>
    /// Validates the working directory. Override to reject platform-specific invalid paths.
    /// </summary>
    protected virtual string BuildWorkingDir(string? requestedDir, string homeDir)
    {
        if (!string.IsNullOrEmpty(requestedDir) && Directory.Exists(requestedDir))
            return Path.GetFullPath(requestedDir);
        if (Directory.Exists(homeDir))
            return homeDir;
        return "/tmp";
    }

    #endregion

    #region Shared environment and command building

    /// <summary>
    /// Builds the complete environment variable set for a new PTY session
    /// by inheriting the current process environment, then layering on
    /// PATH construction and terminal-specific overrides.
    /// </summary>
    private List<(string Key, string Value)> BuildEnvironment(ushort columns, ushort rows, string homeDir, IEnumerable<string>? customPaths)
    {
        var env = InheritCurrentEnvironment();

        BuildPath(env, homeDir, customPaths);
        SetTerminalVars(env, columns, rows, homeDir);

        return env;
    }

    /// <summary>
    /// Snapshots all environment variables from the current process.
    /// </summary>
    private static List<(string Key, string Value)> InheritCurrentEnvironment()
    {
        var env = new List<(string, string)>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            var value = entry.Value?.ToString();
            if (!string.IsNullOrEmpty(key))
                env.Add((key, value ?? ""));
        }
        return env;
    }

    /// <summary>
    /// Assembles the PATH variable from custom paths, platform-specific directories,
    /// common POSIX paths, and NVM node paths — deduplicating and filtering to existing directories.
    /// </summary>
    private void BuildPath(List<(string Key, string Value)> env, string homeDir, IEnumerable<string>? customPaths)
    {
        var additionalPaths = new List<string>();

        if (customPaths != null)
            additionalPaths.AddRange(customPaths.Where(p => !string.IsNullOrWhiteSpace(p)));

        additionalPaths.AddRange(GetPlatformPathDirectories(homeDir));

        additionalPaths.AddRange(new[]
        {
            $"{homeDir}/.local/bin",
            "/usr/local/bin",
            "/usr/local/sbin",
            "/usr/bin",
            "/bin",
            "/usr/sbin",
            "/sbin",
            $"{homeDir}/.cargo/bin",
            $"{homeDir}/.npm-global/bin",
        });

        if (customPaths != null && customPaths.Any())
            SetOrReplace(env, "TERMINALHOST_CUSTOM_PATHS", string.Join(":", customPaths.Where(p => !string.IsNullOrWhiteSpace(p))));

        PrependNvmNodePath(additionalPaths, homeDir);

        // Merge into PATH, avoiding duplicates
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathParts = currentPath.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = additionalPaths.Count - 1; i >= 0; i--)
        {
            var path = additionalPaths[i];
            if (!pathParts.Contains(path) && Directory.Exists(path))
                pathParts.Insert(0, path);
        }

        env.RemoveAll(e => e.Item1 == "PATH");
        env.Add(("PATH", string.Join(":", pathParts)));
    }

    /// <summary>
    /// Finds the latest installed NVM node version (by semver) and prepends
    /// its bin/ directory so node/npm resolve correctly.
    /// </summary>
    private static void PrependNvmNodePath(List<string> additionalPaths, string homeDir)
    {
        var nvmVersionsDir = Path.Combine(homeDir, ".nvm", "versions", "node");
        if (!Directory.Exists(nvmVersionsDir))
            return;

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

    /// <summary>
    /// Sets terminal-specific environment variables: TERM, COLORTERM, dimensions,
    /// HOME, NVM_DIR, and SHELL (fallback to DefaultShell if unset).
    /// </summary>
    private void SetTerminalVars(List<(string Key, string Value)> env, ushort columns, ushort rows, string homeDir)
    {
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
            SetOrReplace(env, "SHELL", DefaultShell);
    }

    /// <summary>
    /// Splits a command string into executable and arguments, resolving
    /// the executable against PATH. Falls back to DefaultShell when no command is given.
    /// </summary>
    private (string executable, string[]? args) GetCommandAndArgs(string? command)
    {
        if (!string.IsNullOrEmpty(command))
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var executable = parts[0];
            var args = parts.Length > 1 ? parts[1..] : null;

            if (File.Exists(executable))
                return (executable, args);

            var resolvedPath = FindInPath(executable);
            if (resolvedPath != null)
                return (resolvedPath, args);

            return (executable, args);
        }

        return (DefaultShell, null);
    }

    /// <summary>
    /// Searches each directory in $PATH for the given command name.
    /// Returns the full path if found, null otherwise.
    /// </summary>
    protected static string? FindInPath(string command)
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
    /// Upserts a key-value pair in the environment list, removing any existing entry for the key.
    /// </summary>
    protected static void SetOrReplace(List<(string Key, string Value)> env, string key, string value)
    {
        env.RemoveAll(e => e.Item1 == key);
        env.Add((key, value));
    }

    /// <summary>
    /// Parses a node version string (e.g. "v18.17.1") into a (major, minor, patch) tuple for sorting.
    /// </summary>
    protected static (int major, int minor, int patch) ParseNodeVersion(string version)
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
