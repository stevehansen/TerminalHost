using System.Collections.Generic;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Per-platform composer of shell-syntax-correct commands. Hides quoting,
/// env-var prefix syntax, built-in shell knowledge, and PATH resolution
/// behind a single port so callers don't switch on OS.
/// </summary>
public interface ICommandComposer
{
    /// <summary>Platform default shell (pwsh.exe / zsh / bash / etc).</summary>
    string DefaultShell { get; }

    /// <summary>True for cmd/pwsh/powershell on Windows; zsh/bash/sh/fish/tcsh/csh on POSIX.</summary>
    bool IsBuiltInShell(string executable);

    /// <summary>
    /// Resolves a command to a full path (PATH probe + platform-specific extensions on Windows).
    /// Returns true and populates <paramref name="fullPath"/> on success, false otherwise.
    /// </summary>
    bool TryResolveExecutable(string command, out string fullPath);

    /// <summary>
    /// Wraps a command so it runs in <paramref name="workingDir"/>. Returns the input
    /// unchanged on POSIX (PTYs cd natively). On Windows, wraps shells with their
    /// working-dir flags (cmd /K cd /d, pwsh -NoExit -WorkingDirectory).
    /// </summary>
    string WithWorkingDirectory(string command, string workingDir);

    /// <summary>
    /// Prefixes env vars to a command. POSIX: "K1=V1 K2=V2 cmd". Windows cmd:
    /// 'set "K1=V1" &amp;&amp; set "K2=V2" &amp;&amp; cmd'.
    /// <para>Iteration order over <paramref name="env"/> is preserved as the caller's
    /// dictionary iteration order (typically insertion order for the standard
    /// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/>).</para>
    /// <para>Values are NOT shell-quoted on POSIX; callers must avoid spaces and
    /// shell metacharacters in values, or quote them themselves. On Windows the
    /// composer escapes <c>"</c> and <c>%</c> inside <c>set</c> values, but other
    /// cmd metacharacters (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>^</c>, <c>|</c>)
    /// are protected only by the surrounding quotes and remain a caller
    /// responsibility for adversarial inputs.</para>
    /// </summary>
    string WithEnvironment(string command, IReadOnlyDictionary<string, string> env);
}
