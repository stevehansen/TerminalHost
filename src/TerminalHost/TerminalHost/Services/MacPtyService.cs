using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalHost.Services;

/// <summary>
/// macOS PTY implementation using a Python helper for proper resize support.
/// </summary>
public class MacPtyService : IPtyService
{
    // Resize command escape sequence: ESC]777;<cols>;<rows>BEL
    private const string ResizePrefix = "\x1b]777;";
    private const string ResizeSuffix = "\x07";

    private Process? _process;
    private int _columns;
    private int _rows;
    private bool _disposed;

    public event EventHandler<int>? ProcessExited;

    public Stream? ReaderStream => _process?.StandardOutput.BaseStream;
    public Stream? WriterStream => _process?.StandardInput.BaseStream;
    public bool IsRunning => _process != null && !_process.HasExited;
    public int? ProcessId => _process?.Id;

    public Task StartAsync(int columns, int rows, string? workingDirectory = null, string? command = null, CancellationToken cancellationToken = default)
    {
        _columns = columns;
        _rows = rows;

        var (executable, args) = GetCommandAndArgs(command);
        var helperPath = GetPtyHelperPath();
        var workDir = !string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Build the arguments for pty_helper.py
        // Format: pty_helper.py <cols> <rows> <command> [args...]
        var helperArgs = $"\"{helperPath}\" {columns} {rows} \"{executable}\"";
        if (!string.IsNullOrEmpty(args))
        {
            helperArgs += $" {args}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/python3",
            Arguments = helperArgs,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        };

        // Copy existing environment variables first
        foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
        {
            var key = env.Key?.ToString();
            var value = env.Value?.ToString();
            if (!string.IsNullOrEmpty(key))
            {
                startInfo.Environment[key] = value ?? "";
            }
        }

        // Override terminal-specific environment variables
        startInfo.Environment["TERM"] = "xterm-256color";
        startInfo.Environment["COLORTERM"] = "truecolor";
        startInfo.Environment["COLUMNS"] = columns.ToString();
        startInfo.Environment["LINES"] = rows.ToString();

        _process = new Process { StartInfo = startInfo };
        _process.EnableRaisingEvents = true;
        _process.Exited += (s, e) =>
        {
            ProcessExited?.Invoke(this, _process.ExitCode);
        };

        _process.Start();

        return Task.CompletedTask;
    }

    private static string GetPtyHelperPath()
    {
        var exePath = AppContext.BaseDirectory;

        // Try various paths
        var paths = new[]
        {
            Path.Combine(exePath, "Resources", "pty_helper.py"),
            Path.Combine(exePath, "pty_helper.py"),
            Path.Combine(exePath, "..", "..", "..", "Resources", "pty_helper.py"),
            Path.Combine(exePath, "..", "..", "..", "..", "..", "Resources", "pty_helper.py"),
        };

        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException($"pty_helper.py not found. Searched in: {string.Join(", ", paths.Select(Path.GetFullPath))}");
    }

    private static (string executable, string? args) GetCommandAndArgs(string? command)
    {
        // If command is specified, parse it
        if (!string.IsNullOrEmpty(command))
        {
            // Extract the executable and arguments from the command
            var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var executable = parts[0];
            var args = parts.Length > 1 ? parts[1] : null;

            // Try to resolve the executable path
            if (File.Exists(executable))
                return (executable, args);

            // Try to find in PATH
            var resolvedPath = FindInPath(executable);
            if (resolvedPath != null)
                return (resolvedPath, args);

            // If we can't find it, return as-is and let the system try
            return (executable, args);
        }

        // Default to user's shell with interactive/login flags
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
        {
            return (shell, "-i -l");
        }
        return ("/bin/zsh", "-i -l");
    }

    private static string? FindInPath(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(':');

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    public void Resize(int columns, int rows)
    {
        if (_process == null || _process.HasExited)
            return;

        if (_columns == columns && _rows == rows)
            return;

        _columns = columns;
        _rows = rows;

        try
        {
            // Send resize command to pty_helper via escape sequence
            var resizeCommand = $"{ResizePrefix}{columns};{rows}{ResizeSuffix}";
            var bytes = Encoding.UTF8.GetBytes(resizeCommand);
            _process.StandardInput.BaseStream.Write(bytes);
            _process.StandardInput.BaseStream.Flush();
        }
        catch
        {
        }
    }

    public void Kill()
    {
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (_process?.StandardInput?.BaseStream != null && !_process.HasExited)
        {
            try
            {
                await _process.StandardInput.BaseStream.WriteAsync(data, cancellationToken);
                await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            }
            catch
            {
            }
        }
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await WriteAsync(bytes, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            _process?.Dispose();
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}
