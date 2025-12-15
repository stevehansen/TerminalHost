namespace TerminalHost.Services;

public interface IRunUrlDetectionService
{
    string? DetectUrl(string output, string? customPattern = null);
    List<string> DetectAllUrls(string output, string? customPattern = null);
    void OpenInBrowser(string url);
}
