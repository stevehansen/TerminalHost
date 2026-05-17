using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Windows implementation of <see cref="ICommandComposer"/>.
/// Knows about cmd.exe / pwsh.exe / powershell.exe / wsl.exe and PATHEXT-based resolution.
/// </summary>
public sealed class WindowsCommandComposer : ICommandComposer
{
    private static readonly HashSet<string> BuiltInShells = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "cmd.exe",
        "pwsh", "pwsh.exe",
        "powershell", "powershell.exe",
        "bash", "bash.exe",
        "wsl", "wsl.exe",
    };

    private readonly Lazy<string> _defaultShell;

    public WindowsCommandComposer()
    {
        _defaultShell = new Lazy<string>(ResolveDefaultShell, isThreadSafe: true);
    }

    public string DefaultShell => _defaultShell.Value;

    private string ResolveDefaultShell()
    {
        if (TryResolveExecutable("pwsh.exe", out _))
            return "pwsh.exe";
        if (TryResolveExecutable("powershell.exe", out _))
            return "powershell.exe";
        return "cmd.exe";
    }

    public bool IsBuiltInShell(string executable)
    {
        if (string.IsNullOrEmpty(executable))
            return false;

        var name = Path.GetFileName(executable);
        return BuiltInShells.Contains(name);
    }

    public bool TryResolveExecutable(string command, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrEmpty(command))
            return false;

        if (File.Exists(command))
        {
            fullPath = Path.GetFullPath(command);
            return true;
        }

        var extensions = BuildExtensions();
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var hasExt = !string.IsNullOrEmpty(Path.GetExtension(command));

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim();
            if (trimmed.Length == 0)
                continue;

            var direct = Path.Combine(trimmed, command);
            if (File.Exists(direct))
            {
                fullPath = direct;
                return true;
            }

            if (!hasExt)
            {
                foreach (var ext in extensions)
                {
                    var withExt = Path.Combine(trimmed, command + ext);
                    if (File.Exists(withExt))
                    {
                        fullPath = withExt;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildExtensions()
    {
        var pathext = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrEmpty(pathext))
            return new[] { ".COM", ".EXE", ".BAT", ".CMD" };
        return pathext.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    public string WithWorkingDirectory(string command, string workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
            return command;

        // Split into head (executable, possibly quoted) and tail (args).
        SplitHeadAndTail(command, out var head, out var tail);
        var headName = Path.GetFileName(StripQuotes(head));

        if (headName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
            headName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            // Chain the user's cmd args (e.g. "/c something") after the cd.
            return tail.Length == 0
                ? $"cmd.exe /K cd /d \"{workingDir}\""
                : $"cmd.exe /K cd /d \"{workingDir}\" && {head} {tail}";
        }

        if (headName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
            headName.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
            headName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
            headName.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            // Inject -NoExit -WorkingDirectory between head and tail so PowerShell sees them
            // as leading flags before any user-supplied args.
            return tail.Length == 0
                ? $"{head} -NoExit -WorkingDirectory \"{workingDir}\""
                : $"{head} -NoExit -WorkingDirectory \"{workingDir}\" {tail}";
        }

        return $"cmd.exe /K cd /d \"{workingDir}\" && {command}";
    }

    /// <summary>
    /// Splits <paramref name="command"/> into its first token (head — the executable) and
    /// the remainder (tail — arguments). If the head starts with a double-quote, the head
    /// extends to the matching closing quote so paths like "C:\Program Files\..." stay intact.
    /// </summary>
    private static void SplitHeadAndTail(string command, out string head, out string tail)
    {
        command = command.TrimStart();
        if (command.Length == 0)
        {
            head = string.Empty;
            tail = string.Empty;
            return;
        }

        if (command[0] == '"')
        {
            var closing = command.IndexOf('"', 1);
            if (closing > 0)
            {
                head = command.Substring(0, closing + 1);
                tail = command.Length > closing + 1 ? command.Substring(closing + 1).TrimStart() : string.Empty;
                return;
            }
        }

        var space = command.IndexOf(' ');
        if (space < 0)
        {
            head = command;
            tail = string.Empty;
        }
        else
        {
            head = command.Substring(0, space);
            tail = command.Substring(space + 1).TrimStart();
        }
    }

    private static string StripQuotes(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s.Substring(1, s.Length - 2);
        return s;
    }

    public string WithEnvironment(string command, IReadOnlyDictionary<string, string> env)
    {
        if (env.Count == 0)
            return command;

        var prefix = string.Join(" && ", env.Select(kv => $"set \"{kv.Key}={EscapeForSet(kv.Value)}\""));
        return $"{prefix} && {command}";
    }

    /// <summary>
    /// Escapes a value for use inside a cmd <c>set "K=V"</c> assignment. Doubles any
    /// embedded <c>"</c> so it doesn't close the quoted region early, and doubles
    /// <c>%</c> so cmd's variable-expansion pass treats it literally.
    /// </summary>
    private static string EscapeForSet(string value)
    {
        return value.Replace("%", "%%").Replace("\"", "\"\"");
    }
}
