using System.Diagnostics;

namespace TerminalHost.Core.Interfaces;

public interface IProcessService
{
    void Start(string fileName);
    void Start(string fileName, string arguments) => Start(new ProcessStartInfo(fileName, arguments));
    void Start(ProcessStartInfo startInfo);

    /// <summary>
    /// Opens a folder in the system file manager.
    /// </summary>
    void OpenFolder(string path) { }

    /// <summary>
    /// Opens the folder containing the specified file and selects it in the file manager.
    /// </summary>
    void RevealInFolder(string filePath) { }
}
