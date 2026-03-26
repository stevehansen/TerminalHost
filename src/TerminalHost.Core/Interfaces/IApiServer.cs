using System;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Lightweight HTTP server for the REST API and SSE endpoints.
/// </summary>
public interface IApiServer : IDisposable
{
    /// <summary>Start listening on the configured port.</summary>
    Task StartAsync();

    /// <summary>Stop the server gracefully.</summary>
    Task StopAsync();

    /// <summary>Whether the server is currently listening.</summary>
    bool IsRunning { get; }

    /// <summary>The base URL the server is listening on.</summary>
    string? BaseUrl { get; }

    /// <summary>Number of active SSE connections.</summary>
    int ActiveSseConnections { get; }

    /// <summary>
    /// Fired when a hook event is received via the /api/hooks/:type endpoint (container proxy).
    /// App.xaml.cs subscribes to route these through the same pipeline as CLI hooks.
    /// </summary>
    event EventHandler<HookEvent>? HookEventReceived;
}
