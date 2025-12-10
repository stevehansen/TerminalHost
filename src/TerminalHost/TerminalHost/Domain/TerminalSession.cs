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

    public void Dispose()
    {
        Terminate();
    }
}
