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
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workDir = GetValidWorkingDirectory(workingDirectory, homeDir);

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

        // Ensure PATH includes common macOS locations (app bundles have minimal PATH)
        var currentPath = startInfo.Environment.TryGetValue("PATH", out var existingPath) ? existingPath : "";
        var additionalPaths = new List<string>
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
        };

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
            catch
            {
                // Ignore errors in NVM detection
            }
        }

        // Build comprehensive PATH, avoiding duplicates
        // Iterate in reverse so first items in additionalPaths end up first in PATH
        var pathParts = currentPath.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = additionalPaths.Count - 1; i >= 0; i--)
        {
            var path = additionalPaths[i];
            if (!pathParts.Contains(path) && Directory.Exists(path))
            {
                pathParts.Insert(0, path); // Prepend to give priority
            }
        }
        startInfo.Environment["PATH"] = string.Join(":", pathParts);

        // Override terminal-specific environment variables
        startInfo.Environment["TERM"] = "xterm-256color";
        startInfo.Environment["COLORTERM"] = "truecolor";
        startInfo.Environment["COLUMNS"] = columns.ToString();
        startInfo.Environment["LINES"] = rows.ToString();
        startInfo.Environment["HOME"] = homeDir;

        // Set up NVM environment so it initializes correctly in the shell
        var nvmDir = Path.Combine(homeDir, ".nvm");
        if (Directory.Exists(nvmDir))
        {
            startInfo.Environment["NVM_DIR"] = nvmDir;
        }

        // Ensure SHELL is set (needed for some tools)
        if (!startInfo.Environment.ContainsKey("SHELL") || string.IsNullOrEmpty(startInfo.Environment["SHELL"]))
        {
            var defaultShell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
            startInfo.Environment["SHELL"] = defaultShell;
        }

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

    /// <summary>
    /// Gets a valid working directory, avoiding app bundles and DMG mounts.
    /// </summary>
    private static string GetValidWorkingDirectory(string? requestedDir, string homeDir)
    {
        // List of path patterns that indicate invalid locations
        var invalidPatterns = new[]
        {
            "/Volumes/",              // DMG mount points
            ".app/Contents/",         // Inside app bundles
        };

        // Check if requested directory is valid
        if (!string.IsNullOrEmpty(requestedDir) && Directory.Exists(requestedDir))
        {
            var fullPath = Path.GetFullPath(requestedDir);
            var isInvalid = invalidPatterns.Any(pattern =>
                fullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (!isInvalid)
            {
                return fullPath;
            }
        }

        // Fall back to home directory
        if (Directory.Exists(homeDir))
        {
            return homeDir;
        }

        // Last resort fallback
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
