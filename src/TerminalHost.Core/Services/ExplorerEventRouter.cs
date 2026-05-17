using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Services;

/// <summary>
/// Translates raw FileExplorerViewModel events into the host-level events
/// MainViewModel re-raises to its views. Owns the FileViewer mode-to-args
/// mapping (Preview vs Edit) and the History/Blame forwarders. Pop-out and
/// Rename remain inline at each MainViewModel because their bodies depend
/// on host-specific concrete types (FileViewerWindow on WPF, explorerVm
/// concrete cast for Rename).
///
/// Thread affinity: UI/dispatcher thread only. No synchronization.
/// </summary>
public sealed class ExplorerEventRouter
{
    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileHistoryRequestedEventArgs>? FileHistoryRequested;
    public event EventHandler<FileBlameRequestedEventArgs>? FileBlameRequested;

    public void HandleFileViewerRequested(FileViewerRequestedEventArgs e)
    {
        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
        {
            FilePath = e.FilePath,
            Line = 0,
            Column = 0,
            OpenInEditMode = e.Mode != FileViewerMode.Preview
        });
    }

    public void HandleFileHistoryRequested(FileHistoryRequestedEventArgs e) =>
        FileHistoryRequested?.Invoke(this, e);

    public void HandleFileBlameRequested(FileBlameRequestedEventArgs e) =>
        FileBlameRequested?.Invoke(this, e);
}
