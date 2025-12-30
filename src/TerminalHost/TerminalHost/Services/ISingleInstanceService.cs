using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface ISingleInstanceService : IDisposable
{
    event EventHandler<CommandLineArgs>? CommandReceived;
    bool TryAcquireLock();
    void StartPipeServer();
}
