using System.IO;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class SessionManager
{
    private readonly List<TerminalSession> _sessions = new();
    private readonly ProfileRegistry _profileRegistry;

    public IReadOnlyList<TerminalSession> ActiveSessions => _sessions.AsReadOnly();
    public ProfileRegistry ProfileRegistry => _profileRegistry;

    public SessionManager(ProfileRegistry profileRegistry)
    {
        _profileRegistry = profileRegistry;
    }

    public event EventHandler<TerminalSession>? SessionCreated;
    public event EventHandler<TerminalSession>? SessionClosed;

    public TerminalSession CreateSession(Profile profile)
    {
        var session = new TerminalSession(profile);
        _sessions.Add(session);
        SessionCreated?.Invoke(this, session);
        return session;
    }

    public void TrackSession(TerminalSession session)
    {
        if (!_sessions.Contains(session))
        {
            _sessions.Add(session);
            SessionCreated?.Invoke(this, session);
        }
    }

    public TerminalSession CreateAdHocSession(string command, string workingDir)
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = ExtractNameFromCommand(command),
            Command = command,
            WorkingDir = workingDir,
            AutoStart = false
        };

        return CreateSession(profile);
    }

    public void CloseSession(Guid sessionId)
    {
        var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session != null)
        {
            session.Dispose();
            _sessions.Remove(session);
            SessionClosed?.Invoke(this, session);
        }
    }

    public void CloseSession(TerminalSession session)
    {
        CloseSession(session.Id);
    }

    public TerminalSession? GetSession(Guid sessionId)
    {
        return _sessions.FirstOrDefault(s => s.Id == sessionId);
    }

    public void CloseAllSessions()
    {
        foreach (var session in _sessions.ToList())
        {
            session.Dispose();
            _sessions.Remove(session);
            SessionClosed?.Invoke(this, session);
        }
    }

    private static string ExtractNameFromCommand(string command)
    {
        // Extract a readable name from the command
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "Terminal";

        var executable = Path.GetFileNameWithoutExtension(parts[0]);
        return string.IsNullOrEmpty(executable) ? "Terminal" : executable;
    }
}
