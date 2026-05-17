using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Posix.Services;

/// <summary>
/// POSIX (macOS + Linux) implementation of <see cref="ICommandComposer"/>.
/// Hides shell quoting, env-var prefix syntax, and PATH resolution behind a single port.
/// </summary>
public sealed class PosixCommandComposer : ICommandComposer
{
    private static readonly HashSet<string> BuiltInShells = new(StringComparer.OrdinalIgnoreCase)
    {
        "zsh", "bash", "sh", "fish", "tcsh", "csh",
    };

    private static readonly string[] CommonBinDirs =
    {
        "/bin",
        "/usr/bin",
        "/usr/local/bin",
        "/opt/homebrew/bin",
        "/usr/local/Homebrew/bin",
    };

    public string DefaultShell
    {
        get
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (!string.IsNullOrEmpty(shell))
                return shell;
            return "/bin/zsh";
        }
    }

    public bool IsBuiltInShell(string executable)
    {
        if (string.IsNullOrEmpty(executable))
            return false;

        // Match on the leaf name so full paths like "/bin/zsh" map to "zsh".
        // Avoids the historic EndsWith("sh") trap that classified pwsh / dash / xonsh
        // as built-ins and silently skipped MCP collab registration.
        var name = Path.GetFileName(executable);
        return BuiltInShells.Contains(name);
    }

    public bool TryResolveExecutable(string command, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrEmpty(command))
            return false;

        // Absolute path
        if (Path.IsPathRooted(command) && File.Exists(command))
        {
            fullPath = command;
            return true;
        }

        if (File.Exists(command))
        {
            fullPath = Path.GetFullPath(command);
            return true;
        }

        // Walk PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        // Common POSIX bin dirs (in case PATH was stripped, e.g. Finder/launchd-launched apps)
        foreach (var dir in CommonBinDirs)
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>POSIX PTYs cd to the working directory natively; pass the command through.</summary>
    public string WithWorkingDirectory(string command, string workingDir) => command;

    public string WithEnvironment(string command, IReadOnlyDictionary<string, string> env)
    {
        if (env.Count == 0)
            return command;

        var prefix = string.Join(" ", env.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{prefix} {command}";
    }
}
