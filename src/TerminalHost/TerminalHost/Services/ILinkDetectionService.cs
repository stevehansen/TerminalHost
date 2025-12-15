using TerminalHost.Services;

namespace TerminalHost.Services;

public interface ILinkDetectionService
{
    string? DetectLink(string text, string? workingDirectory = null);
    List<DetectedLink> DetectAllLinks(string text, string? workingDirectory = null, int maxLinks = 20);
    void OpenLink(string link);
    bool IsFilePath(string link);
}
