using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface ISessionManager
{
    IReadOnlyList<TerminalSession> ActiveSessions { get; }
    IProfileRegistry ProfileRegistry { get; }

    event EventHandler<TerminalSession>? SessionCreated;
    event EventHandler<TerminalSession>? SessionClosed;

    TerminalSession CreateSession(Profile profile);
    void TrackSession(TerminalSession session);
    TerminalSession CreateAdHocSession(string command, string workingDir);
    void CloseSession(Guid sessionId);
    void CloseSession(TerminalSession session);
    TerminalSession? GetSession(Guid sessionId);
    void CloseAllSessions();
}
