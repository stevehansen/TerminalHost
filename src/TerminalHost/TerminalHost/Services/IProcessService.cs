using System.Diagnostics;

namespace TerminalHost.Services;

public interface IProcessService
{
    void Start(string fileName);
    void Start(string fileName, string arguments) => Start(new ProcessStartInfo(fileName, arguments));
    void Start(ProcessStartInfo startInfo);
}

internal sealed class ProcessService : IProcessService
{
    public void Start(string fileName) => Process.Start(fileName);
    public void Start(string fileName, string arguments) => Process.Start(new ProcessStartInfo(fileName, arguments));
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
