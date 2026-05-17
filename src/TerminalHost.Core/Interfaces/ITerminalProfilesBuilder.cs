using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Builds the (custom, shell) profile pair for a new project tab and attaches
/// a container name when the workspace is configured for one. Pure construction:
/// no async work, no toasts, no fire-and-forget side effects. The caller still
/// owns "ensure container actually running" and UI feedback on failure.
/// </summary>
public interface ITerminalProfilesBuilder
{
    /// <summary>
    /// Construct the profile pair.
    /// </summary>
    /// <param name="workingDirectory">Project working directory; stamped onto both profiles.</param>
    /// <param name="aiAssistant">AI assistant whose Command/Name/Icon drive the custom profile.</param>
    /// <param name="settings">App settings supplying the shell command/name/icon.</param>
    /// <param name="wrapCustomInShell">
    /// When true, the custom terminal launches <c>settings.ShellCommand</c> and the AI CLI runs as
    /// a <see cref="Profile.StartupCommand"/> — the user can exit/restart the AI without losing the
    /// terminal (Avalonia default). When false, the custom terminal launches the AI CLI directly
    /// (WPF default, where the EasyTerminalControl host handles re-entry differently).
    /// </param>
    TerminalProfilesResult Build(
        string workingDirectory,
        AiAssistant aiAssistant,
        AppSettings settings,
        bool wrapCustomInShell);
}
