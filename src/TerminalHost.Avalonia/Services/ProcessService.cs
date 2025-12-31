using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TerminalHost.Services;

/// <summary>
/// Cross-platform process service for Avalonia.
/// </summary>
public class ProcessService : IProcessService
{
    public void Start(string fileName)
    {
        Process.Start(new ProcessStartInfo(fileName)
        {
            UseShellExecute = true
        });
    }

    public void Start(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true
        });
    }

    public void Start(ProcessStartInfo startInfo)
    {
        Process.Start(startInfo);
    }

    public void OpenFolder(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path)
            {
                UseShellExecute = true
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", path);
        }
    }
}
