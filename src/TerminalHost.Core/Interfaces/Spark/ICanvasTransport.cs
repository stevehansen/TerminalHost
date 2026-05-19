using System;
using System.Threading.Tasks;
using TerminalHost.Core.Spark;

namespace TerminalHost.Core.Interfaces.Spark;

/// <summary>
/// Bidirectional canvas channel. Production adapters wrap WebView2 (WPF) or
/// Avalonia.WebView. Test adapter records outbound messages and exposes
/// <c>Inject(CanvasInbound)</c> for inputs.
/// </summary>
/// <remarks>
/// The adapter owns:
/// <list type="bullet">
///   <item>UI-thread marshaling (orchestrator callbacks arrive on a single logical thread)</item>
///   <item>The ready-handshake and pre-ready outbound queue</item>
///   <item>JSON serialization in both directions</item>
/// </list>
/// </remarks>
public interface ICanvasTransport
{
    /// <summary>
    /// Sends a message to the canvas. Safe to call before <see cref="IsReady"/> —
    /// pre-ready messages are queued by the adapter and flushed when ready.
    /// </summary>
    Task SendAsync(CanvasOutbound message);

    /// <summary>
    /// Marshals <paramref name="action"/> onto the UI thread the orchestrator runs on.
    /// Used by the orchestrator to hop incoming background events (e.g. activity events)
    /// without depending on Dispatcher / Avalonia / WebView2.
    /// </summary>
    void Post(Action action);

    /// <summary>Raised on the UI thread when the canvas posts a message to the host.</summary>
    event EventHandler<CanvasInbound>? Received;

    /// <summary>Raised on the UI thread once, when the canvas reports ready.</summary>
    event EventHandler? Ready;

    /// <summary>True once the canvas has reported ready.</summary>
    bool IsReady { get; }
}
