using System.Diagnostics;

namespace TerminalHost.Core.Interfaces;

public interface IProcessService
{
    void Start(string fileName);
    void Start(string fileName, string arguments) => Start(new ProcessStartInfo(fileName, arguments));
    void Start(ProcessStartInfo startInfo);
}
