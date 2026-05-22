using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Tests.TestAdapters;

/// <summary>
/// In-memory <see cref="IPanelSurface"/> adapter. Records mount/unmount/focus calls
/// and exposes a helper to raise <c>DismissRequested</c> from tests.
/// </summary>
public sealed class InMemoryPanelSurface : IPanelSurface
{
    public PanelZone Zone { get; init; }
    public PanelScope Scope { get; init; }

    public IPanelableViewModel? Mounted { get; private set; }
    public int Mounts { get; private set; }
    public int Unmounts { get; private set; }
    public int Focuses { get; private set; }
    public PanelMountOptions? LastMountOptions { get; private set; }

    /// <summary>When set to non-null, the next call to <see cref="Mount"/> throws this exception.</summary>
    public Exception? MountException { get; set; }

    /// <summary>
    /// When true, <see cref="MountException"/> is not cleared after being thrown — every subsequent
    /// <see cref="Mount"/> call continues to throw the same exception until the test resets it.
    /// </summary>
    public bool MountExceptionIsPermanent { get; set; }

    public event EventHandler<PanelDismissEventArgs>? DismissRequested;

    public void Mount(IPanelableViewModel vm, PanelMountOptions options)
    {
        if (MountException is { } ex)
        {
            if (!MountExceptionIsPermanent)
                MountException = null;
            throw ex;
        }
        Mounted = vm;
        LastMountOptions = options;
        Mounts++;
    }

    public void Unmount(string panelId)
    {
        if (Mounted?.PanelId == panelId) Mounted = null;
        Unmounts++;
    }

    public void Focus(string panelId) => Focuses++;

    public bool IsMounted(string panelId) => Mounted?.PanelId == panelId;

    /// <summary>Test helper: simulates a surface-initiated dismissal (Escape, click-outside, etc.).</summary>
    public void RaiseDismiss(string panelId, PanelDismissTrigger trigger = PanelDismissTrigger.Escape) =>
        DismissRequested?.Invoke(this, new PanelDismissEventArgs(panelId, trigger));
}
