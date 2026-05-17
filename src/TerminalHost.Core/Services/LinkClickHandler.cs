using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Routes a Ctrl+Click gesture from a terminal to either a file-preview
/// request (for file paths) or an OpenLink call (for URLs). Scans the
/// most-recent terminal output backwards, word-by-word then line-by-line,
/// and fires for the first detected link.
///
/// Thread affinity: UI/dispatcher thread. Stateless across calls.
/// </summary>
public sealed class LinkClickHandler
{
    private readonly ILinkDetectionService _linkDetectionService;

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;

    public LinkClickHandler(ILinkDetectionService linkDetectionService)
    {
        _linkDetectionService = linkDetectionService;
    }

    public void Handle(string recentOutput, string workingDirectory)
    {
        if (string.IsNullOrEmpty(recentOutput)) return;

        var lines = recentOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Reverse())
        {
            var cleanLine = line.Trim();
            if (string.IsNullOrEmpty(cleanLine)) continue;

            var words = cleanLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                var link = _linkDetectionService.DetectLink(word, workingDirectory);
                if (link != null)
                {
                    Dispatch(link);
                    return;
                }
            }

            // Also try the whole line in case it's a file path with spaces.
            var linkFromLine = _linkDetectionService.DetectLink(cleanLine, workingDirectory);
            if (linkFromLine != null)
            {
                Dispatch(linkFromLine);
                return;
            }
        }
    }

    private void Dispatch(string link)
    {
        if (_linkDetectionService.IsFilePath(link))
        {
            var (path, line, column) = FilePathPositionParser.Parse(link);
            FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
            {
                FilePath = path,
                Line = line,
                Column = column,
            });
        }
        else
        {
            _linkDetectionService.OpenLink(link);
        }
    }
}
