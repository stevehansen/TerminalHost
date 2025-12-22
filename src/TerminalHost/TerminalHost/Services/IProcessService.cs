using System.Diagnostics;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

internal sealed class ProcessService : IProcessService
{
    public void Start(string fileName) => Process.Start(fileName);
    public void Start(string fileName, string arguments) => Process.Start(new ProcessStartInfo(fileName, arguments));
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
