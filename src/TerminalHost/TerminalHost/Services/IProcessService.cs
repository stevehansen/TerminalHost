using System.Diagnostics;

namespace TerminalHost.Services;

public interface IProcessService
{
    void Start(string fileName);
    void Start(ProcessStartInfo startInfo);
}

internal sealed class ProcessService : IProcessService
{
    public void Start(string fileName) => Process.Start(fileName);
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
