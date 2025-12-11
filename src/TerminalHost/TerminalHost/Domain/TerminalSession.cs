using System.Windows.Controls;
using EasyWindowsTerminalControl;

namespace TerminalHost.Domain;

public class TerminalSession : IDisposable
{
    public Guid Id { get; }
    public Profile Profile { get; }
    public SessionState State { get; private set; }
    public int? ExitCode { get; private set; }
    public ContentControl? TerminalControl { get; set; }

    private EasyTerminalControl? _easyTerminalControl;

    public event EventHandler<int>? ProcessExited;

    public TerminalSession(Profile profile)
    {
        Id = Guid.NewGuid();
        Profile = profile;
        State = SessionState.Running;
    }

    public void SetTerminalControl(EasyTerminalControl control)
    {
        _easyTerminalControl = control;
        TerminalControl = control;
    }

    public void MarkAsExited(int exitCode)
    {
        State = SessionState.Exited;
        ExitCode = exitCode;
        ProcessExited?.Invoke(this, exitCode);
    }

    public void Terminate()
    {
        try
        {
            // Try to kill the process through the IProcess interface
            if (State == SessionState.Running)
            {
                var termPty = _easyTerminalControl?.ConPTYTerm;
                var process = termPty?.Process;
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
        }
        catch
        {
            // Ignore errors during termination
        }
        finally
        {
            State = SessionState.Exited;
        }
    }

    public bool IsProcessRunning()
    {
        if (State == SessionState.Exited)
            return false;

        try
        {
            var termPty = _easyTerminalControl?.ConPTYTerm;
            return termPty?.Process != null && !termPty.Process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends text to the terminal as if typed by the user.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <param name="appendNewline">If true, appends a newline to execute the command.</param>
    /// <param name="newlineChar">The newline character to use (default: \r for shells).</param>
    /// <param name="useUserInput">If true, uses internal UserInput method (for apps like Claude Code).</param>
    public void SendText(string text, bool appendNewline = true, string newlineChar = "\r", bool useUserInput = false)
    {
        try
        {
            var termPty = _easyTerminalControl?.ConPTYTerm;
            if (termPty == null || !IsProcessRunning())
                return;

            if (useUserInput)
            {
                // Focus first
                _easyTerminalControl?.Focus();

                // Send text + newline via the internal UserInput method
                // This properly triggers key handling for apps like Claude Code
                // Use BeginInvoke to allow focus to settle before sending input
                var textToSend = appendNewline ? text + "\r" : text;
                _easyTerminalControl?.Dispatcher.BeginInvoke(() =>
                {
                    Console.WriteLine($"[TerminalSession] Sending via UserInput: '{textToSend.Replace("\r", "\\r")}'");
                    SendViaUserInput(textToSend);
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                // Use WriteToTerm for standard shell commands
                var textToSend = appendNewline ? text + newlineChar : text;
                termPty.WriteToTerm(textToSend.AsSpan());
            }
        }
        catch
        {
            // Ignore errors when sending text
        }
    }

    /// <summary>
    /// Sends text to the terminal (without executing - user presses Enter manually).
    /// </summary>
    private void SendViaUserInput(string input)
    {
        if (_easyTerminalControl == null) return;

        try
        {
            var conPtyTerm = _easyTerminalControl.ConPTYTerm;
            if (conPtyTerm == null) return;

            // Strip any newline characters - user will press Enter manually
            var textOnly = input.TrimEnd('\r', '\n');
            if (!string.IsNullOrEmpty(textOnly))
            {
                conPtyTerm.WriteToTerm(textOnly.AsSpan());
            }
        }
        catch
        {
            // Silently ignore errors
        }
    }

    /// <summary>
    /// Focuses the terminal control.
    /// </summary>
    public void Focus()
    {
        try
        {
            _easyTerminalControl?.Focus();
        }
        catch
        {
            // Ignore focus errors
        }
    }

    public void Dispose()
    {
        Terminate();
    }
}
