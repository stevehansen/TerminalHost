using System.Diagnostics;

namespace TerminalHost.Services;

public interface IProcessService
{
    void Start(string fileName);
    void Start(string fileName, string arguments) => Start(new ProcessStartInfo(fileName, arguments));
    void Start(ProcessStartInfo startInfo);

    /// <summary>
    /// Opens a folder in the system file browser.
    /// </summary>
    void OpenFolder(string path);

    /// <summary>
    /// Reveals a file in the system file browser (Finder on macOS).
    /// </summary>
    void RevealInFinder(string filePath);

    /// <summary>
    /// Opens a URL in the default browser.
    /// </summary>
    void OpenUrl(string url);

    /// <summary>
    /// Opens a file with its default application.
    /// </summary>
    void OpenWithDefaultApp(string filePath);
}

internal sealed class ProcessService : IProcessService
{
    public void Start(string fileName) => Process.Start(fileName);
    public void Start(string fileName, string arguments) => Process.Start(new ProcessStartInfo(fileName, arguments));
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);

    public void OpenFolder(string path)
    {
        // macOS: use 'open' command
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false
        });
    }

    public void RevealInFinder(string filePath)
    {
        // macOS: open -R reveals file in Finder
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"-R \"{filePath}\"",
            UseShellExecute = false
        });
    }

    public void OpenUrl(string url)
    {
        // macOS: open command handles URLs
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = url,
            UseShellExecute = false
        });
    }

    public void OpenWithDefaultApp(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{filePath}\"",
            UseShellExecute = false
        });
    }
}
