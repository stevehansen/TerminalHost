using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface ISingleInstanceService : IDisposable
{
    event EventHandler<CommandLineArgs>? CommandReceived;
    bool TryAcquireLock();
    void StartPipeServer();
}
