using System.Diagnostics;
using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Services;

internal sealed class ProcessService : IProcessService
{
    public void Start(string fileName) => Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
    public void Start(string fileName, string arguments) => Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);

    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", path)
        {
            UseShellExecute = true
        });
    }

    public void RevealInFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        if (!File.Exists(filePath))
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                OpenFolder(directory);
            }
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
        {
            UseShellExecute = true
        });
    }
}
