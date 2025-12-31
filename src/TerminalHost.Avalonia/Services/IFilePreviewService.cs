namespace TerminalHost.Services;

public interface IFilePreviewService
{
    FilePreviewResult? LoadFilePreview(string filePath, int? highlightLine = null);
}
